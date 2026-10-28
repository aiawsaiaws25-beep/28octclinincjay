# Lakeside Medical Clinic

A responsive ASP.NET Core Razor Pages clinic website with appointment booking and patient accounts.

## Prerequisites

- .NET 10 SDK.
- SQL Server Express LocalDB on Windows, installed for the user running the app. In Visual Studio Installer, select the SQL Server Express LocalDB individual component, or install LocalDB from Microsoft's SQL Server Express download.
- The default connection in `appsettings.json` uses `(localdb)\MSSQLLocalDB` and creates a database named `LakesideMedical`. No database password is stored in source.

## Run locally

```powershell
dotnet restore
dotnet tool restore
dotnet ef database update
dotnet run
```

Run these commands from the project directory. Open the localhost URL printed by the application. The migrations are `InitialCreate` (appointments), `AddIdentity` (patient accounts), and `AddStripeDeposit` (nullable Stripe session ID and Pending slot reservations). They are applied explicitly, not automatically on application startup. The Stripe migration adds `stripeSessionId` only if missing and keeps existing appointments unchanged.

Bootstrap and form-validation scripts are bundled locally. Bootstrap Icons are loaded from the jsDelivr CDN and require an internet connection.

## Booking and accounts

- `/Booking` has four input steps (specialty, doctor, date/time, patient details), then a review screen. **Confirm booking & pay AED 100** saves a `Pending` appointment and redirects to Stripe-hosted test checkout. It is not marked `Booked` until the server verifies payment.
- The example doctors are Dr. Sara Ahmed (GP), Dr. Omar Khan (ENT), Dr. Lina Haddad (Dentist), and Dr. Raj Mehta (Cardiologist). Edit the single catalogue in `Models/BookingSchedule.cs` to change them.
- Slots are 30 minutes from 9:00am through 8:30pm, Monday-Saturday. The final appointment finishes at 9pm. Dates/times are interpreted in Dubai time (UTC+4), while `CreatedAt` is UTC.
- Server validation rejects past times, Sundays, off-grid slots, incorrect doctor/specialty combinations and invalid patient details. A filtered unique database index reserves slots for both `Pending` and `Booked` appointments. Cancelling a confirmed appointment releases that slot.
- Guests may book. Signed-in patients use their account email, which is enforced server-side, not merely pre-filled in the browser.
- Register, Login and POST-only Logout use ASP.NET Core Identity in the same database. Passwords require at least eight characters with uppercase, lowercase, a digit and a symbol. Failed login attempts use Identity's lockout protection.
- `/reception` is restricted to the `reception` Identity role. At startup, the app ensures `patient` and `reception` roles exist, assigns the patient role to existing users with no role, and seeds `reception@lakeside.local` from `RECEPTION_PASSWORD` if that account does not already exist. Configure the password in the root `.env` before starting the app. ASP.NET Identity stores its standard salted password hash in `AspNetUsers`; role membership is stored in `AspNetUserRoles`, rather than a custom users-table role column. The dashboard shows appointments for the current Dubai date and permits status transitions from Booked to Arrived, Arrived to Done, or Booked/Arrived to No-show. Send reminder opens a prefilled message in the staff mail app; it does not send automatically.
- `/MyAppointments` requires login and lists only bookings matching the account email, latest appointment date first. Cancellation checks the signed-in email in the database update itself, regardless of the submitted appointment ID.
- Stripe confirmation pages use an unguessable session ID, not a sequential appointment ID. Keep the link private. The previous protected 30-minute confirmation links remain supported for pre-payment bookings. No confirmation emails are sent yet.
- All appointment queries and writes are in `Services/AppointmentService.cs`; pages do not access `AppDbContext`. Identity manages its own account persistence through `UserManager` and `SignInManager`.

### Development-only account verification

Email confirmation is disabled as requested. Email matching therefore does **not** prove ownership of an email address: someone could register using another patient's address. Do not publish this prototype with real patient information until email verification, production privacy/retention policies and appropriate access controls are in place. Configure HTTPS and production SQL Server before deployment; LocalDB is for local development.

## Stripe test deposits

The application stays in ASP.NET Core and uses the official **Stripe.net** SDK rather than a Node/Express sidecar.

1. Copy `.env.example` to `.env` in the project root.
2. Set `STRIPE_SECRET_KEY` inside `.env` to your own **secret test key**, locally. Never paste the key into chat, commit it, put it in `appsettings.json`, or put it in browser JavaScript.
3. Set `BASE_URL` to the origin you run the site on, for example `http://localhost:5221` for the HTTP launch profile. Match the actual scheme and port. HTTPS is required except for loopback development.
4. Apply migrations with `dotnet ef database update`, then restart the app.

