using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class AdvancedSchedulingTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static Project NewProject() => new("Test", D(5, 8));

    [Fact]
    public void ElapsedDurationIgnoresWorkingTime()
    {
        var p = NewProject();
        var cure = p.AddTask("Cure concrete", 2 * 24 * 60);
        cure.IsElapsedDuration = true;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 8), cure.Finish);
    }

    [Fact]
    public void ElapsedLagRunsOvernight()
    {
        var p = NewProject();
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", Day);
        var link = p.Link(a, b, LinkType.FinishToStart, 12 * 60);
        link.LagFormat = LagFormat.ElapsedTime;

        ProjectScheduler.Schedule(p);

        // Mon 17:00 + 12 clock hours = Tue 05:00 -> next working moment Tue 08:00.
        Assert.Equal(D(6, 8), b.Start);
    }

    [Fact]
    public void PercentLagUsesPredecessorDuration()
    {
        var p = NewProject();
        var a = p.AddTask("A", 4 * Day);
        var b = p.AddTask("B", Day);
        var link = p.Link(a, b, LinkType.StartToStart);
        link.LagFormat = LagFormat.PercentOfPredecessor;
        link.LagPercent = 50;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 8), b.Start);
    }

    [Fact]
    public void SplitTaskHasGapAndLaterFinish()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", Day);
        p.Link(a, b);
        a.Split(Day, Day);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 17), a.Finish);
        Assert.Equal(2 * Day, a.DurationMinutes);
        Assert.Equal(new[] { new TaskSegment(D(5, 8), D(5, 17)), new TaskSegment(D(7, 8), D(7, 17)) }, a.Segments);
        Assert.Equal(D(8, 8), b.Start);
    }

    [Fact]
    public void SplitMustFallInsideTask()
    {
        var p = NewProject();
        var a = p.AddTask("A", Day);
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Split(Day, 60));
    }

    [Fact]
    public void CompleteThroughFollowsPercentComplete()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        a.PercentComplete = 50;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(5, 17), a.CompleteThrough);
    }

    [Fact]
    public void ScheduleFromFinishPlacesTasksAgainstFinishDate()
    {
        var p = NewProject();
        p.ScheduleFromFinish = true;
        p.FinishDate = D(16, 17);
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 3 * Day);
        var c = p.AddTask("C", Day);
        p.Link(a, b);
        foreach (var t in new[] { a, b, c })
            t.ConstraintType = ConstraintType.AsLateAsPossible;

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(14, 8), D(16, 17)), (b.Start, b.Finish));
        Assert.Equal((D(12, 8), D(13, 17)), (a.Start, a.Finish));
        Assert.Equal(D(16, 8), c.Start);
        Assert.Equal(D(12, 8), p.Start);
        Assert.True(a.IsCritical && b.IsCritical);
        Assert.Equal(4 * Day, c.TotalFloatMinutes);
    }

    [Fact]
    public void UncompletedWorkMovesToStatusDate()
    {
        var p = NewProject();
        var a = p.AddTask("A", 4 * Day);
        var b = p.AddTask("B", Day);
        a.ActualStart = D(5, 8);
        a.PercentComplete = 25;
        p.StatusDate = D(7, 8);
        p.Options.RescheduleUncompletedWorkToStatusDate = true;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(9, 17), a.Finish);
        Assert.Equal(new[] { new TaskSegment(D(5, 8), D(5, 17)), new TaskSegment(D(7, 8), D(9, 17)) }, a.Segments);
        Assert.Equal(D(5, 17), a.CompleteThrough);
        Assert.Equal(D(7, 8), b.Start);
    }

    [Fact]
    public void InProgressTaskWithoutReschedulingKeepsOriginalFinish()
    {
        var p = NewProject();
        var a = p.AddTask("A", 4 * Day);
        a.ActualStart = D(5, 8);
        a.PercentComplete = 25;
        p.StatusDate = D(7, 8);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(8, 17), a.Finish);
        Assert.Single(a.Segments);
    }

    [Fact]
    public void RecurringTaskGeneratesConstrainedOccurrences()
    {
        var p = NewProject();
        var meeting = p.AddRecurringTask("Status meeting", new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = { DayOfWeek.Monday },
            Start = new DateOnly(2026, 1, 5),
            Occurrences = 3,
            OccurrenceDurationMinutes = 60,
        });

        ProjectScheduler.Schedule(p);

        Assert.True(meeting.IsRecurring);
        Assert.Equal(new[] { "Status meeting 1", "Status meeting 2", "Status meeting 3" }, meeting.Children.Select(c => c.Name));
        Assert.Equal(new[] { D(5, 8), D(12, 8), D(19, 8) }, meeting.Children.Select(c => c.Start));
        Assert.Equal((D(5, 8), D(19, 9)), (meeting.Start, meeting.Finish));
    }

    [Fact]
    public void RecurringTaskCanBeRegenerated()
    {
        var p = NewProject();
        var pattern = new RecurrencePattern { Frequency = RecurrenceFrequency.Daily, Start = new DateOnly(2026, 1, 5), Occurrences = 7 };
        var task = p.AddRecurringTask("Stand-up", pattern);
        Assert.Equal(5, task.Children.Count); // weekend occurrences are skipped

        p.SetRecurrence(task, new RecurrencePattern { Frequency = RecurrenceFrequency.Daily, Start = new DateOnly(2026, 1, 5), Occurrences = 2 });
        Assert.Equal(2, task.Children.Count);
        Assert.Equal(3, p.Tasks.Count);
    }

    [Theory]
    [InlineData(2, null, 10)] // second Tuesday of Feb 2026
    [InlineData(5, null, 24)] // last Tuesday of Feb 2026
    [InlineData(null, 31, 28)] // day 31 clamps to the end of February
    public void MonthlyPatternDays(int? week, int? dayOfMonth, int expectedDay)
    {
        var pattern = new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Monthly,
            WeekOfMonth = week,
            DayOfMonth = dayOfMonth,
            DayOfWeek = DayOfWeek.Tuesday,
            Start = new DateOnly(2026, 2, 1),
            Occurrences = 1,
        };
        Assert.Equal(new DateOnly(2026, 2, expectedDay), pattern.Dates().Single());
    }

    [Fact]
    public void YearlyPatternAndEndDate()
    {
        var pattern = new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Yearly,
            Month = 12,
            DayOfMonth = 25,
            Start = new DateOnly(2026, 1, 1),
            End = new DateOnly(2028, 12, 31),
        };
        Assert.Equal(new[] { 2026, 2027, 2028 }, pattern.Dates().Select(d => d.Year));
    }

    [Fact]
    public void RecurrenceWithoutEndIsRejected()
    {
        var pattern = new RecurrencePattern { Frequency = RecurrenceFrequency.Daily, Start = new DateOnly(2026, 1, 5) };
        Assert.Throws<InvalidOperationException>(() => pattern.Dates().ToList());
    }
}
