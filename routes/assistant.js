const express = require('express');
const { rateLimit } = require('express-rate-limit');
const assistant = require('../services/assistantService');

const router = express.Router();

const chatLimiter = rateLimit({
  windowMs: 60 * 1000,
  limit: 10,
  standardHeaders: 'draft-7',
  legacyHeaders: false,
  message: { error: 'Too many messages. Please wait a minute and try again.' }
});

router.post('/api/chat', chatLimiter, express.json({ limit: '16kb' }), async (req, res) => {
  res.set('Cache-Control', 'no-store');
  const messages = req.body?.messages;
  if (!assistant.isValidConversation(messages)) {
    return res.status(400).json({ error: 'Please send a valid conversation.' });
  }
  try {
    const reply = await assistant.getReply(messages);
    return res.json({ reply });
  } catch (error) {
    if (error instanceof assistant.AssistantNotConfiguredError) {
      console.error('Assistant chat is not configured: ANTHROPIC_API_KEY is missing.');
      return res.status(503).json({ error: 'The assistant is not available right now. Please call the clinic.' });
    }
    console.error('Assistant chat request failed.', error?.message);
    return res.status(502).json({ error: 'The assistant could not respond. Please try again.' });
  }
});

module.exports = router;
