const express = require('express');
const appointments = require('../services/appointmentService');
const schedule = require('../services/bookingSchedule');
const { requireAuth, requireRole } = require('../middleware/auth');

const router = express.Router();

function takeFlash(req) {
  const message = req.session.flash;
  if (message) delete req.session.flash;
  return message || null;
}

function parseId(value) {
  const id = Number(value);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

router.get('/MyAppointments', requireAuth, async (req, res, next) => {
  try {
    const list = await appointments.getBookingsByEmail(req.user.email);
    res.set('Cache-Control', 'no-store');
    res.render('my-appointments', {
      title: 'My Appointments',
      statusMessage: takeFlash(req),
      appointments: list.map((a) => ({ ...a, when: `${schedule.displayDate(a.date)} at ${schedule.displayTime(a.time)}` }))
    });
  } catch (error) {
    next(error);
  }
});

router.post('/MyAppointments/cancel', requireAuth, async (req, res, next) => {
  try {
    const id = parseId(req.body.id);
    if (!id || !(await appointments.cancelPatientBooking(id, req.user.email))) {
      return res.status(404).render('error', {
        title: 'Not found',
        message: 'This appointment was not found in your booked appointments.'
      });
    }
    req.session.flash = 'Your appointment has been cancelled.';
    res.redirect('/MyAppointments');
  } catch (error) {
    next(error);
  }
});

router.get('/reception', requireRole('reception'), async (req, res, next) => {
  try {
    const today = schedule.dubaiToday();
    const todays = await appointments.getBookingsByDate(today);
    const groups = new Map();
    for (const appointment of todays) {
      const reminderBody = `Hello ${appointment.patientName}, this is a reminder of your ${appointment.specialty} appointment with ${appointment.doctor} today at ${schedule.displayTime(appointment.time)} at Cluster X, JLT, Dubai. — Lakeside Medical Clinic`;
      const row = {
        ...appointment,
        displayTime: schedule.displayTime(appointment.time),
        reminderHref: appointment.patientEmail
          ? `mailto:${encodeURIComponent(appointment.patientEmail).replace(/%40/g, '@')}?subject=${encodeURIComponent('Appointment reminder - Lakeside Medical Clinic')}&body=${encodeURIComponent(reminderBody)}`
          : null
      };
      if (!groups.has(appointment.doctor)) groups.set(appointment.doctor, []);
      groups.get(appointment.doctor).push(row);
    }
    res.set('Cache-Control', 'no-store');
    res.render('reception', {
      title: 'Reception dashboard',
      clinicDate: schedule.displayLongDate(today),
      appointmentCount: todays.length,
      appointmentsByDoctor: [...groups.entries()].map(([doctor, rows], index) => ({ doctor, rows, headingId: `doctor-${index + 1}` })),
      message: takeFlash(req)
    });
  } catch (error) {
    next(error);
  }
});

router.post('/reception/status', requireRole('reception'), async (req, res, next) => {
  try {
    const id = parseId(req.body.id);
    const status = req.body.status;
    if (!id || !appointments.RECEPTION_STATUSES.includes(status)) {
      return res.status(400).render('error', { title: 'Bad request', message: 'That status update was not valid.' });
    }
    const updated = await appointments.updateReceptionStatus(id, status);
    req.session.flash = updated
      ? `Appointment marked ${status}.`
      : "That appointment's status changed or the requested action is no longer available.";
    res.redirect('/reception');
  } catch (error) {
    next(error);
  }
});

module.exports = router;
