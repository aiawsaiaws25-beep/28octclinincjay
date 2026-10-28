require('dotenv').config({ quiet: true });

const crypto = require('node:crypto');
const path = require('node:path');
const express = require('express');
const cookieSession = require('cookie-session');
const { loadUser } = require('./middleware/auth');
const { csrfProtection } = require('./middleware/csrf');

function sessionKeys() {
  const secret = (process.env.SESSION_SECRET || '').trim();
  if (secret.length >= 32) return [secret];
  console.warn('SESSION_SECRET is missing or shorter than 32 characters: using a temporary key, so sign-ins reset when the server restarts.');
  return [crypto.randomBytes(32).toString('hex')];
}

const app = express();
const isProduction = process.env.NODE_ENV === 'production' || Boolean(process.env.VERCEL);

app.set('view engine', 'ejs');
app.set('views', path.join(__dirname, 'views'));
app.set('trust proxy', 1);
app.disable('x-powered-by');

app.use((req, res, next) => {
  res.set({
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Referrer-Policy': 'strict-origin-when-cross-origin'
  });
  if (isProduction) res.set('Strict-Transport-Security', 'max-age=31536000');
  next();
});

app.use(express.static(path.join(__dirname, 'public'), { maxAge: isProduction ? '1d' : 0 }));

app.use(cookieSession({
  name: 'lakeside.sid',
  keys: sessionKeys(),
  httpOnly: true,
  sameSite: 'lax',
  secure: isProduction
}));
app.use((req, res, next) => {
  res.locals.title = 'Lakeside Medical Clinic';
  res.locals.year = new Date().getUTCFullYear();
  res.locals.currentUser = null;
  res.locals.csrfToken = '';
  next();
});
app.use(express.urlencoded({ extended: false, limit: '32kb' }));
app.use(loadUser);
app.use(csrfProtection({ exemptPaths: ['/api/chat'] }));

app.use(require('./routes/assistant'));
app.use(require('./routes/account'));
app.use(require('./routes/booking'));
app.use(require('./routes/patient'));

app.get('/', (req, res) => res.render('index', { title: 'Expert care, close to home' }));
app.get('/Privacy', (req, res) => res.render('privacy', { title: 'Privacy Policy' }));
app.get('/Account/AccessDenied', (req, res) => res.status(403).render('access-denied', { title: 'Access denied' }));

app.use((req, res) => {
  res.status(404).render('error', { title: 'Page not found', message: "We couldn't find that page." });
});

// eslint-disable-next-line no-unused-vars
app.use((error, req, res, next) => {
  const status = Number.isInteger(error?.status) && error.status >= 400 && error.status < 500 ? error.status : 500;
  if (status === 500) console.error(error);
  if (req.path.startsWith('/api/')) {
    return res.status(status).json({ error: status === 500 ? 'Something went wrong.' : 'Invalid request.' });
  }
  res.status(status).render('error', {
    title: 'Error',
    message: status === 500 ? 'An error occurred while processing your request. Please try again.' : 'The request was invalid.'
  });
});

module.exports = app;
