using System.ComponentModel.DataAnnotations;
using LakesideMedical.Data;
using LakesideMedical.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LakesideMedical.Services;

public class AppointmentService(AppDbContext context, TimeProvider clock)
{
    public async Task<Appointment?> CreateBookingAsync(BookingInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var dateTime = BookingSchedule.GetAppointmentDateTime(input.Date, input.Time);
        if (!BookingSchedule.Doctors.TryGetValue(input.Specialty, out var doctor)
            || doctor != input.Doctor
            || dateTime is null
            || !BookingSchedule.IsValidAppointmentTime(dateTime.Value, clock)
            || string.IsNullOrWhiteSpace(input.PatientName))
        {
            throw new ValidationException("Choose a valid specialty, doctor and future clinic time, and enter your name.");
        }

        var appointment = new Appointment
        {
            Specialty = input.Specialty,
            Doctor = doctor,
            AppointmentDateTime = dateTime.Value,
            PatientName = input.PatientName.Trim(),
            PatientPhone = input.PatientPhone.Trim(),
            PatientEmail = NormalizeEmail(input.PatientEmail),
            Status = "Pending",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(appointment);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return appointment;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            context.Entry(appointment).State = EntityState.Detached;
            return null; // Another patient may have booked the slot since it was displayed.
        }
        catch (DbUpdateException exception)
        {
            throw new AppointmentStorageException("The booking could not be saved to the database.", exception);
        }
    }

    public Task<List<Appointment>> GetBookingsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        return context.Appointments.AsNoTracking()
            .Where(a => a.PatientEmail == normalizedEmail)
            .OrderByDescending(a => a.AppointmentDateTime)
            .ThenByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Appointment>> GetBookingsByDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var start = date.Date;
        return context.Appointments.AsNoTracking()
            .Where(a => a.AppointmentDateTime.Date == start)
            .OrderBy(a => a.AppointmentDateTime)
            .ToListAsync(cancellationToken);
    }

    public Task<Appointment?> GetBookingAsync(int id, CancellationToken cancellationToken = default) =>
        context.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Appointment?> GetBookingByStripeSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        context.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.StripeSessionId == sessionId, cancellationToken);

    public async Task SetStripeSessionAsync(int id, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var updated = await context.Appointments
            .Where(a => a.Id == id && a.Status == "Pending" && a.StripeSessionId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.StripeSessionId, sessionId), cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("The pending appointment could not be linked to checkout.");
        }
    }

    public async Task MarkDepositPaidAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await context.Appointments
            .Where(a => a.StripeSessionId == sessionId && a.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Status, "Booked"), cancellationToken);
    }

    public async Task<bool> UpdateStatusAsync(int id, string patientEmail, string status,
        CancellationToken cancellationToken = default)
    {
        if (status != "Cancelled")
        {
            throw new ArgumentException("Only cancellation is supported for patient status updates.", nameof(status));
        }

        var normalizedEmail = NormalizeEmail(patientEmail);
        var updated = await context.Appointments
            .Where(a => a.Id == id && a.PatientEmail == normalizedEmail && a.Status == "Booked")
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Status, status), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> UpdateReceptionStatusAsync(
        int id,
        DateTime appointmentDate,
        string status,
        CancellationToken cancellationToken = default)
    {
        var allowedPreviousStatuses = status switch
        {
            "Arrived" => new[] { "Booked" },
            "Done" => new[] { "Arrived" },
            "No-show" => new[] { "Booked", "Arrived" },
            _ => throw new ArgumentException("Unsupported reception status.", nameof(status))
        };

        var updated = await context.Appointments
            .Where(appointment => appointment.Id == id
                && appointment.AppointmentDateTime.Date == appointmentDate.Date
                && allowedPreviousStatuses.Contains(appointment.Status))
            .ExecuteUpdateAsync(setters => setters.SetProperty(appointment => appointment.Status, status), cancellationToken);
        return updated == 1;
    }

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToLowerInvariant();
    }
}

public class AppointmentStorageException(string message, Exception innerException) : Exception(message, innerException);
