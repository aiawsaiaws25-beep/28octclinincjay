using System.Collections.Concurrent;
using System.Globalization;
using LakesideMedical.Models;
using LakesideMedical.Services;
using Stripe.Checkout;

namespace LakesideMedical.Tests;

public class FakeStripeCheckoutGateway : IStripeCheckoutGateway
{
    private readonly ConcurrentDictionary<string, Session> sessions = new();

    public void ValidateConfiguration() { }

    public Task<Session> CreateAsync(int appointmentId, CancellationToken cancellationToken)
    {
        var reference = appointmentId.ToString(CultureInfo.InvariantCulture);
        var session = new Session
        {
            Id = $"cs_test_appointment{reference}",
            Url = $"https://checkout.stripe.com/c/pay/cs_test_appointment{reference}",
            PaymentStatus = "unpaid",
            Mode = "payment",
            Currency = "aed",
            AmountTotal = 10000,
            ClientReferenceId = reference,
            Metadata = new() { ["appointmentId"] = reference }
        };
        sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<Session> RetrieveAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(sessions[sessionId]);

    public void Pay(string sessionId) => sessions[sessionId].PaymentStatus = "paid";
}

public class DepositTests
{
    [Fact]
    public void CheckoutUsesOneOffAed100AndExactReturnRoutes()
    {
        var options = StripeCheckoutGateway.CreateOptions(42, "https://clinic.example");
        Assert.Equal("payment", options.Mode);
        Assert.Equal("https://clinic.example/booking-success?session_id={CHECKOUT_SESSION_ID}", options.SuccessUrl);
        Assert.Equal("https://clinic.example/booking", options.CancelUrl);
        Assert.Equal("42", options.ClientReferenceId);
        Assert.Equal("42", options.Metadata["appointmentId"]);
        Assert.Equal("card", Assert.Single(options.AllowedPaymentMethodTypes));
        var item = Assert.Single(options.LineItems);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("aed", item.PriceData.Currency);
        Assert.Equal(10000, item.PriceData.UnitAmount);
        Assert.Equal("Refundable consultation deposit", item.PriceData.ProductData.Name);
    }

    private static Appointment Appointment() => new() { Id = 42, Status = "Pending", StripeSessionId = "cs_test_unit" };

    private static Session PaidSession() => new()
    {
        Id = "cs_test_unit",
        PaymentStatus = "paid",
        Mode = "payment",
        Currency = "aed",
        AmountTotal = 10000,
        ClientReferenceId = "42",
        Metadata = new() { ["appointmentId"] = "42" }
    };

    [Fact]
    public void OnlyVerifiedExactDepositMatches() =>
        Assert.True(DepositService.IsPaidDeposit(PaidSession(), Appointment()));

    [Theory]
    [InlineData("unpaid")]
    [InlineData("no_payment_required")]
    [InlineData("")]
    public void RedirectWithoutPaidStatusCannotConfirm(string paymentStatus)
    {
        var session = PaidSession();
        session.PaymentStatus = paymentStatus;
        Assert.False(DepositService.IsPaidDeposit(session, Appointment()));
    }

    [Theory]
    [InlineData("live")]
    [InlineData("session")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("mode")]
    [InlineData("reference")]
    [InlineData("metadata")]
    [InlineData("missing-metadata")]
    public void RejectsMismatchedOrLivePayment(string mismatch)
    {
        var session = PaidSession();
        switch (mismatch)
        {
            case "live": session.Livemode = true; break;
            case "session": session.Id = "cs_test_other"; break;
            case "currency": session.Currency = "usd"; break;
            case "amount": session.AmountTotal = 100; break;
            case "mode": session.Mode = "subscription"; break;
            case "reference": session.ClientReferenceId = "43"; break;
            case "metadata": session.Metadata["appointmentId"] = "43"; break;
            case "missing-metadata": session.Metadata = null; break;
        }
        Assert.False(DepositService.IsPaidDeposit(session, Appointment()));
    }
}
