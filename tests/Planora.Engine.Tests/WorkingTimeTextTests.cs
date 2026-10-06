using Planora.Engine.Calendars;

namespace Planora.Engine.Tests;

public class WorkingTimeTextTests
{
    [Fact]
    public void ParsesAndFormatsRanges()
    {
        var times = WorkingTimeText.Parse("13:00-17:00, 8-12");
        Assert.Equal("08:00-12:00, 13:00-17:00", WorkingTimeText.Format(times));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nonworking")]
    [InlineData("  nonworking ")]
    public void EmptyMeansNonWorking(string text) => Assert.Empty(WorkingTimeText.Parse(text));

    [Fact]
    public void FullDay() => Assert.Equal("00:00-24:00", WorkingTimeText.Format(WorkingTimeText.Parse("0:00-24:00")));

    [Theory]
    [InlineData("8-")]
    [InlineData("25:00-26:00")]
    [InlineData("8:00-12:00, 11:00-13:00")]
    [InlineData("12:00-8:00")]
    public void RejectsInvalid(string text) =>
        Assert.Throws<FormatException>(() => WorkingTimeText.Parse(text));

    [Fact]
    public void RebaseKeepsOwnSettings()
    {
        var standard = WorkingCalendar.CreateStandard();
        var nights = WorkingCalendar.CreateNightShift();
        var resource = new WorkingCalendar("Res", standard);
        resource.SetWorkWeekDay(DayOfWeek.Friday);
        resource.AddHoliday("Leave", new DateOnly(2026, 1, 6));

        var rebased = resource.Rebase(nights);

        Assert.Same(nights, rebased.BaseCalendar);
        Assert.Empty(rebased.GetWorkWeekDay(DayOfWeek.Friday));
        Assert.Equal(nights.GetWorkWeekDay(DayOfWeek.Monday), rebased.GetWorkWeekDay(DayOfWeek.Monday));
        Assert.Single(rebased.Exceptions);

        var standalone = resource.Rebase(null);
        Assert.Equal(standard.GetWorkWeekDay(DayOfWeek.Monday), standalone.GetWorkWeekDay(DayOfWeek.Monday));
    }
}
