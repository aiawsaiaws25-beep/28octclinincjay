const bcrypt = require('bcryptjs');
const { ready } = require('../data/db');

const MAX_FAILED_LOGINS = 5;
const LOCKOUT_MINUTES = 5;
// Compared against when the email is unknown so response timing does not reveal registered accounts.
const DUMMY_HASH = bcrypt.hashSync('lakeside-dummy-password', 10);

function toUser(row) {
  return row ? { id: Number(row.id), email: row.email, role: row.role } : null;
}

async function findById(id) {
  const db = await ready();
  const result = await db.execute({ sql: 'SELECT id, email, role FROM users WHERE id = ?', args: [id] });
  return toUser(result.rows[0]);
}

async function createPatient(email, password) {
  const db = await ready();
  try {
    const result = await db.execute({
      sql: "INSERT INTO users (email, password_hash, role) VALUES (?, ?, 'patient') RETURNING id, email, role",
      args: [email, await bcrypt.hash(password, 12)]
    });
    return toUser(result.rows[0]);
  } catch (error) {
    if (/UNIQUE constraint failed|SQLITE_CONSTRAINT/i.test(`${error.code} ${error.message}`)) return null;
    throw error;
  }
}

async function verifyLogin(email, password, now = new Date()) {
  const db = await ready();
  const result = await db.execute({ sql: 'SELECT * FROM users WHERE email = ?', args: [email] });
  const row = result.rows[0];
  if (!row) {
    await bcrypt.compare(password, DUMMY_HASH);
    return { ok: false };
  }
  if (row.lockout_until && new Date(row.lockout_until) > now) {
    return { ok: false, lockedOut: true };
  }
  if (await bcrypt.compare(password, String(row.password_hash))) {
    await db.execute({ sql: 'UPDATE users SET failed_logins = 0, lockout_until = NULL WHERE id = ?', args: [row.id] });
    return { ok: true, user: toUser(row) };
  }
  const failures = Number(row.failed_logins) + 1;
  const lockedOut = failures >= MAX_FAILED_LOGINS;
  await db.execute({
    sql: 'UPDATE users SET failed_logins = ?, lockout_until = ? WHERE id = ?',
    args: [
      lockedOut ? 0 : failures,
      lockedOut ? new Date(now.getTime() + LOCKOUT_MINUTES * 60_000).toISOString() : null,
      row.id
    ]
  });
  return { ok: false, lockedOut };
}

module.exports = { findById, createPatient, verifyLogin };
