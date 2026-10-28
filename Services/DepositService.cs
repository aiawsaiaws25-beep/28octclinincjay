using System.Globalization;
using LakesideMedical.Models;
using Stripe.Checkout;

namespace LakesideMedical.Services;

public class DepositService(AppointmentService appointments, IStripeCheckoutGateway stripe)
{
    public void ValidateConfiguration() => stripe.ValidateConfiguration();

    public async Task<string> StartCheckoutAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var session = await stripe.CreateAsync(appointment.Id, cancellationToken);
        if (session.Livemode || !Uri.TryCreate(session.Url, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "checkout.stripe.com"
            || string.IsNullOrWhiteSpace(session.Id))
        {
            throw new InvalidOperationException("Stripe did not return a valid test checkout session.");
        }
        await appointments.SetStripeSessionAsync(appointment.Id, session.Id, cancellationToken);
        return session.Url;
    }

    public async Task<Appointment?> VerifyCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetBookingByStripeSessionAsync(sessionId, cancellationToken);
        if (appointment is null)
        {
            return null;
        }
        var session = await stripe.RetrieveAsync(sessionId, cancellationToken);
        if (IsPaidDeposit(session, appointment))
        {
            await appointments.MarkDepositPaidAsync(sessionId, cancellationToken);
            return await appointments.GetBookingByStripeSessionAsync(sessionId, cancellationToken);
        }
        return appointment;
    }

    public static bool IsPaidDeposit(Session session, Appointment appointment) =>
        !session.Livemode
        && session.Id == appointment.StripeSessionId
        && !string.IsNullOrWhiteSpace(session.Id)
        && session.PaymentStatus == "paid"
        && session.Mode == "payment"
        && session.Currency == "aed"
        && session.AmountTotal == 10000
        && session.ClientReferenceId == appointment.Id.ToString(CultureInfo.InvariantCulture)
        && session.Metadata is not null
        && session.Metadata.TryGetValue("appointmentId", out var reference)
        && reference == appointment.Id.ToString(CultureInfo.InvariantCulture);
}
