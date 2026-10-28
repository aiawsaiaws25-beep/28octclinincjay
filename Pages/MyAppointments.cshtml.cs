using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class MyAppointmentsModel(AppointmentService appointments, UserManager<IdentityUser> userManager) : PageModel
{
    public List<Appointment> Appointments { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync() =>
        Appointments = await appointments.GetBookingsByEmailAsync(await GetPatientEmailAsync(), HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var updated = await appointments.UpdateStatusAsync(id, await GetPatientEmailAsync(), "Cancelled", HttpContext.RequestAborted);
        if (!updated)
        {
            return NotFound("This appointment was not found in your booked appointments.");
        }
        StatusMessage = "Your appointment has been cancelled.";
        return RedirectToPage();
    }

    private async Task<string> GetPatientEmailAsync()
    {
        var user = await userManager.GetUserAsync(User)
            ?? throw new InvalidOperationException("The signed-in patient account could not be found.");
        return await userManager.GetEmailAsync(user)
            ?? throw new InvalidOperationException("The signed-in patient has no email address.");
    }
}
