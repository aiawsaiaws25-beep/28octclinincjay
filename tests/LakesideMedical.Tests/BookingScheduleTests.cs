using LakesideMedical.Models;

namespace LakesideMedical.Tests;

public class BookingScheduleTests
{
    private readonly TimeProvider clock = new FixedClock();

    [Fact]
    public void SlotsCoverOpeningHoursInThirtyMinuteSteps()
    {
        Assert.Equal(24, BookingSchedule.TimeSlots.Count);
        Assert.Equal("09:00", BookingSchedule.TimeSlots[0]);
        Assert.Equal("20:30", BookingSchedule.TimeSlots[^1]);
        for (var index = 1; index < BookingSchedule.TimeSlots.Count; index++)
        {
            Assert.Equal(TimeSpan.FromMinutes(30),
                TimeOnly.Parse(BookingSchedule.TimeSlots[index]) - TimeOnly.Parse(BookingSchedule.TimeSlots[index - 1]));
        }
        Assert.Equal(4, BookingSchedule.Doctors.Count);
    }

    [Theory]
    [InlineData(2030, 1, 7, 9, 0, true)]
    [InlineData(2030, 1, 7, 20, 30, true)]
    [InlineData(2030, 1, 7, 21, 0, false)]
    [InlineData(2030, 1, 7, 8, 30, false)]
    [InlineData(2030, 1, 7, 9, 15, false)]
    [InlineData(2030, 1, 6, 9, 0, false)]
    [InlineData(2030, 1, 13, 9, 0, false)]
    [InlineData(2029, 12, 31, 9, 0, false)]
    public void ScheduleRejectsPastClosedAndOffGridTimes(int year, int month, int day, int hour, int minute, bool expected)
    {
        Assert.Equal(expected, BookingSchedule.IsValidAppointmentTime(new DateTime(year, month, day, hour, minute, 0), clock));
    }

    [Theory]
    [InlineData("21:00")]
    [InlineData("09:15")]
    [InlineData("bad-time")]
    public void DateTimeParserRejectsInvalidSlots(string time) =>
        Assert.Null(BookingSchedule.GetAppointmentDateTime(new DateTime(2030, 1, 7), time));
}

public class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2030, 1, 7, 0, 0, 0, TimeSpan.Zero);
}
