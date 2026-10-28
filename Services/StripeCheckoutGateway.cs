using System.Globalization;
using Stripe;
using Stripe.Checkout;

namespace LakesideMedical.Services;

public interface IStripeCheckoutGateway
{
    void ValidateConfiguration();
    Task<Session> CreateAsync(int appointmentId, CancellationToken cancellationToken);
    Task<Session> RetrieveAsync(string sessionId, CancellationToken cancellationToken);
}

public class StripeCheckoutGateway : IStripeCheckoutGateway
{
    public void ValidateConfiguration() => GetSettings();

    public Task<Session> CreateAsync(int appointmentId, CancellationToken cancellationToken)
    {
        var (key, baseUrl) = GetSettings();
        var reference = appointmentId.ToString(CultureInfo.InvariantCulture);
        return new SessionService(new StripeClient(key)).CreateAsync(CreateOptions(appointmentId, baseUrl),
            new RequestOptions { IdempotencyKey = $"appointment-deposit-{reference}" }, cancellationToken);
    }

    internal static SessionCreateOptions CreateOptions(int appointmentId, string baseUrl)
    {
        var reference = appointmentId.ToString(CultureInfo.InvariantCulture);
        return new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = baseUrl + "/booking-success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = baseUrl + "/booking",
            ClientReferenceId = reference,
            Metadata = new() { ["appointmentId"] = reference },
            AllowedPaymentMethodTypes = ["card"],
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aed",
                        UnitAmount = 10000,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Refundable consultation deposit",
                            Description = "AED 100 consultation deposit - test payment. Contact the clinic to request a refund."
                        }
                    }
                }
            ]
        };
    }

    public Task<Session> RetrieveAsync(string sessionId, CancellationToken cancellationToken)
    {
        var (key, _) = GetSettings();
        return new SessionService(new StripeClient(key)).GetAsync(sessionId, cancellationToken: cancellationToken);
    }

    private static (string Key, string BaseUrl) GetSettings()
    {
        var key = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new PaymentConfigurationException("Set STRIPE_SECRET_KEY to a secret test key in the server's .env file.");
        }
        var baseUrl = Environment.GetEnvironmentVariable("BASE_URL");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
        {
            throw new PaymentConfigurationException("Set BASE_URL to the site's HTTPS origin (HTTP is allowed for localhost).");
        }
        return (key, uri.GetLeftPart(UriPartial.Authority));
    }
}

public class PaymentConfigurationException(string message) : Exception(message);
