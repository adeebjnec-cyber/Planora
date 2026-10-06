using Planora.Engine.Calendars;

namespace Planora.Engine.Tests;

public class CalendarTests
{
    private const int Day = 480;
    private static readonly WorkingCalendar Standard = WorkingCalendar.CreateStandard();

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    [Fact]
    public void OneDayFromMondayMorningEndsMondayEvening() =>
        Assert.Equal(D(5, 17), Standard.AddWorkingMinutes(D(5, 8), Day));

    [Fact]
    public void LunchBreakIsSkipped() =>
        Assert.Equal(D(5, 14), Standard.AddWorkingMinutes(D(5, 8), 300));

    [Fact]
    public void WeekendIsSkipped() =>
        Assert.Equal(D(12, 17), Standard.AddWorkingMinutes(D(9, 8), 2 * Day));

    [Fact]
    public void AddingFromEndOfDayStartsNextWorkingDay() =>
        Assert.Equal(D(12, 9), Standard.AddWorkingMinutes(D(9, 17), 60));

    [Fact]
    public void SubtractingLandsOnStartOfWork()
    {
        Assert.Equal(D(5, 8), Standard.AddWorkingMinutes(D(5, 17), -Day));
        Assert.Equal(D(2, 16), Standard.AddWorkingMinutes(D(5, 8), -60));
    }

    [Fact]
    public void ZeroIsUnchanged() =>
        Assert.Equal(D(9, 17), Standard.AddWorkingMinutes(D(9, 17), 0));

    [Fact]
    public void WorkingMinutesBetweenCountsOnlyWorkingTime()
    {
        Assert.Equal(2 * Day, Standard.WorkingMinutesBetween(D(5, 8), D(6, 17)));
        Assert.Equal(Day, Standard.WorkingMinutesBetween(D(9, 8), D(12, 8)));
        Assert.Equal(-Day, Standard.WorkingMinutesBetween(D(12, 8), D(9, 8)));
        Assert.Equal(0, Standard.WorkingMinutesBetween(D(9, 17), D(12, 8)));
    }

    [Fact]
    public void NextAndPreviousWorkingMoments()
    {
        Assert.Equal(D(12, 8), Standard.NextWorkingMoment(D(10, 11)));
        Assert.Equal(D(5, 13), Standard.NextWorkingMoment(D(5, 12)));
        Assert.Equal(D(5, 10), Standard.NextWorkingMoment(D(5, 10)));
        Assert.Equal(D(9, 17), Standard.PreviousWorkingMoment(D(12, 8)));
        Assert.Equal(D(5, 12), Standard.PreviousWorkingMoment(D(5, 12, 30)));
    }

    [Fact]
    public void HolidayIsNonWorking()
    {
        var cal = WorkingCalendar.CreateStandard();
        cal.AddHoliday("Holiday", new DateOnly(2026, 1, 6));
        Assert.False(cal.IsWorkingDay(new DateOnly(2026, 1, 6)));
        Assert.Equal(D(7, 17), cal.AddWorkingMinutes(D(5, 8), 2 * Day));
    }

    [Fact]
    public void ExceptionCanMakeSaturdayWorking()
    {
        var cal = WorkingCalendar.CreateStandard();
        cal.AddException("Overtime", new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), WorkingTime.FromHours(9, 13));
        Assert.Equal(D(10, 13), cal.AddWorkingMinutes(D(9, 17), 240));
    }

    [Fact]
    public void DerivedCalendarInheritsBaseHolidaysAndWeek()
    {
        var baseCal = WorkingCalendar.CreateStandard();
        baseCal.AddHoliday("Holiday", new DateOnly(2026, 1, 7));
        var resourceCal = new WorkingCalendar("Part-timer", baseCal);
        resourceCal.SetWorkWeekDay(DayOfWeek.Monday);

        Assert.False(resourceCal.IsWorkingDay(new DateOnly(2026, 1, 5)));   // own override
        Assert.True(resourceCal.IsWorkingDay(new DateOnly(2026, 1, 6)));    // inherited week
        Assert.False(resourceCal.IsWorkingDay(new DateOnly(2026, 1, 7)));   // inherited holiday
        Assert.True(resourceCal.IsWorkWeekDayInherited(DayOfWeek.Tuesday));
    }

    [Fact]
    public void TwentyFourHourCalendarRunsThroughMidnight()
    {
        var cal = WorkingCalendar.Create24Hours();
        Assert.Equal(new DateTime(2026, 1, 6, 2, 0, 0), cal.AddWorkingMinutes(D(5, 20), 360));
    }

    [Fact]
    public void CalendarWithoutWorkingTimeThrows()
    {
        var cal = new WorkingCalendar("Empty");
        Assert.Throws<InvalidOperationException>(() => cal.AddWorkingMinutes(D(5, 8), 60));
    }

    [Fact]
    public void OverlappingWorkingTimesAreRejected()
    {
        var cal = new WorkingCalendar("Bad");
        Assert.Throws<ArgumentException>(() =>
            cal.SetWorkWeekDay(DayOfWeek.Monday, WorkingTime.FromHours(8, 12), WorkingTime.FromHours(11, 15)));
    }
}
