const express = require('express');
const schedule = require('../services/bookingSchedule');
const appointments = require('../services/appointmentService');
const deposits = require('../services/depositService');

const router = express.Router();
const PHONE_PATTERN = /^\+?[0-9][0-9 ()-]{5,28}[0-9]$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const STEP_LABELS = ['Specialty', 'Doctor', 'Date & time', 'Patient details', 'Review'];

function text(value, maxLength = 300) {
  return typeof value === 'string' ? value.trim().slice(0, maxLength) : '';
}

function readInput(req) {
  const body = req.body || {};
  const input = {
    specialty: text(body.specialty, 50),
    doctor: text(body.doctor, 100),
    date: text(body.date, 10),
    time: text(body.time, 5),
    patientName: text(body.patientName, 200),
    patientPhone: text(body.patientPhone, 50),
    patientEmail: text(body.patientEmail, 300).toLowerCase()
  };
  if (req.user) input.patientEmail = req.user.email.toLowerCase();
  return input;
}

function readStep(req) {
  const step = Number.parseInt(req.body?.step, 10);
  return Number.isInteger(step) ? step : 0;
}

function validateStep(step, input) {
  const errors = [];
  const doctor = Object.hasOwn(schedule.DOCTORS, input.specialty) ? schedule.DOCTORS[input.specialty] : null;
  if (!doctor) {
    errors.push({ field: 'specialty', message: 'Choose one of our four specialties.' });
  } else if (step >= 2 && input.doctor !== doctor) {
    errors.push({ field: 'doctor', message: 'Choose the doctor for your selected specialty.' });
  }
  if (step >= 3 && !schedule.isValidAppointmentTime(input.date, input.time)) {
    errors.push({ field: 'date', message: 'Choose a future 30-minute slot, Monday to Saturday, between 9am and 9pm (Dubai time).' });
  }
  if (step >= 4) {
    if (input.patientName.length < 2 || input.patientName.length > 100) {
      errors.push({ field: 'patientName', message: 'Enter your full name (2-100 characters).' });
    }
    if (!PHONE_PATTERN.test(input.patientPhone)) {
      errors.push({ field: 'patientPhone', message: 'Enter a valid phone number, for example +971 50 123 4567.' });
    }
    if (input.patientEmail.length > 254 || !EMAIL_PATTERN.test(input.patientEmail)) {
      errors.push({ field: 'patientEmail', message: 'Enter a valid email address.' });
    }
  }
  return errors;
}

async function renderBooking(req, res, { step, input, errors = [], status = 200 }) {
  let taken = [];
  if (step === 3 && schedule.parseDate(input.date)) {
    taken = await appointments.getUnavailableSlots(input.doctor, input.date);
  }
  const now = new Date();
  const slots = schedule.TIME_SLOTS.map((time) => ({
    value: time,
    label: schedule.displayTime(time),
    unavailable: !schedule.isValidAppointmentTime(input.date, time, now) || taken.includes(time)
  }));
  res.set('Cache-Control', 'no-store');
  res.status(status).render('booking', {
    title: 'Book an appointment',
    step,
    input,
    errors,
    slots,
    labels: STEP_LABELS,
    doctors: schedule.DOCTORS,
    today: schedule.dubaiToday(now),
    reviewDate: schedule.displayDate(input.date),
    reviewTime: schedule.displayTime(input.time)
  });
}

function emptyInput(req) {
  return {
    specialty: '', doctor: '', date: '', time: '',
    patientName: '', patientPhone: '', patientEmail: req.user ? req.user.email : ''
  };
}

router.get('/Booking', async (req, res, next) => {
  try {
    const input = emptyInput(req);
    const specialty = typeof req.query.specialty === 'string' ? req.query.specialty : '';
    if (Object.hasOwn(schedule.DOCTORS, specialty)) input.specialty = specialty;
    await renderBooking(req, res, { step: 1, input });
  } catch (error) {
    next(error);
  }
});

