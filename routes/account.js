const express = require('express');
const users = require('../services/userService');

const router = express.Router();
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function safeReturnUrl(value) {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return '/';
  return value.slice(0, 500);
}

function startSession(req, user, rememberMe) {
  req.session = { userId: user.id };
  req.sessionOptions.maxAge = rememberMe ? 14 * 24 * 60 * 60 * 1000 : undefined;
}

function passwordErrors(password) {
  const errors = [];
  if (password.length < 8 || password.length > 100) errors.push('Password must be 8-100 characters long.');
  if (!/[a-z]/.test(password)) errors.push('Password must include a lowercase letter.');
  if (!/[A-Z]/.test(password)) errors.push('Password must include an uppercase letter.');
  if (!/[0-9]/.test(password)) errors.push('Password must include a number.');
  if (!/[^A-Za-z0-9]/.test(password)) errors.push('Password must include a symbol.');
  return errors;
}

router.get('/Account/Login', (req, res) => {
  res.render('login', { title: 'Patient login', errors: [], email: '', returnUrl: safeReturnUrl(req.query.returnUrl) });
});

router.post('/Account/Login', async (req, res, next) => {
  try {
    const email = typeof req.body.email === 'string' ? req.body.email.trim().toLowerCase().slice(0, 254) : '';
    const password = typeof req.body.password === 'string' ? req.body.password.slice(0, 200) : '';
    const returnUrl = safeReturnUrl(req.body.returnUrl);
    const render = (errors) => res.status(400).render('login', { title: 'Patient login', errors, email, returnUrl });

    if (!EMAIL_PATTERN.test(email) || !password) return render(['Enter your email and password.']);
    const result = await users.verifyLogin(email, password);
    if (result.lockedOut) return render(['This account is temporarily locked after too many failed attempts. Please try again in a few minutes.']);
    if (!result.ok) return render(['Invalid login attempt.']);

    startSession(req, result.user, req.body.rememberMe === 'on');
    res.redirect(returnUrl === '/' && result.user.role === 'reception' ? '/reception' : returnUrl);
  } catch (error) {
    next(error);
  }
});

router.get('/Account/Register', (req, res) => {
  res.render('register', { title: 'Create a patient account', errors: [], email: '', returnUrl: safeReturnUrl(req.query.returnUrl) });
});

router.post('/Account/Register', async (req, res, next) => {
  try {
    const email = typeof req.body.email === 'string' ? req.body.email.trim().toLowerCase() : '';
    const password = typeof req.body.password === 'string' ? req.body.password : '';
    const confirmPassword = typeof req.body.confirmPassword === 'string' ? req.body.confirmPassword : '';
    const returnUrl = safeReturnUrl(req.body.returnUrl);
    const errors = [];
    if (email.length > 254 || !EMAIL_PATTERN.test(email)) errors.push('Enter a valid email address.');
    errors.push(...passwordErrors(password));
    if (password !== confirmPassword) errors.push('The password and confirmation password do not match.');
    const render = (list) => res.status(400).render('register', {
      title: 'Create a patient account', errors: list, email: email.slice(0, 254), returnUrl
    });
    if (errors.length) return render(errors);

    const user = await users.createPatient(email, password);
    if (!user) return render(['An account with this email already exists. Please log in instead.']);
    startSession(req, user, false);
    res.redirect(returnUrl);
  } catch (error) {
    next(error);
  }
});

router.post('/Account/Logout', (req, res) => {
  req.session = null;
  res.redirect('/');
});

module.exports = router;
