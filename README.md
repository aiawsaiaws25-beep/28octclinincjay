# Lakeside Medical Clinic

A responsive clinic website built with **Node.js, Express and EJS**, ready to deploy on **Vercel**. It has a booking wizard with a Stripe test deposit, patient accounts, a password-protected reception dashboard and the floating **Ask Lakeside** AI chat assistant.

## Project structure

```
api/index.js                  Vercel serverless entry (exports the Express app)
app.js                        Express app: sessions, CSRF, routes, error pages
server.js                     Local development server (http://localhost:5221)
data/db.js                    libSQL/SQLite schema, users.role migration, reception seed
services/assistantService.js  All AI code (OpenAI, or Claude as fallback, + system prompt)
services/appointmentService.js  Appointment queries and status updates
services/depositService.js    Stripe Checkout deposit (AED 100) and payment verification
services/bookingSchedule.js   Doctors, 30-minute slots, Dubai time rules
services/userService.js       Patient accounts, bcrypt passwords, login lockout
middleware/                   auth (requireAuth / requireRole) and CSRF protection
routes/                       account, booking, patient + reception, /api/chat
views/                        EJS pages; partials/header.ejs and partials/footer.ejs (chat widget)
public/                       CSS, JS, images, Bootstrap (served as static files by Vercel)
tests/app.test.js             node:test + supertest integration tests
```

## Run locally

Requires Node.js 20 or later.

```powershell
npm install
Copy-Item .env.example .env   # then fill in the values
npm start                     # http://localhost:5221
npm test
```

Without `TURSO_DATABASE_URL`, data is stored in `data/lakeside.db` (ignored by git).

## Environment variables

| Name | Purpose |
| --- | --- |
| `SESSION_SECRET` | Signs the session cookie. At least 32 random characters. |
| `RECEPTION_PASSWORD` | Seeds/updates `reception@lakeside.local` with role `reception` (bcrypt-hashed). Override the email with `RECEPTION_EMAIL`. |
| `OPENAI_API_KEY` | Server-side key for the Ask Lakeside assistant (used first). Never exposed to the browser. |
| `OPENAI_MODEL` | Optional OpenAI model override (default `gpt-4.1-mini`). |
| `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL` | Optional Claude fallback, used only when `OPENAI_API_KEY` is empty (default model `claude-haiku-5-5`). |
| `STRIPE_SECRET_KEY` | Stripe **test** secret key (`sk_test_...`). Live keys are rejected. |
| `BASE_URL` | Public site URL used for Stripe redirects, e.g. `https://your-app.vercel.app`. |
| `TURSO_DATABASE_URL`, `TURSO_AUTH_TOKEN` | Persistent libSQL database for Vercel. |

## Deploy to Vercel

1. Import the GitHub repository in Vercel (Framework preset: **Other**), or run `vercel --prod`.
2. Add the environment variables above in **Project Settings -> Environment Variables**.
3. Create a free Turso database (Vercel Marketplace -> Turso, or `turso db create`) and set `TURSO_DATABASE_URL` and `TURSO_AUTH_TOKEN`.

> Vercel functions have no persistent disk. Without Turso the app falls back to a temporary `/tmp` SQLite file that is reset whenever the function restarts, so accounts and bookings will disappear. Always configure Turso for real use.

`vercel.json` serves `public/` as static files and rewrites every other path to the Express function in `api/index.js`.

## Features

- **Booking wizard** - specialty, doctor, date/time (Mon-Sat, 9am-9pm Dubai time, 30-minute slots), patient details and review. Double-booking is blocked by a unique index on active (Pending/Booked) slots.
- **Stripe deposit** - confirming saves a Pending appointment and opens Stripe Checkout for an AED 100 refundable deposit. `/booking-success` verifies the session server-side before marking it Booked.
- **Patient accounts** - register, login (lockout after 5 failed attempts), logout, My Appointments with cancellation of Booked appointments.
- **Reception dashboard** (`/reception`, role `reception` only) - today's appointments in Asia/Dubai time grouped by doctor, a count of today's appointments, Arrived / Done / No-show buttons and a Send reminder email link.
- **Ask Lakeside** - floating chat assistant on every page (`views/partials/footer.ejs`) calling `POST /api/chat`. Answers only from verified clinic facts, refuses medical advice, and ends every reply with the London International sign-off. Rate-limited to 10 messages per minute per IP.

## Security notes

- Secrets are read from environment variables only; `.env` is git-ignored.
- Sessions are signed, HTTP-only, SameSite=Lax cookies (Secure in production). All form posts require a CSRF token.
- This is a prototype: email ownership is not verified and Stripe runs in test mode. Use sample data only.
