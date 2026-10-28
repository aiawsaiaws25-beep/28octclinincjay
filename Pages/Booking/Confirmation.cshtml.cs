using System.Globalization;
using System.Security.Cryptography;
using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Booking;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ConfirmationModel(
    AppointmentService appointments,
    IDataProtectionProvider protectionProvider,
    ILogger<ConfirmationModel> logger) : PageModel
{
    public Appointment Appointment { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return NotFound();
        }
        string value;
        try
        {
            value = protectionProvider.CreateProtector("BookingConfirmation")
                .ToTimeLimitedDataProtector().Unprotect(token);
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "An invalid or expired booking confirmation link was used.");
            return BadRequest("This confirmation link is invalid or has expired. Log in to view your appointments.");
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return BadRequest("Invalid confirmation link.");
        }
        var appointment = await appointments.GetBookingAsync(id, HttpContext.RequestAborted);
        if (appointment is null)
        {
            return NotFound();
        }
        if (appointment.StripeSessionId is not null)
        {
            return RedirectToPage("/Booking/Success", new { session_id = appointment.StripeSessionId });
        }
        if (appointment.Status != "Booked")
        {
            return BadRequest("This appointment is not confirmed. Please contact the clinic.");
        }
        Appointment = appointment;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
