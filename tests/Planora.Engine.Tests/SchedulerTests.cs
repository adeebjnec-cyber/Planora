using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class SchedulerTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static Project NewProject() => new("Test", D(5, 8));

    [Fact]
    public void FinishToStartChain()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 3 * Day);
        p.Link(a, b);

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(5, 8), D(6, 17)), (a.Start, a.Finish));
        Assert.Equal((D(7, 8), D(9, 17)), (b.Start, b.Finish));
        Assert.Equal(D(9, 17), p.Finish);
    }

    /// <summary>
    /// Classic CPM network: A(3d) -> C(4d) -> E(2d) and B(2d) -> D(1d) -> E.
    /// Critical path A-C-E; B and D have 4 days of total float.
    /// </summary>
    [Fact]
    public void CriticalPathAndFloat()
    {
        var p = NewProject();
        var a = p.AddTask("A", 3 * Day);
        var b = p.AddTask("B", 2 * Day);
        var c = p.AddTask("C", 4 * Day);
        var d = p.AddTask("D", 1 * Day);
        var e = p.AddTask("E", 2 * Day);
        p.Link(a, c);
        p.Link(b, d);
        p.Link(c, e);
        p.Link(d, e);

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(8, 8), D(13, 17)), (c.Start, c.Finish));
        Assert.Equal((D(14, 8), D(15, 17)), (e.Start, e.Finish));
        Assert.Equal(D(7, 8), d.EarlyStart);
        Assert.Equal(D(13, 8), d.LateStart);
        Assert.Equal(D(13, 17), d.LateFinish);

        Assert.True(a.IsCritical && c.IsCritical && e.IsCritical);
        Assert.False(b.IsCritical || d.IsCritical);
        Assert.Equal(4 * Day, b.TotalFloatMinutes);
        Assert.Equal(4 * Day, d.TotalFloatMinutes);
        Assert.Equal(0, b.FreeFloatMinutes);
        Assert.Equal(4 * Day, d.FreeFloatMinutes);
        Assert.Equal(0, e.TotalFloatMinutes);
    }

    [Fact]
    public void StartToStartWithLag()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Link(a, b, LinkType.StartToStart, 2 * Day);

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(7, 8), D(8, 17)), (b.Start, b.Finish));
    }

    [Fact]
    public void FinishToFinish()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Link(a, b, LinkType.FinishToFinish);

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(8, 8), D(9, 17)), (b.Start, b.Finish));
    }

    [Fact]
    public void StartToFinish()
    {
        var p = NewProject();
        var a = p.AddTask("A", 1 * Day);
        p.AddTask("Spacer", 0);
        var b = p.AddTask("B", 2 * Day);
        a.ConstraintType = ConstraintType.StartNoEarlierThan;
        a.ConstraintDate = D(12, 8);
        p.Link(a, b, LinkType.StartToFinish);

        ProjectScheduler.Schedule(p);

        // B must finish when A starts (Mon 12th, 08:00) => B runs Thu 8th - Fri 9th.
        Assert.Equal((D(8, 8), D(9, 17)), (b.Start, b.Finish));
    }

    [Fact]
    public void LeadOverlapsTasks()
    {
        var p = NewProject();
        var a = p.AddTask("A", 3 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Link(a, b, LinkType.FinishToStart, -1 * Day);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 8), b.Start);
    }

    [Fact]
    public void MilestoneSitsAtPredecessorFinish()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var m = p.AddTask("Done", 0);
        m.IsMilestone = true;
        p.Link(a, m);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(9, 17), m.Start);
        Assert.Equal(D(9, 17), m.Finish);
        Assert.True(m.IsCritical);
    }

    [Fact]
    public void HolidayDelaysTask()
    {
        var p = NewProject();
        p.Calendar.AddHoliday("Holiday", new DateOnly(2026, 1, 6));
        var a = p.AddTask("A", 2 * Day);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 17), a.Finish);
    }

    [Fact]
    public void StartNoEarlierThan()
    {
        var p = NewProject();
        var a = p.AddTask("A", Day);
        a.ConstraintType = ConstraintType.StartNoEarlierThan;
        a.ConstraintDate = D(10, 8); // Saturday -> next working moment Monday
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(12, 8), a.Start);
    }

    [Fact]
    public void MustStartOnOverridesLinksAndCreatesNegativeFloat()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var b = p.AddTask("B", Day);
        p.Link(a, b);
        b.ConstraintType = ConstraintType.MustStartOn;
        b.ConstraintDate = D(7, 8);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 8), b.Start);
        Assert.Equal(-3 * Day, a.TotalFloatMinutes);
        Assert.True(a.IsCritical);
    }

    [Fact]
    public void MustFinishOn()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        a.ConstraintType = ConstraintType.MustFinishOn;
        a.ConstraintDate = D(9, 17);
        ProjectScheduler.Schedule(p);
        Assert.Equal((D(8, 8), D(9, 17)), (a.Start, a.Finish));
    }

    [Fact]
    public void FinishNoLaterThanReducesFloat()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        var longTask = p.AddTask("Long", 10 * Day);
        a.ConstraintType = ConstraintType.FinishNoLaterThan;
        a.ConstraintDate = D(7, 17);

        ProjectScheduler.Schedule(p);

        Assert.Equal(Day, a.TotalFloatMinutes);
        Assert.Equal(0, longTask.TotalFloatMinutes);
    }

    [Fact]
    public void DeadlineLimitsLateFinishAndIsFlaggedWhenMissed()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        a.Deadline = D(7, 17);

        ProjectScheduler.Schedule(p);

        Assert.True(a.MissesDeadline);
        Assert.Equal(-2 * Day, a.TotalFloatMinutes);
        Assert.Equal(D(9, 17), a.Finish); // deadlines never move the task
    }

    [Fact]
    public void AsLateAsPossibleUsesFreeFloat()
    {
        var p = NewProject();
        var a = p.AddTask("A", 1 * Day);
        var longTask = p.AddTask("Long", 5 * Day);
        var end = p.AddTask("End", 1 * Day);
        p.Link(a, end);
        p.Link(longTask, end);
        a.ConstraintType = ConstraintType.AsLateAsPossible;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(5, 8), a.EarlyStart);
        Assert.Equal((D(9, 8), D(9, 17)), (a.Start, a.Finish));
        Assert.Equal(D(12, 8), end.Start);
    }

    [Fact]
    public void ManualTaskKeepsItsDates()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var m = p.AddTask("Manual", Day);
        p.Link(a, m);
        m.Mode = TaskMode.ManuallyScheduled;
        m.ManualStart = D(6, 8);
        m.ManualFinish = D(6, 17);

        ProjectScheduler.Schedule(p);

        Assert.Equal((D(6, 8), D(6, 17)), (m.Start, m.Finish));
    }

    [Fact]
    public void SummaryRollsUpChildren()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 3 * Day);
        p.Indent(a);
        p.Indent(b);
        p.Link(a, b);
        a.PercentComplete = 100;

        ProjectScheduler.Schedule(p);

        Assert.True(phase.IsSummary);
        Assert.Equal("1.2", b.WbsCode);
        Assert.Equal((D(5, 8), D(9, 17)), (phase.Start, phase.Finish));
        Assert.Equal(5 * Day, phase.DurationMinutes);
        Assert.Equal(40, phase.PercentComplete, 3);
    }

    [Fact]
    public void SummaryActualDatesRollUp()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", Day);
        p.Indent(a);
        p.Indent(b);
        p.Link(a, b);
        a.ActualStart = D(5, 8);
        a.ActualFinish = D(5, 17);
        a.PercentComplete = 100;

        ProjectScheduler.Schedule(p);
        Assert.Equal(D(5, 8), phase.ActualStart);
        Assert.Null(phase.ActualFinish);

        b.ActualStart = D(6, 8);
        b.ActualFinish = D(6, 17);
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(6, 17), phase.ActualFinish);
    }

    [Fact]
    public void LinkIntoSummaryDelaysAllItsChildren()
    {
        var p = NewProject();
        var design = p.AddTask("Design", 2 * Day);
        var build = p.AddTask("Build");
        var x = p.AddTask("X", Day);
        var y = p.AddTask("Y", Day);
        p.Indent(x);
        p.Indent(y);
        p.Link(design, build);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 8), x.Start);
        Assert.Equal(D(7, 8), y.Start);
        Assert.Equal(D(7, 17), build.Finish);
    }

    [Fact]
    public void LinkOutOfSummaryWaitsForAllChildren()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var x = p.AddTask("X", Day);
        var y = p.AddTask("Y", 3 * Day);
        p.Indent(x);
        p.Indent(y);
        var next = p.AddTask("Next", Day);
        p.Link(phase, next);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(8, 8), next.Start);
        Assert.True(y.IsCritical);
        Assert.False(x.IsCritical);
    }

    [Fact]
    public void CircularDependencyIsReported()
    {
        var p = NewProject();
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", Day);
        var c = p.AddTask("C", Day);
        p.Link(a, b);
        p.Link(b, c);
        p.Link(c, a);

        var ex = Assert.Throws<SchedulingException>(() => ProjectScheduler.Schedule(p));
        Assert.Equal(3, ex.Tasks.Count);
    }

    [Fact]
    public void InactiveTasksAreIgnored()
    {
        var p = NewProject();
        var a = p.AddTask("A", 5 * Day);
        var b = p.AddTask("B", Day);
        p.Link(a, b);
        a.IsActive = false;

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(5, 8), b.Start);
    }

    [Fact]
    public void TaskCalendarIsUsed()
    {
        var p = NewProject();
        var a = p.AddTask("A", 2 * Day);
        a.Calendar = Calendars.WorkingCalendar.Create24Hours();

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(6, 0), a.Finish); // 16 hours straight from 08:00 Monday is 00:00 Tuesday
    }

    [Fact]
    public void MultipleCriticalPaths()
    {
        var p = NewProject();
        var shortTask = p.AddTask("Short", Day);
        p.AddTask("Long", 5 * Day);
        p.Options.CalculateMultipleCriticalPaths = true;

        ProjectScheduler.Schedule(p);

        Assert.True(shortTask.IsCritical);
    }
}
