using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Booking;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class SuccessModel(DepositService deposits, ILogger<SuccessModel> logger) : PageModel
{
    public Appointment? Appointment { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? session_id)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (string.IsNullOrWhiteSpace(session_id) || session_id.Length > 255
            || !session_id.StartsWith("cs_test_", StringComparison.Ordinal))
        {
            return BadRequest("A valid test checkout session is required.");
        }
        try
        {
            Appointment = await deposits.VerifyCheckoutAsync(session_id, HttpContext.RequestAborted);
            if (Appointment is null)
            {
                return NotFound("No appointment was found for this checkout session.");
            }
        }
        catch (Stripe.StripeException)
        {
            logger.LogError("Stripe checkout verification failed.");
            ErrorMessage = "We couldn't verify your payment with Stripe. Your booking has not been confirmed by this visit. Refresh to check again or contact the clinic before paying again.";
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        catch (PaymentConfigurationException)
        {
            logger.LogError("Stripe test checkout verification is not configured.");
            ErrorMessage = "Payment verification is unavailable. Please contact the clinic before paying again.";
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        return Page();
    }
}