`dotenv.net` loads the root `.env` server-side without overwriting existing environment variables. `.env` and variants are ignored by Git and excluded from publish output. The checked-in `.env.example` has an empty secret value. For deployments, set server environment variables rather than publishing a secrets file. Missing keys and live keys are rejected.

## Ask Lakeside chat assistant

The floating Ask Lakeside assistant is included on every Razor page. Its `POST /api/chat` endpoint calls Anthropic from the server; the API key is never sent to the browser. Add `ANTHROPIC_API_KEY` to the root `.env` (copy `.env.example` first), then restart the app. `ANTHROPIC_MODEL` is optional and defaults to `claude-haiku-5-5`. The endpoint accepts at most 12 alternating user/assistant messages, limits each IP to 10 requests per minute, and returns a service-unavailable response when no API key is configured.

The assistant is limited by its system prompt to the clinic facts listed in the app. Insurance acceptance is not confirmed, so it does not claim coverage. It does not provide medical advice and directs serious or urgent concerns to urgent/emergency care. Confirm the provider model is available for your Anthropic account before using the assistant.

On confirmation:

- The appointment is saved as `Pending` before Stripe is called.
- The server creates a one-off Checkout Session for **AED 100** (`aed`, `10000` minor units, quantity one) with a per-appointment idempotency key. Only the appointment reference is sent as metadata; patient contact details are not sent by the application.
- The session ID is saved to `Appointments.stripeSessionId` before redirecting the browser to `checkout.stripe.com`.
- The success URL is `BASE_URL/booking-success?session_id={CHECKOUT_SESSION_ID}`. The cancel URL is `BASE_URL/booking`.
- The success route looks up the appointment by its saved session ID, then retrieves the session directly from Stripe. Only a paid, test-mode, one-off AED 100 session matching the appointment can promote `Pending` to `Booked`. Refreshes are safe, and cancelled bookings are never reactivated.
- Unpaid checkout shows **Payment not completed** and stays `Pending`. API failures show a verification-unavailable message rather than a false confirmation.

Use Stripe test card `4242 4242 4242 4242`, a future expiry and any valid test CVC to exercise successful checkout. Do not use real card or patient details.

### Deliberate limitations

- The deposit is described as refundable, but **refunds are manual through the clinic/Stripe Dashboard**; cancellation does not initiate a refund.
- No webhook or background reconciliation is implemented. A patient who pays but never returns to the success page stays `Pending` until that page is visited. Signed-in patients can use **Check payment status** from My Appointments.
- Unpaid/abandoned bookings continue reserving their slot as `Pending`. They are not automatically expired or released. Contact the clinic before creating another booking after a checkout error. Production use needs webhook reconciliation and an expiry/release policy before deployment.
- Integration tests use an in-process fake Stripe gateway; they never charge a card or need a secret key. A real Stripe-hosted checkout requires your locally configured key and a working database.

## Tests

```powershell
dotnet test .\tests\LakesideMedical.Tests\LakesideMedical.Tests.csproj
```

The schedule and deposit-verification tests do not need a database. The patient-flow integration tests require LocalDB and create a uniquely named `LakesideMedicalTests_...` database, apply all migrations, then delete only that test database. They cover registration/login/logout, server validation, Pending-save-before-checkout, unpaid/paid payment verification, email isolation, antiforgery enforcement, ownership-checked cancellation and slot conflicts.

To run only the database-free tests:

```powershell
dotnet test .\tests\LakesideMedical.Tests\LakesideMedical.Tests.csproj --filter "FullyQualifiedName~BookingScheduleTests|FullyQualifiedName~DepositTests"
```

If EF reports that it cannot locate a Local Database Runtime installation, finish installing LocalDB, restart the terminal if needed, and run `dotnet ef database update` again.

## Before publishing

- Replace the example clinic name, area, address and copy in `Pages/Index.cshtml` and `Pages/Shared/_Layout.cshtml`.
- Add the real clinic phone number in the Contact section, ideally as a `tel:` link.
- Replace `[Your name]` in the shared footer and remove the sample-details notice after verifying all details.
- Confirm the example doctor names and clinic schedule in `Models/BookingSchedule.cs`.

Custom styles live in `wwwroot/css/site.css`. The clinic illustration is a local SVG in `wwwroot/images/clinic.svg`. The footer and navigation are shared across all Razor pages.
