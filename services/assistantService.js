const Anthropic = require('@anthropic-ai/sdk');
const OpenAI = require('openai');

const MAX_MESSAGES = 12;
const MAX_MESSAGE_LENGTH = 2000;
const MAX_CONVERSATION_LENGTH = 10000;
const DEFAULT_OPENAI_MODEL = 'gpt-4.1-mini';
const DEFAULT_ANTHROPIC_MODEL = 'claude-haiku-5-5';
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
    super('No AI provider key is configured (set OPENAI_API_KEY or ANTHROPIC_API_KEY).');
  }
}

class AssistantUpstreamError extends Error {}

let injected;
const cache = {};

// Tests can inject a fake client: setClient(client, 'openai' | 'anthropic').
function setClient(client, provider = 'openai') {
  injected = client ? { provider, client } : undefined;
}

function cached(name, apiKey, create) {
  if (!cache[name] || cache[name].apiKey !== apiKey) cache[name] = { apiKey, client: create(apiKey) };
  return cache[name].client;
}

// OpenAI is used whenever OPENAI_API_KEY is set; Claude is the fallback.
function getProvider() {
  if (injected) return injected;
  const openaiKey = (process.env.OPENAI_API_KEY || '').trim();
  if (openaiKey) {
    return { provider: 'openai', client: cached('openai', openaiKey, (apiKey) => new OpenAI({ apiKey, maxRetries: 1, timeout: 30000 })) };
  }
  const anthropicKey = (process.env.ANTHROPIC_API_KEY || '').trim();
  if (anthropicKey) {
    return { provider: 'anthropic', client: cached('anthropic', anthropicKey, (apiKey) => new Anthropic({ apiKey, maxRetries: 1, timeout: 30000 })) };
  }
  throw new AssistantNotConfiguredError();
}

function isReasoningModel(model) {
  return /^(o\d|gpt-5)/.test(model) && !/chat-latest/.test(model);
}

async function askOpenAI(client, messages) {
  const model = (process.env.OPENAI_MODEL || '').trim() || DEFAULT_OPENAI_MODEL;
  const reasoning = isReasoningModel(model);
  const response = await client.chat.completions.create({
    model,
    // Reasoning models spend part of the budget on hidden reasoning, so they need more headroom.
    max_completion_tokens: reasoning ? 2000 : 400,
    ...(reasoning ? { reasoning_effort: 'low' } : { temperature: 0.3 }),
    messages: [{ role: 'system', content: SYSTEM_PROMPT }, ...messages.map(({ role, content }) => ({ role, content }))]
  });
  return response?.choices?.[0]?.message?.content;
}

async function askAnthropic(client, messages) {
  const response = await client.messages.create({
    model: (process.env.ANTHROPIC_MODEL || '').trim() || DEFAULT_ANTHROPIC_MODEL,
    max_tokens: 300,
    system: SYSTEM_PROMPT,
    messages: messages.map(({ role, content }) => ({ role, content }))
  });
  return response?.content?.find((block) => block.type === 'text')?.text;
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
  const { provider, client } = getProvider();
  if (!isValidConversation(messages)) throw new TypeError('The conversation is invalid.');

  let text;
  try {
    text = provider === 'openai' ? await askOpenAI(client, messages) : await askAnthropic(client, messages);
  } catch (error) {
    throw new AssistantUpstreamError(`${provider} request failed${error?.status ? ` with HTTP ${error.status}` : ''}: ${error?.message ?? 'unknown error'}`);
  }
  if (typeof text !== 'string' || !text.trim()) {
    throw new AssistantUpstreamError(`${provider} returned an empty or invalid response.`);
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
