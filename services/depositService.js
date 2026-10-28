const Stripe = require('stripe');
const appointments = require('./appointmentService');

const DEPOSIT_AMOUNT = 10000;
const DEPOSIT_CURRENCY = 'aed';
const SESSION_ID_PATTERN = /^cs_test_[A-Za-z0-9_]{1,247}$/;

class PaymentConfigurationError extends Error {}

let stripeClient;
let stripeClientKey;

function setStripeClient(client) {
  stripeClient = client;
  stripeClientKey = client ? '__injected__' : undefined;
}

function getBaseUrl() {
  const raw = (process.env.BASE_URL || '').trim();
  if (!raw) throw new PaymentConfigurationError('BASE_URL is not configured.');
  let url;
  try {
    url = new URL(raw);
  } catch {
    throw new PaymentConfigurationError('BASE_URL must be an absolute URL.');
  }
  const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
  if (url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback)) {
    throw new PaymentConfigurationError('BASE_URL must use https (http is allowed only for localhost).');
  }
  return url.origin;
}

function getStripe() {
  if (stripeClientKey === '__injected__') return stripeClient;
  const key = (process.env.STRIPE_SECRET_KEY || '').trim();
  if (!key.startsWith('sk_test_')) {
    throw new PaymentConfigurationError('STRIPE_SECRET_KEY must be a Stripe test-mode secret key (sk_test_...).');
  }
  if (!stripeClient || stripeClientKey !== key) {
    stripeClient = new Stripe(key, { maxNetworkRetries: 1, timeout: 15000 });
    stripeClientKey = key;
  }
  return stripeClient;
}

function validateConfiguration() {
  getStripe();
  getBaseUrl();
}

async function startCheckout(appointment) {
  const baseUrl = getBaseUrl();
  const session = await getStripe().checkout.sessions.create({
    mode: 'payment',
    payment_method_types: ['card'],
    line_items: [{
      quantity: 1,
      price_data: {
        currency: DEPOSIT_CURRENCY,
        unit_amount: DEPOSIT_AMOUNT,
        product_data: {
          name: 'Refundable consultation deposit',
          description: 'AED 100 consultation deposit - test payment. Contact the clinic to request a refund.'
        }
      }
    }],
    client_reference_id: String(appointment.id),
    metadata: { appointmentId: String(appointment.id) },
    success_url: `${baseUrl}/booking-success?session_id={CHECKOUT_SESSION_ID}`,
    cancel_url: `${baseUrl}/booking`
  }, { idempotencyKey: `appointment-deposit-${appointment.id}` });

  if (!session?.id || !session.url) throw new Error('Stripe did not return a checkout session URL.');
  if (!(await appointments.setStripeSession(appointment.id, session.id))) {
    throw new Error('The checkout session could not be linked to the appointment.');
  }
  return session.url;
}

function isValidSessionId(sessionId) {
  return typeof sessionId === 'string' && sessionId.length <= 255 && SESSION_ID_PATTERN.test(sessionId);
}

function isPaidDeposit(session, appointment) {
  const id = String(appointment.id);
  return session.livemode === false
    && session.id === appointment.stripeSessionId
    && session.payment_status === 'paid'
    && session.mode === 'payment'
    && session.currency === DEPOSIT_CURRENCY
    && session.amount_total === DEPOSIT_AMOUNT
    && session.client_reference_id === id
    && session.metadata?.appointmentId === id;
}

async function verifyCheckout(sessionId) {
  const appointment = await appointments.getBookingByStripeSession(sessionId);
  if (!appointment) return null;
  if (appointment.status !== 'Pending') return appointment;
  const session = await getStripe().checkout.sessions.retrieve(sessionId);
  if (isPaidDeposit(session, appointment)) {
    await appointments.markDepositPaid(appointment.id, sessionId);
  }
  return appointments.getBooking(appointment.id);
}

module.exports = {
  PaymentConfigurationError,
  setStripeClient,
  validateConfiguration,
  startCheckout,
  isValidSessionId,
  isPaidDeposit,
  verifyCheckout
};
