const DUBAI_OFFSET_MS = 4 * 60 * 60 * 1000; // Dubai is UTC+4 all year (no daylight saving).

const DOCTORS = Object.freeze({
  'General Physician': 'Dr. Sara Ahmed',
  ENT: 'Dr. Omar Khan',
  Dentist: 'Dr. Lina Haddad',
  Cardiologist: 'Dr. Raj Mehta'
});

const TIME_SLOTS = Object.freeze(
  Array.from({ length: 24 }, (_, index) => {
    const minutes = 9 * 60 + index * 30;
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
  })
);

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const LONG_DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const LONG_MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

function dubaiNow(now = new Date()) {
  const shifted = new Date(now.getTime() + DUBAI_OFFSET_MS);
  return {
    date: shifted.toISOString().slice(0, 10),
    minutes: shifted.getUTCHours() * 60 + shifted.getUTCMinutes()
  };
}

function dubaiToday(now = new Date()) {
  return dubaiNow(now).date;
}

function parseDate(value) {
  if (typeof value !== 'string' || !DATE_PATTERN.test(value)) return null;
  const date = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(date.getTime()) || date.toISOString().slice(0, 10) !== value) return null;
  return date;
}

function isOpenDay(dateString) {
  const date = parseDate(dateString);
  return date !== null && date.getUTCDay() !== 0;
}

function isBookableDate(dateString, now = new Date()) {
  return isOpenDay(dateString) && dateString >= dubaiToday(now);
}

function isValidAppointmentTime(dateString, time, now = new Date()) {
  if (!isOpenDay(dateString) || !TIME_SLOTS.includes(time)) return false;
  const current = dubaiNow(now);
  if (dateString !== current.date) return dateString > current.date;
  const [hours, minutes] = time.split(':').map(Number);
  return hours * 60 + minutes > current.minutes;
}

function displayTime(time) {
  if (!TIME_SLOTS.includes(time)) return time;
  const [hours, minutes] = time.split(':').map(Number);
  const suffix = hours >= 12 ? 'PM' : 'AM';
  return `${hours % 12 || 12}:${String(minutes).padStart(2, '0')} ${suffix}`;
}

function displayDate(dateString) {
  const date = parseDate(dateString);
  if (!date) return dateString;
  return `${DAY_NAMES[date.getUTCDay()]}, ${date.getUTCDate()} ${MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
}

function displayLongDate(dateString) {
  const date = parseDate(dateString);
  if (!date) return dateString;
  return `${LONG_DAY_NAMES[date.getUTCDay()]}, ${date.getUTCDate()} ${LONG_MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
}

module.exports = {
  DOCTORS,
  TIME_SLOTS,
  dubaiNow,
  dubaiToday,
  parseDate,
  isBookableDate,
  isValidAppointmentTime,
  displayTime,
  displayDate,
  displayLongDate
};
