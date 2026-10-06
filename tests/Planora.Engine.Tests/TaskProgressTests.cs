using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TaskProgressTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static (Project, ProjectTask, ProjectTask) TwoInChain()
    {
        var p = new Project("Test", D(5, 8));
        var a = p.AddTask("A", 4 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Link(a, b);
        ProjectScheduler.Schedule(p);
        return (p, a, b);
    }

    [Fact]
    public void MarkOnTrackCompletesWorkUpToTheStatusDate()
    {
        var (p, a, b) = TwoInChain();
        TaskProgress.MarkOnTrack(p, a, D(7, 8));       // two of four days done
        Assert.Equal(50, a.PercentComplete);
        Assert.Equal(D(5, 8), a.ActualStart);
        TaskProgress.MarkOnTrack(p, b, D(7, 8));       // not started yet
        Assert.Equal(0, b.PercentComplete);
        TaskProgress.MarkOnTrack(p, a, D(20, 8));
        Assert.Equal(100, a.PercentComplete);
        Assert.Equal(a.Finish, a.ActualFinish);
    }

    [Fact]
    public void UpdateTasksWithActualAndRemainingDurationResetsDuration()
    {
        var (p, a, _) = TwoInChain();
        TaskProgress.UpdateTask(p, a, actualDurationMinutes: 3 * Day, remainingDurationMinutes: 3 * Day);
        Assert.Equal(6 * Day, a.DurationMinutes);
        Assert.Equal(50, a.PercentComplete);

        TaskProgress.UpdateTask(p, a, remainingDurationMinutes: Day);
        Assert.Equal(4 * Day, a.DurationMinutes);
        Assert.Equal(75, a.PercentComplete);
    }

    [Fact]
    public void ActualFinishCompletesTaskAndSetsDuration()
    {
        var (p, a, b) = TwoInChain();
        TaskProgress.UpdateTask(p, a, actualStart: D(6, 8), actualFinish: D(8, 17));
        ProjectScheduler.Schedule(p);
        Assert.Equal(100, a.PercentComplete);
        Assert.Equal(3 * Day, a.DurationMinutes);
        Assert.Equal((D(6, 8), D(8, 17)), (a.Start, a.Finish));
        Assert.Equal(D(9, 8), b.Start);
    }

    [Fact]
    public void UpdateProjectCompleteThroughDate()
    {
        var (p, a, b) = TwoInChain();
        var changed = TaskProgress.UpdateCompleteThrough(p, D(10, 8), UpdateProgressMode.Proportional);
        Assert.Equal(new[] { a, b }, changed);
        Assert.Equal(100, a.PercentComplete);
        Assert.Equal(50, b.PercentComplete);   // Friday done of Fri-Mon

        var (p2, a2, b2) = TwoInChain();
        TaskProgress.UpdateCompleteThrough(p2, D(10, 8), UpdateProgressMode.ZeroOrHundred);
        Assert.Equal(100, a2.PercentComplete);
        Assert.Equal(0, b2.PercentComplete);
    }

    [Fact]
    public void RescheduleUncompletedWorkSplitsInProgressAndDelaysUnstarted()
    {
        var (p, a, b) = TwoInChain();
        TaskProgress.SetPercentComplete(a, 25);        // one day done
        var changed = TaskProgress.RescheduleUncompletedWork(p, D(12, 8));
        ProjectScheduler.Schedule(p);

        Assert.Contains(a, changed);
        Assert.Single(a.Splits);
        Assert.Equal(D(14, 17), a.Finish);             // three days left from Monday 12th
        Assert.Equal(D(15, 8), b.Start);
    }

    [Fact]
    public void PhysicalPercentCompleteDrivesEarnedValueWhenChosen()
    {
        var p = new Project("Test", D(5, 8));
        var bob = p.AddResource("Bob");
        bob.StandardRate = 10;
        var a = p.AddTask("A", 2 * Day);
        p.Assign(a, bob);
        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        TaskProgress.SetPercentComplete(a, 50);
        a.PhysicalPercentComplete = 20;
        p.StatusDate = D(6, 17);

        ProjectScheduler.Schedule(p);
        Assert.Equal(80m, a.EarnedValue.Bcwp);
        a.EarnedValueMethod = EarnedValueMethod.PhysicalPercentComplete;
        ProjectScheduler.Schedule(p);
        Assert.Equal(32m, a.EarnedValue.Bcwp);
    }
}
