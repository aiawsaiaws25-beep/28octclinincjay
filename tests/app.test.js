const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const dbFile = path.join(os.tmpdir(), `lakeside-test-${process.pid}-${Date.now()}.db`);
process.env.DATABASE_URL = `file:${dbFile.replace(/\\/g, '/')}`;
process.env.RECEPTION_PASSWORD = 'Reception-Test-123!';
process.env.SESSION_SECRET = 'test-session-secret-that-is-at-least-32-chars';
process.env.BASE_URL = 'http://localhost:5221';
delete process.env.ANTHROPIC_API_KEY;
delete process.env.STRIPE_SECRET_KEY;
delete process.env.TURSO_DATABASE_URL;
delete process.env.VERCEL;

const request = require('supertest');
const app = require('../app');
const db = require('../data/db');
const schedule = require('../services/bookingSchedule');
const appointments = require('../services/appointmentService');
const deposits = require('../services/depositService');
const assistant = require('../services/assistantService');

const stripeSessions = new Map();
const fakeStripe = {
  checkout: {
    sessions: {
      async create(params, options) {
        const id = `cs_test_${params.metadata.appointmentId}`;
        assert.equal(options.idempotencyKey, `appointment-deposit-${params.metadata.appointmentId}`);
        assert.equal(params.line_items[0].price_data.unit_amount, 10000);
        assert.equal(params.success_url, 'http://localhost:5221/booking-success?session_id={CHECKOUT_SESSION_ID}');
        stripeSessions.set(id, {
          id, livemode: false, payment_status: 'paid', mode: 'payment', currency: 'aed', amount_total: 10000,
          client_reference_id: params.client_reference_id, metadata: params.metadata
        });
        return { id, url: `https://checkout.stripe.test/${id}` };
      },
      async retrieve(id) {
        return stripeSessions.get(id);
      }
    }
  }
};

function removeQuietly(file) {
  try { fs.rmSync(file, { force: true }); } catch { /* libSQL may keep the file open briefly on Windows. */ }
}

function csrfFrom(html) {
  const match = /name="_csrf" value="([^"]+)"/.exec(html);
  assert.ok(match, 'page should contain a CSRF token');
  return match[1];
}

async function csrf(agent, url = '/') {
  return csrfFrom((await agent.get(url)).text);
}

function nextMonday() {
  const date = new Date(`${schedule.dubaiToday()}T00:00:00Z`);
  do date.setUTCDate(date.getUTCDate() + 1); while (date.getUTCDay() !== 1);
  return date.toISOString().slice(0, 10);
}

async function register(agent, email, password = 'Test-Patient-123!') {
  const token = await csrf(agent, '/Account/Register');
  return agent.post('/Account/Register').type('form')
    .send({ _csrf: token, email, password, confirmPassword: password, returnUrl: '/' });
}

async function login(agent, email, password) {
  const token = await csrf(agent, '/Account/Login');
  return agent.post('/Account/Login').type('form').send({ _csrf: token, email, password, returnUrl: '/' });
}

let server;

before(async () => {
  deposits.setStripeClient(fakeStripe);
  await db.ready();
  server = await new Promise((resolve) => { const s = app.listen(0, '127.0.0.1', () => resolve(s)); });
});

after(async () => {
  await new Promise((resolve) => server.close(resolve));
  db.close();
  removeQuietly(dbFile);
});

test('schedule covers opening hours in 30-minute steps', () => {
  assert.equal(schedule.TIME_SLOTS.length, 24);
  assert.equal(schedule.TIME_SLOTS[0], '09:00');
  assert.equal(schedule.TIME_SLOTS.at(-1), '20:30');
  assert.equal(Object.keys(schedule.DOCTORS).length, 4);
});

test('schedule rejects past, closed and off-grid times', () => {
  const now = new Date('2030-01-07T00:00:00Z');
  const cases = [
    ['2030-01-07', '09:00', true], ['2030-01-07', '20:30', true], ['2030-01-07', '21:00', false],
    ['2030-01-07', '08:30', false], ['2030-01-07', '09:15', false], ['2030-01-06', '09:00', false],
    ['2030-01-13', '09:00', false], ['2029-12-31', '09:00', false], ['2030-02-30', '09:00', false],
    ['bad', '09:00', false], ['2030-01-07', 'bad-time', false]
  ];
  for (const [date, time, expected] of cases) {
    assert.equal(schedule.isValidAppointmentTime(date, time, now), expected, `${date} ${time}`);
  }
  assert.equal(schedule.isValidAppointmentTime('2030-01-07', '09:00', new Date('2030-01-07T05:00:00Z')), false);
});