router.post('/Booking', async (req, res, next) => {
  try {
    const input = readInput(req);
    let step = readStep(req);
    const handler = text(req.body?.handler, 20);

    if (step < 1 || step > 5) {
      return renderBooking(req, res, {
        step: 1, input, errors: [{ field: '', message: 'The booking step was invalid. Please start again.' }]
      });
    }

    if (handler === 'Back') {
      if (step < 2) {
        return renderBooking(req, res, {
          step: 1, input, errors: [{ field: '', message: 'The booking step was invalid. Please start again.' }]
        });
      }
      return renderBooking(req, res, { step: step - 1, input });
    }

    if (handler === 'Slots') {
      const errors = [];
      if (!schedule.parseDate(input.date)) {
        errors.push({ field: 'date', message: 'Choose a valid date to see available times.' });
      } else if (!schedule.isBookableDate(input.date)) {
        errors.push({ field: 'date', message: 'Choose today or a future date, Monday to Saturday.' });
      }
      return renderBooking(req, res, { step: 3, input, errors });
    }

    if (handler === 'Confirm') {
      step = 5;
      const errors = validateStep(step, input);
      if (errors.length) return renderBooking(req, res, { step, input, errors });
      return confirmBooking(req, res, input);
    }

    const errors = validateStep(step, input);
    if (errors.length) return renderBooking(req, res, { step, input, errors });
    return renderBooking(req, res, { step: Math.min(step + 1, 5), input });
  } catch (error) {
    next(error);
  }
});

async function confirmBooking(req, res, input) {
  const fail = (message) => renderBooking(req, res, { step: 5, input, errors: [{ field: '', message }] });
  try {
    deposits.validateConfiguration();
  } catch (error) {
    if (error instanceof deposits.PaymentConfigurationError) {
      console.error(`Stripe test checkout is not configured: ${error.message}`);
      return fail('Online deposit payment is not set up yet, so bookings cannot be confirmed online. Please contact the clinic to book.');
    }
    throw error;
  }

  let appointment;
  try {
    appointment = await appointments.createBooking({ ...input, doctor: schedule.DOCTORS[input.specialty] });
  } catch (error) {
    console.error('Saving the appointment failed.', error);
    return fail("We couldn't save your booking. Please try again or contact the clinic.");
  }
  if (!appointment) {
    return renderBooking(req, res, {
      step: 3,
      input: { ...input, time: '' },
      errors: [{ field: '', message: 'That time was just booked. Please choose another available slot.' }]
    });
  }

  try {
    return res.redirect(303, await deposits.startCheckout(appointment));
  } catch (error) {
    console.error('Starting Stripe checkout failed.', error?.message);
    if (error?.type?.startsWith?.('Stripe')) {
      return fail('Payment could not be started. Any saved appointment remains Pending, not Booked. Please contact the clinic before trying again.');
    }
    return fail("We couldn't finish setting up payment. Any saved appointment remains Pending. Contact the clinic before trying again.");
  }
}

router.get('/booking-success', async (req, res, next) => {
  res.set({ 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'X-Robots-Tag': 'noindex' });
  const sessionId = req.query.session_id;
  if (!deposits.isValidSessionId(sessionId)) {
    return res.status(400).render('error', { title: 'Bad request', message: 'This payment link is invalid.' });
  }
  try {
    const appointment = await deposits.verifyCheckout(sessionId);
    if (!appointment) {
      return res.status(404).render('error', { title: 'Not found', message: 'We could not find a booking for this payment link.' });
    }
    return res.render('booking-success', {
      title: appointment.status === 'Booked' ? 'Booking confirmed' : 'Payment not completed',
      appointment,
      errorMessage: null,
      displayDate: schedule.displayDate(appointment.date),
      displayTime: schedule.displayTime(appointment.time)
    });
  } catch (error) {
    if (error instanceof deposits.PaymentConfigurationError || error?.type?.startsWith?.('Stripe')) {
      console.error('Stripe payment verification failed.', error.message);
      return res.status(503).render('booking-success', {
        title: 'Payment verification unavailable',
        appointment: null,
        errorMessage: "We couldn't verify your payment right now. Your appointment stays Pending until payment is verified. Please refresh this page in a few minutes or contact the clinic."
      });
    }
    next(error);
  }
});

module.exports = router;
