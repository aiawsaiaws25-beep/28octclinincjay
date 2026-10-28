const users = require('../services/userService');

async function loadUser(req, res, next) {
  try {
    req.user = null;
    if (req.session?.userId) {
      req.user = await users.findById(req.session.userId);
      if (!req.user) req.session = null;
    }
    res.locals.currentUser = req.user;
    next();
  } catch (error) {
    next(error);
  }
}

function redirectToLogin(req, res) {
  res.redirect(`/Account/Login?returnUrl=${encodeURIComponent(req.originalUrl)}`);
}

function requireAuth(req, res, next) {
  if (!req.user) return redirectToLogin(req, res);
  next();
}

function requireRole(role) {
  return (req, res, next) => {
    if (!req.user) return redirectToLogin(req, res);
    if (req.user.role !== role) {
      return res.status(403).render('access-denied', { title: 'Access denied' });
    }
    next();
  };
}

module.exports = { loadUser, requireAuth, requireRole };
