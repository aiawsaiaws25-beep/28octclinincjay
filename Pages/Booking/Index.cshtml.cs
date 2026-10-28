using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Booking;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class IndexModel(
    AppointmentService appointments,
    UserManager<IdentityUser> userManager,
    DepositService deposits,
    TimeProvider clock,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public BookingInput Input { get; set; } = new();

    [BindProperty]
    public int Step { get; set; } = 1;

    public DateTime Today => BookingSchedule.DubaiNow(clock).Date;
    public List<string> UnavailableSlots { get; private set; } = [];
    public bool IsSignedIn => User.Identity?.IsAuthenticated == true;

    public async Task OnGetAsync(string? specialty)
    {
        if (specialty is not null && BookingSchedule.Doctors.ContainsKey(specialty))
        {
            Input.Specialty = specialty;
        }
        await SetAccountEmailAsync();
    }

    public async Task<IActionResult> OnPostNextAsync()
    {
        await SetAccountEmailAsync();
        if (!ValidateStep())
        {
            await LoadSlotsAsync();
            return Page();
        }
        Step = Math.Min(Step + 1, 5);
        ModelState.Clear();
        await LoadSlotsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostBackAsync()
    {
        await SetAccountEmailAsync();
        if (Step is < 2 or > 5)
        {
            Step = 1;
            ModelState.AddModelError("", "The booking step was invalid. Please start again.");
            return Page();
        }
        Step--;
        ModelState.Clear();
        await LoadSlotsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSlotsAsync()
    {
        await SetAccountEmailAsync();
        Step = 3;
        ModelState.Clear();
        if (Input.Date is null)
        {
            ModelState.AddModelError("Input.Date", "Choose a valid date to see available times.");
        }
        else if (Input.Date.Value.Date < Today || Input.Date.Value.DayOfWeek == DayOfWeek.Sunday)
        {
            ModelState.AddModelError("Input.Date", "Choose today or a future date, Monday to Saturday.");
        }
        await LoadSlotsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        await SetAccountEmailAsync();
        Step = 5;
        if (!ValidateStep())
        {
            return Page();
        }

        try
        {
            deposits.ValidateConfiguration();
            var appointment = await appointments.CreateBookingAsync(Input, HttpContext.RequestAborted);
            if (appointment is null)
            {
                Step = 3;
                Input.Time = "";
                ModelState.Clear();
                ModelState.AddModelError("", "That time was just booked. Please choose another available slot.");
                await LoadSlotsAsync();
                return Page();
            }

            var checkoutUrl = await deposits.StartCheckoutAsync(appointment, HttpContext.RequestAborted);
            return Redirect(checkoutUrl);
        }
        catch (PaymentConfigurationException exception)
        {
            logger.LogError("Stripe test checkout is not configured.");
            ModelState.AddModelError("", exception.Message);
        }
        catch (Stripe.StripeException)
        {
            logger.LogError("Stripe checkout creation failed.");
            ModelState.AddModelError("", "Payment could not be started. Any saved appointment remains Pending, not Booked. Please contact the clinic before trying again.");
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError("", exception.Message);
        }
        catch (AppointmentStorageException exception)
        {
            logger.LogError(exception, "Could not save an appointment.");
            ModelState.AddModelError("", "We couldn't save your booking. Please try again or contact the clinic.");
        }
        catch (Microsoft.Data.SqlClient.SqlException exception)
        {
            logger.LogError(exception, "Could not persist the appointment checkout link.");
            ModelState.AddModelError("", "We couldn't finish setting up payment. Any saved appointment remains Pending. Contact the clinic before trying again.");
        }
        return Page();
    }

    private async Task SetAccountEmailAsync()
    {
        if (IsSignedIn)
        {
            Input.PatientEmail = await userManager.GetEmailAsync(await userManager.GetUserAsync(User)
                ?? throw new InvalidOperationException("The signed-in patient account could not be found."))
                ?? throw new InvalidOperationException("The signed-in patient has no email address.");
            ModelState.Remove("Input.PatientEmail");
        }
    }

    private bool ValidateStep()
    {
        if (Step is < 1 or > 5)
        {
            Step = 1;
            ModelState.Clear();
            ModelState.AddModelError("", "The booking step was invalid. Please start again.");
            return false;
        }

        var fields = new List<string> { nameof(Input.Specialty) };
        if (Step >= 2) fields.Add(nameof(Input.Doctor));
        if (Step >= 3) fields.AddRange([nameof(Input.Date), nameof(Input.Time)]);
        if (Step >= 4) fields.AddRange([nameof(Input.PatientName), nameof(Input.PatientPhone), nameof(Input.PatientEmail)]);

        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Input.", StringComparison.Ordinal)
            && !fields.Contains(k["Input.".Length..])).ToList())
        {
            ModelState.Remove(key);
        }
        if (!BookingSchedule.Doctors.TryGetValue(Input.Specialty, out var doctor))
        {
            ModelState.AddModelError("Input.Specialty", "Choose one of our four specialties.");
        }
        else if (Step >= 2 && Input.Doctor != doctor)
        {
            ModelState.AddModelError("Input.Doctor", "Choose the doctor for your selected specialty.");
        }
        if (Step >= 3)
        {
            var dateTime = BookingSchedule.GetAppointmentDateTime(Input.Date, Input.Time);
            if (dateTime is null || !BookingSchedule.IsValidAppointmentTime(dateTime.Value, clock))
            {
                ModelState.AddModelError("Input.Date", "Choose a future 30-minute slot, Monday to Saturday, between 9am and 9pm (Dubai time).");
            }
        }
        return ModelState.IsValid;
    }

    private async Task LoadSlotsAsync()
    {
        if (Step != 3 || Input.Date is null)
        {
            return;
        }
        var bookings = await appointments.GetBookingsByDateAsync(Input.Date.Value, HttpContext.RequestAborted);
        UnavailableSlots = bookings
            .Where(a => a.Doctor == Input.Doctor && (a.Status == "Booked" || a.Status == "Pending"))
            .Select(a => a.AppointmentDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList();
    }

    public bool IsSlotUnavailable(string time)
    {
        var dateTime = BookingSchedule.GetAppointmentDateTime(Input.Date, time);
        return dateTime is null || !BookingSchedule.IsValidAppointmentTime(dateTime.Value, clock)
            || UnavailableSlots.Contains(time);
    }
}