test('home page renders with chat assistant and footer credit', async () => {
  const response = await request(server).get('/');
  assert.equal(response.status, 200);
  assert.match(response.text, /Expert care,/);
  assert.match(response.text, /Ask Lakeside/);
  assert.match(response.text, /built at London International/);
  assert.match(response.text, /href="\/Booking\?specialty=General%20Physician"/);
});

test('role column is added to an existing users table when missing', async () => {
  const legacyFile = path.join(os.tmpdir(), `lakeside-legacy-${process.pid}.db`);
  const { createClient } = require('@libsql/client');
  const legacy = createClient({ url: `file:${legacyFile.replace(/\\/g, '/')}` });
  await legacy.execute('CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE COLLATE NOCASE, password_hash TEXT NOT NULL, failed_logins INTEGER NOT NULL DEFAULT 0, lockout_until TEXT, created_at TEXT)');
  await legacy.execute("INSERT INTO users (email, password_hash) VALUES ('old@example.test', 'x')");
  legacy.close();

  db.close();
  const original = process.env.DATABASE_URL;
  process.env.DATABASE_URL = `file:${legacyFile.replace(/\\/g, '/')}`;
  try {
    const migrated = await db.ready();
    const rows = (await migrated.execute('SELECT email, role FROM users ORDER BY id')).rows;
    assert.deepEqual(rows.map((r) => [r.email, r.role]), [['old@example.test', 'patient'], ['reception@lakeside.local', 'reception']]);
    await db.ready();
  } finally {
    db.close();
    process.env.DATABASE_URL = original;
    removeQuietly(legacyFile);
    await db.ready();
  }
});

test('booking flow takes a Stripe deposit and patients manage their own bookings', async () => {
  const alice = request.agent(server);
  const bob = request.agent(server);
  const aliceEmail = `alice-${Date.now()}@example.test`;
  assert.equal((await register(alice, aliceEmail)).status, 302);
  assert.equal((await register(bob, `bob-${Date.now()}@example.test`)).status, 302);

  const date = nextMonday();
  let page = await alice.get('/Booking?specialty=ENT');
  assert.match(page.text, /value="ENT" required checked/);
  let token = csrfFrom(page.text);
  const step2 = await alice.post('/Booking').type('form').send({ _csrf: token, step: 1, handler: 'Next', specialty: 'General Physician' });
  assert.match(step2.text, /2\. Choose a doctor/);
  const bad = await alice.post('/Booking').type('form').send({ _csrf: token, step: 2, handler: 'Next', specialty: 'General Physician', doctor: 'Dr. Omar Khan' });
  assert.match(bad.text, /Choose the doctor for your selected specialty\./);
  const slots = await alice.post('/Booking').type('form').send({
    _csrf: token, step: 3, handler: 'Slots', specialty: 'General Physician', doctor: 'Dr. Sara Ahmed', date
  });
  assert.match(slots.text, /value="09:00" required \/>/);

  const booking = {
    _csrf: token, step: 5, handler: 'Confirm', specialty: 'General Physician', doctor: 'Dr. Sara Ahmed', date, time: '09:00',
    patientName: 'Alice Patient', patientPhone: '+971 50 123 4567', patientEmail: 'spoofed@example.test'
  };
  const confirm = await alice.post('/Booking').type('form').send(booking);
  assert.equal(confirm.status, 303);
  assert.match(confirm.headers.location, /^https:\/\/checkout\.stripe\.test\/cs_test_\d+$/);
  const sessionId = confirm.headers.location.split('/').pop();

  const stored = await appointments.getBookingByStripeSession(sessionId);
  assert.equal(stored.status, 'Pending');
  assert.equal(stored.patientEmail, aliceEmail);

  const bobToken = await csrf(bob, '/Booking');
  const duplicate = await bob.post('/Booking').type('form').send({ ...booking, _csrf: bobToken });
  assert.match(duplicate.text, /That time was just booked/);

  const successUrl = `/booking-success?session_id=${sessionId}`;
  const success = await alice.get(successUrl);
  assert.equal(success.status, 200);
  assert.match(success.text, /Booking confirmed/);
  assert.equal(success.headers['referrer-policy'], 'no-referrer');
  assert.equal((await appointments.getBooking(stored.id)).status, 'Booked');
  assert.equal((await request(server).get('/booking-success?session_id=bad')).status, 400);
  assert.equal((await request(server).get('/booking-success?session_id=cs_test_unknown')).status, 404);

  const aliceList = await alice.get('/MyAppointments');
  assert.match(aliceList.text, new RegExp(`LM-${stored.id}`));
  const bobList = await bob.get('/MyAppointments');
  assert.doesNotMatch(bobList.text, new RegExp(`LM-${stored.id}<`));
  const bobCancel = await bob.post('/MyAppointments/cancel').type('form').send({ _csrf: csrfFrom(bobList.text), id: stored.id });
  assert.equal(bobCancel.status, 404);
  const noCsrf = await alice.post('/MyAppointments/cancel').type('form').send({ id: stored.id });
  assert.equal(noCsrf.status, 400);
  const cancel = await alice.post('/MyAppointments/cancel').type('form').send({ _csrf: csrfFrom(aliceList.text), id: stored.id });
  assert.equal(cancel.status, 302);
  assert.match((await alice.get('/MyAppointments')).text, /Cancelled/);
  assert.match((await alice.get(successUrl)).text, /This appointment is cancelled/);

  const logoutToken = await csrf(alice, '/MyAppointments');
  const logout = await alice.post('/Account/Logout').type('form').send({ _csrf: logoutToken });
  assert.equal(logout.status, 302);
  const requiresLogin = await alice.get('/MyAppointments');
  assert.equal(requiresLogin.status, 302);
  assert.match(requiresLogin.headers.location, /\/Account\/Login\?returnUrl=/);
  assert.equal((await login(alice, aliceEmail, 'Test-Patient-123!')).status, 302);
  assert.match((await alice.get('/MyAppointments')).text, /Cancelled/);
});

