using Planora.Engine.Model;

namespace Planora.Engine.Tests;

public class DurationTextTests
{
    private static readonly ScheduleOptions Options = new();

    [Theory]
    [InlineData("3d", 1440, false, false)]
    [InlineData("3", 1440, false, false)]
    [InlineData("2 wks", 4800, false, false)]
    [InlineData("1.5 days", 720, false, false)]
    [InlineData("4h?", 240, false, true)]
    [InlineData("30 mins", 30, false, false)]
    [InlineData("1mo", 9600, false, false)]
    [InlineData("5ed", 7200, true, false)]
    [InlineData("2 ewks", 20160, true, false)]
    [InlineData("90em", 90, true, false)]
    public void ParsesDurations(string text, long minutes, bool elapsed, bool estimated)
    {
        var d = DurationText.Parse(text, Options);
        Assert.Equal((minutes, elapsed, estimated), (d.Minutes, d.IsElapsed, d.IsEstimated));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3 parsecs")]
    [InlineData("-1d")]
    public void RejectsInvalidDurations(string text) =>
        Assert.False(DurationText.TryParse(text, Options, out _));

    [Fact]
    public void ParsesLags()
    {
        Assert.True(DurationText.TryParseLag("-1d", Options, out var lead));
        Assert.Equal(new ParsedLag(LagFormat.WorkingTime, -480, 0), lead);

        Assert.True(DurationText.TryParseLag("50%", Options, out var percent));
        Assert.Equal(new ParsedLag(LagFormat.PercentOfPredecessor, 0, 50), percent);

        Assert.True(DurationText.TryParseLag("2ed", Options, out var elapsed));
        Assert.Equal(new ParsedLag(LagFormat.ElapsedTime, 2880, 0), elapsed);
    }

    [Fact]
    public void FormatsDurations()
    {
        Assert.Equal("3 days", DurationText.Format(1440, Options));
        Assert.Equal("1 day", DurationText.Format(480, Options));
        Assert.Equal("0 days", DurationText.Format(0, Options));
        Assert.Equal("2 edays", DurationText.Format(2880, Options, isElapsed: true));
        Assert.Equal("4 hrs?", DurationText.Format(240, Options, DurationUnit.Hours, isEstimated: true));
    }

    [Fact]
    public void HonoursHoursPerDaySetting()
    {
        var options = new ScheduleOptions { MinutesPerDay = 420 };
        Assert.Equal(840, DurationText.Parse("2d", options).Minutes);
    }
}
