const Anthropic = require('@anthropic-ai/sdk');

const MAX_MESSAGES = 12;
const MAX_MESSAGE_LENGTH = 2000;
const MAX_CONVERSATION_LENGTH = 10000;
const DEFAULT_MODEL = 'claude-haiku-5-5';
const SIGN_OFF = '— Lakeside Medical Clinic, powered by London International · lisrc.ae';

const SYSTEM_PROMPT = `You are Ask Lakeside, the friendly information assistant for Lakeside Medical Clinic.
Answer using only the verified clinic information below. Do not invent or infer clinic facts.

Specialties: GP (General Physician), ENT, Dentist, Cardiologist.
Doctors: Dr. Sara Ahmed (GP), Dr. Omar Khan (ENT), Dr. Lina Haddad (Dentist), Dr. Raj Mehta (Cardiologist).
Hours: Monday-Saturday, 9am-9pm; closed Sunday. Dubai time.
Insurance: The clinic's accepted insurers have not been confirmed. Do not claim that Daman, AXA, or any other insurer is accepted. If asked, say you don't know and suggest calling the clinic.
Location: Cluster X, JLT, Dubai, UAE.
Deposit: AED 100 refundable consultation deposit.

If requested information is not above, say you don't know and suggest calling the clinic.
Do not give medical advice or discuss symptoms, diagnoses, or treatments. If asked for medical advice, say you can't give medical advice and offer to help book the appropriate specialist. If the situation sounds serious or urgent, suggest seeking urgent/emergency care.
Keep replies short and friendly. End every reply with this exact one-line sign-off:
${SIGN_OFF}`;

class AssistantNotConfiguredError extends Error {
  constructor() {
    super('ANTHROPIC_API_KEY is not configured.');
  }
}

class AssistantUpstreamError extends Error {}

let injectedClient;
let cachedClient;
let cachedKey;

function setClient(client) {
  injectedClient = client;
}

function getClient() {
  if (injectedClient) return injectedClient;
  const apiKey = (process.env.ANTHROPIC_API_KEY || '').trim();
  if (!apiKey) throw new AssistantNotConfiguredError();
  if (!cachedClient || cachedKey !== apiKey) {
    cachedClient = new Anthropic({ apiKey, maxRetries: 1, timeout: 30000 });
    cachedKey = apiKey;
  }
  return cachedClient;
}

function isValidConversation(messages) {
  if (!Array.isArray(messages) || messages.length < 1 || messages.length > MAX_MESSAGES) return false;
  let totalLength = 0;
  for (let index = 0; index < messages.length; index++) {
    const message = messages[index];
    const expectedRole = index % 2 === 0 ? 'user' : 'assistant';
    if (!message || typeof message !== 'object'
      || message.role !== expectedRole
      || typeof message.content !== 'string'
      || !message.content.trim()
      || message.content.length > MAX_MESSAGE_LENGTH) {
      return false;
    }
    totalLength += message.content.length;
    if (totalLength > MAX_CONVERSATION_LENGTH) return false;
  }
  return messages[messages.length - 1].role === 'user';
}

function withSignOff(reply) {
  const trimmed = reply.trim();
  return trimmed.endsWith(SIGN_OFF) ? trimmed : `${trimmed}\n${SIGN_OFF}`;
}

async function getReply(messages) {
  const client = getClient();
  if (!isValidConversation(messages)) throw new TypeError('The conversation is invalid.');

  let response;
  try {
    response = await client.messages.create({
      model: (process.env.ANTHROPIC_MODEL || '').trim() || DEFAULT_MODEL,
      max_tokens: 300,
      system: SYSTEM_PROMPT,
      messages: messages.map(({ role, content }) => ({ role, content }))
    });
  } catch (error) {
    throw new AssistantUpstreamError(`Anthropic request failed${error?.status ? ` with HTTP ${error.status}` : ''}.`);
  }

  const text = response?.content?.find((block) => block.type === 'text')?.text;
  if (typeof text !== 'string' || !text.trim()) {
    throw new AssistantUpstreamError('Anthropic returned an empty or invalid response.');
  }
  return withSignOff(text);
}

module.exports = {
  SIGN_OFF,
  SYSTEM_PROMPT,
  AssistantNotConfiguredError,
  AssistantUpstreamError,
  isValidConversation,
  getReply,
  setClient
};
