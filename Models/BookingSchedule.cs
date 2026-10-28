using System.Globalization;

namespace LakesideMedical.Models;

public static class BookingSchedule
{
    public static IReadOnlyDictionary<string, string> Doctors { get; } =
        new Dictionary<string, string>
        {
            ["General Physician"] = "Dr. Sara Ahmed",
            ["ENT"] = "Dr. Omar Khan",
            ["Dentist"] = "Dr. Lina Haddad",
            ["Cardiologist"] = "Dr. Raj Mehta"
        }.AsReadOnly();

    public static IReadOnlyList<string> TimeSlots { get; } = Enumerable.Range(18, 24)
        .Select(halfHour => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(halfHour * 30))
            .ToString("HH:mm", CultureInfo.InvariantCulture))
        .ToArray();

    public static DateTime DubaiNow(TimeProvider clock) =>
        clock.GetUtcNow().ToOffset(TimeSpan.FromHours(4)).DateTime;

    public static DateTime? GetAppointmentDateTime(DateTime? date, string? time)
    {
        if (date is null || !TimeSlots.Contains(time ?? "")
            || !TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var slot))
        {
            return null;
        }

        return DateTime.SpecifyKind(date.Value.Date.Add(slot.ToTimeSpan()), DateTimeKind.Unspecified);
    }

    public static string DisplayTime(string time) =>
        TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture)
            .ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static bool IsValidAppointmentTime(DateTime value, TimeProvider clock) =>
        value.DayOfWeek != DayOfWeek.Sunday
        && value > DubaiNow(clock)
        && value.TimeOfDay >= TimeSpan.FromHours(9)
        && value.TimeOfDay < TimeSpan.FromHours(21)
        && value.Minute % 30 == 0
        && value.Second == 0
        && value.Ticks % TimeSpan.TicksPerSecond == 0;
}