test('reception dashboard is restricted to the reception role and updates statuses', async () => {
  assert.match((await request(server).get('/reception')).headers.location, /\/Account\/Login\?returnUrl=%2Freception/);

  const patient = request.agent(server);
  await register(patient, `patient-${Date.now()}@example.test`);
  assert.equal((await patient.get('/reception')).status, 403);

  const today = schedule.dubaiToday();
  const created = await appointments.createBooking({
    specialty: 'Dentist', doctor: 'Dr. Lina Haddad', date: today, time: '20:30',
    patientName: 'Reception Test', patientPhone: '+971501234567', patientEmail: 'reception-test@example.test'
  });
  await appointments.setStripeSession(created.id, `cs_test_reception_${created.id}`);
  await appointments.markDepositPaid(created.id, `cs_test_reception_${created.id}`);

  const staff = request.agent(server);
  assert.equal((await login(staff, 'reception@lakeside.local', 'wrong-password')).status, 400);
  const signedIn = await login(staff, 'reception@lakeside.local', process.env.RECEPTION_PASSWORD);
  assert.equal(signedIn.headers.location, '/reception');

  let dashboard = await staff.get('/reception');
  assert.equal(dashboard.status, 200);
  assert.match(dashboard.text, /LAKESIDE MEDICAL RECEPTION &middot; LONDON INTERNATIONAL &middot; LISRC.AE/);
  assert.match(dashboard.text, /by Aisha Khan/);
  assert.match(dashboard.text, /<strong>1<\/strong>\s*<span>appointment today<\/span>/);
  assert.match(dashboard.text, /Dr\. Lina Haddad/);
  assert.match(dashboard.text, /Send reminder/);
  assert.match(dashboard.text, /mailto:reception-test@example\.test\?subject=/);

  const post = async (status) => staff.post('/reception/status').type('form')
    .send({ _csrf: csrfFrom(dashboard.text), id: created.id, status });
  assert.equal((await post('Done')).status, 302);
  assert.equal((await appointments.getBooking(created.id)).status, 'Booked');
  await post('Arrived');
  assert.equal((await appointments.getBooking(created.id)).status, 'Arrived');
  await post('Done');
  assert.equal((await appointments.getBooking(created.id)).status, 'Done');
  assert.equal((await post('Cancelled')).status, 400);
  dashboard = await staff.get('/reception');
  assert.match(dashboard.text, /<span class="reception-status">Done<\/span>/);
});

test('chat endpoint validates input, requires a key and adds the sign-off', async () => {
  assert.equal((await request(server).post('/api/chat').send({ messages: [] })).status, 400);
  assert.equal((await request(server).post('/api/chat').send({ messages: [{ role: 'assistant', content: 'hi' }] })).status, 400);
  assert.equal((await request(server).post('/api/chat').set('Content-Type', 'application/json').send('{bad')).status, 400);

  const missingKey = await request(server).post('/api/chat').send({ messages: [{ role: 'user', content: 'Hours?' }] });
  assert.equal(missingKey.status, 503);

  let captured;
  assistant.setClient({
    messages: {
      async create(params) {
        captured = params;
        return { content: [{ type: 'text', text: 'We are open Monday to Saturday, 9am-9pm.' }] };
      }
    }
  });
  try {
    const response = await request(server).post('/api/chat').send({ messages: [{ role: 'user', content: 'Hours?' }] });
    assert.equal(response.status, 200);
    assert.ok(response.body.reply.endsWith(assistant.SIGN_OFF));
    assert.equal(captured.model, 'claude-haiku-5-5');
    assert.match(captured.system, /Do not give medical advice/);
    assert.match(captured.system, /Dr\. Raj Mehta \(Cardiologist\)/);
  } finally {
    assistant.setClient(undefined);
  }
});
