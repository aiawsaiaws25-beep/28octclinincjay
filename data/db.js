const path = require('node:path');
const bcrypt = require('bcryptjs');
const { createClient } = require('@libsql/client');

const DEFAULT_RECEPTION_EMAIL = 'reception@lakeside.local';
let client;
let readyPromise;

function resolveDatabaseConfig() {
  if (process.env.TURSO_DATABASE_URL) {
    return { url: process.env.TURSO_DATABASE_URL, authToken: process.env.TURSO_AUTH_TOKEN };
  }
  if (process.env.DATABASE_URL) {
    return { url: process.env.DATABASE_URL };
  }
  if (process.env.VERCEL) {
    console.warn('TURSO_DATABASE_URL is not set: using a temporary /tmp database that is reset when the serverless function restarts.');
    return { url: 'file:/tmp/lakeside.db' };
  }
  return { url: `file:${path.join(__dirname, 'lakeside.db').replace(/\\/g, '/')}` };
}

function getClient() {
  if (!client) client = createClient(resolveDatabaseConfig());
  return client;
}

async function createSchema(db) {
  await db.batch([
    `CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      email TEXT NOT NULL UNIQUE COLLATE NOCASE,
      password_hash TEXT NOT NULL,
      failed_logins INTEGER NOT NULL DEFAULT 0,
      lockout_until TEXT,
      created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
    )`,
    `CREATE TABLE IF NOT EXISTS appointments (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      specialty TEXT NOT NULL,
      doctor TEXT NOT NULL,
      appointment_date TEXT NOT NULL,
      appointment_time TEXT NOT NULL,
      patient_name TEXT NOT NULL,
      patient_phone TEXT NOT NULL,
      patient_email TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'Pending',
      stripe_session_id TEXT,
      deposit_paid_at TEXT,
      created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
    )`,
    `CREATE UNIQUE INDEX IF NOT EXISTS ux_appointments_active_slot
      ON appointments (doctor, appointment_date, appointment_time)
      WHERE status IN ('Booked', 'Pending')`,
    `CREATE UNIQUE INDEX IF NOT EXISTS ux_appointments_stripe_session
      ON appointments (stripe_session_id) WHERE stripe_session_id IS NOT NULL`,
    'CREATE INDEX IF NOT EXISTS ix_appointments_email ON appointments (patient_email)',
    'CREATE INDEX IF NOT EXISTS ix_appointments_date ON appointments (appointment_date)'
  ], 'write');

  const columns = await db.execute('PRAGMA table_info(users)');
  if (!columns.rows.some((column) => column.name === 'role')) {
    await db.execute("ALTER TABLE users ADD COLUMN role TEXT NOT NULL DEFAULT 'patient'");
  }
}

async function seedReceptionUser(db) {
  const password = process.env.RECEPTION_PASSWORD;
  const email = (process.env.RECEPTION_EMAIL || DEFAULT_RECEPTION_EMAIL).trim().toLowerCase();
  if (!password) {
    console.warn('RECEPTION_PASSWORD is not set: the reception staff account was not seeded.');
    return;
  }
  if (password.length < 8) {
    console.warn('RECEPTION_PASSWORD must be at least 8 characters: the reception staff account was not seeded.');
    return;
  }

  const existing = await db.execute({ sql: 'SELECT id, password_hash FROM users WHERE email = ?', args: [email] });
  if (existing.rows.length === 0) {
    await db.execute({
      sql: "INSERT INTO users (email, password_hash, role) VALUES (?, ?, 'reception')",
      args: [email, await bcrypt.hash(password, 12)]
    });
    return;
  }

  const user = existing.rows[0];
  const passwordMatches = await bcrypt.compare(password, String(user.password_hash));
  await db.execute({
    sql: "UPDATE users SET role = 'reception', password_hash = ? WHERE id = ?",
    args: [passwordMatches ? user.password_hash : await bcrypt.hash(password, 12), user.id]
  });
}

async function ready() {
  if (!readyPromise) {
    readyPromise = (async () => {
      const db = getClient();
      await createSchema(db);
      await seedReceptionUser(db);
      return db;
    })().catch((error) => {
      readyPromise = undefined;
      throw error;
    });
  }
  return readyPromise;
}

function close() {
  if (client) client.close();
  client = undefined;
  readyPromise = undefined;
}

module.exports = { ready, close, DEFAULT_RECEPTION_EMAIL };
