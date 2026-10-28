const { ready } = require('../data/db');
const { dubaiToday } = require('./bookingSchedule');

const RECEPTION_TRANSITIONS = Object.freeze({
  Arrived: ['Booked'],
  Done: ['Arrived'],
  'No-show': ['Booked', 'Arrived']
});

function toAppointment(row) {
  if (!row) return null;
  return {
    id: Number(row.id),
    specialty: row.specialty,
    doctor: row.doctor,
    date: row.appointment_date,
    time: row.appointment_time,
    patientName: row.patient_name,
    patientPhone: row.patient_phone,
    patientEmail: row.patient_email,
    status: row.status,
    stripeSessionId: row.stripe_session_id ?? null,
    depositPaidAt: row.deposit_paid_at ?? null
  };
}

function isUniqueViolation(error) {
  const text = `${error?.code ?? ''} ${error?.message ?? ''} ${error?.cause?.message ?? ''}`;
  return /SQLITE_CONSTRAINT|UNIQUE constraint failed/i.test(text);
}

async function createBooking(input) {
  const db = await ready();
  try {
    const result = await db.execute({
      sql: `INSERT INTO appointments
        (specialty, doctor, appointment_date, appointment_time, patient_name, patient_phone, patient_email, status)
        VALUES (?, ?, ?, ?, ?, ?, ?, 'Pending') RETURNING *`,
      args: [input.specialty, input.doctor, input.date, input.time, input.patientName, input.patientPhone, input.patientEmail]
    });
    return toAppointment(result.rows[0]);
  } catch (error) {
    if (isUniqueViolation(error)) return null;
    throw error;
  }
}

async function getBookingsByEmail(email) {
  const db = await ready();
  const result = await db.execute({
    sql: `SELECT * FROM appointments WHERE patient_email = ?
      ORDER BY appointment_date DESC, appointment_time DESC, id DESC`,
    args: [String(email).toLowerCase()]
  });
  return result.rows.map(toAppointment);
}

async function getBookingsByDate(date) {
  const db = await ready();
  const result = await db.execute({
    sql: 'SELECT * FROM appointments WHERE appointment_date = ? ORDER BY doctor, appointment_time, id',
    args: [date]
  });
  return result.rows.map(toAppointment);
}

async function getUnavailableSlots(doctor, date) {
  const db = await ready();
  const result = await db.execute({
    sql: `SELECT appointment_time FROM appointments
      WHERE doctor = ? AND appointment_date = ? AND status IN ('Booked', 'Pending')`,
    args: [doctor, date]
  });
  return result.rows.map((row) => row.appointment_time);
}

async function getBooking(id) {
  const db = await ready();
  const result = await db.execute({ sql: 'SELECT * FROM appointments WHERE id = ?', args: [id] });
  return toAppointment(result.rows[0]);
}

async function getBookingByStripeSession(sessionId) {
  const db = await ready();
  const result = await db.execute({ sql: 'SELECT * FROM appointments WHERE stripe_session_id = ?', args: [sessionId] });
  return toAppointment(result.rows[0]);
}

async function setStripeSession(id, sessionId) {
  const db = await ready();
  const result = await db.execute({
    sql: `UPDATE appointments SET stripe_session_id = ?
      WHERE id = ? AND status = 'Pending' AND stripe_session_id IS NULL`,
    args: [sessionId, id]
  });
  return result.rowsAffected === 1;
}

async function markDepositPaid(id, sessionId) {
  const db = await ready();
  const result = await db.execute({
    sql: `UPDATE appointments SET status = 'Booked', deposit_paid_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
      WHERE id = ? AND stripe_session_id = ? AND status = 'Pending'`,
    args: [id, sessionId]
  });
  return result.rowsAffected === 1;
}

async function cancelPatientBooking(id, email) {
  const db = await ready();
  const result = await db.execute({
    sql: `UPDATE appointments SET status = 'Cancelled'
      WHERE id = ? AND patient_email = ? AND status = 'Booked'`,
    args: [id, String(email).toLowerCase()]
  });
  return result.rowsAffected === 1;
}

async function updateReceptionStatus(id, status, now = new Date()) {
  const allowedFrom = RECEPTION_TRANSITIONS[status];
  if (!allowedFrom) return false;
  const db = await ready();
  const result = await db.execute({
    sql: `UPDATE appointments SET status = ?
      WHERE id = ? AND appointment_date = ? AND status IN (${allowedFrom.map(() => '?').join(', ')})`,
    args: [status, id, dubaiToday(now), ...allowedFrom]
  });
  return result.rowsAffected === 1;
}

module.exports = {
  RECEPTION_STATUSES: Object.keys(RECEPTION_TRANSITIONS),
  createBooking,
  getBookingsByEmail,
  getBookingsByDate,
  getUnavailableSlots,
  getBooking,
  getBookingByStripeSession,
  setStripeSession,
  markDepositPaid,
  cancelPatientBooking,
  updateReceptionStatus
};
