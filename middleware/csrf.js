const crypto = require('node:crypto');

function tokensMatch(expected, actual) {
  if (typeof expected !== 'string' || typeof actual !== 'string') return false;
  const a = Buffer.from(expected);
  const b = Buffer.from(actual);
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}

function csrfProtection({ exemptPaths = [] } = {}) {
  return (req, res, next) => {
    if (!req.session.csrf) req.session.csrf = crypto.randomBytes(32).toString('base64url');
    res.locals.csrfToken = req.session.csrf;

    const safeMethod = ['GET', 'HEAD', 'OPTIONS'].includes(req.method);
    if (safeMethod || exemptPaths.includes(req.path)) return next();
    if (!tokensMatch(req.session.csrf, req.body?._csrf)) {
      return res.status(400).render('error', {
        title: 'Bad request',
        message: 'Your form expired or was not sent from this site. Please go back, refresh the page and try again.'
      });
    }
    next();
  };
}

module.exports = { csrfProtection };
