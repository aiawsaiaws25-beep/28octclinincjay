using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages;

[Authorize(Roles = ReceptionSeeder.ReceptionRole)]
public class ReceptionModel(AppointmentService appointments, TimeProvider clock) : PageModel
{
    public DateTime ClinicDate { get; private set; }
    public List<IGrouping<string, Appointment>> AppointmentsByDoctor { get; private set; } = [];
    public int AppointmentCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ClinicDate = BookingSchedule.DubaiNow(clock).Date;
        var todaysAppointments = await appointments.GetBookingsByDateAsync(ClinicDate, cancellationToken);
        AppointmentCount = todaysAppointments.Count;
        AppointmentsByDoctor = todaysAppointments
            .OrderBy(appointment => appointment.Doctor)
            .ThenBy(appointment => appointment.AppointmentDateTime)
            .GroupBy(appointment => appointment.Doctor)
            .ToList();
    }

    public async Task<IActionResult> OnPostUpdateStatusAsync(
        int id,
        string? status,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || status is not ("Arrived" or "Done" or "No-show"))
        {
            return BadRequest();
        }

        var today = BookingSchedule.DubaiNow(clock).Date;
        var updated = await appointments.UpdateReceptionStatusAsync(id, today, status, cancellationToken);
        if (!updated)
        {
            TempData["ReceptionMessage"] = "That appointment's status changed or the requested action is no longer available.";
        }
        else
        {
            TempData["ReceptionMessage"] = $"Appointment marked {status}.";
        }

        return RedirectToPage();
    }
}
