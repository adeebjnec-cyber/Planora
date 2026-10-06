using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ResourceTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static Project NewProject() => new("Test", D(5, 8));

    [Fact]
    public void WorkAndCostFromRate()
    {
        var p = NewProject();
        var task = p.AddTask("Build", 2 * Day);
        var dev = p.AddResource("Developer");
        dev.StandardRate = 50;
        dev.CostPerUse = 100;
        var a = p.Assign(task, dev);

        ProjectScheduler.Schedule(p);

        Assert.Equal(2 * Day, a.WorkMinutes);
        Assert.Equal(900m, a.Cost);
        Assert.Equal(900m, task.Cost);
        Assert.Equal(2 * Day, task.WorkMinutes);
        Assert.Equal(900m, p.Cost);
        Assert.Equal(2 * Day, dev.WorkMinutes);
    }

    [Fact]
    public void EffortDrivenFixedUnitsShortensTask()
    {
        var p = NewProject();
        var task = p.AddTask("Dig", 4 * Day);
        task.IsEffortDriven = true;
        p.Assign(task, p.AddResource("A"));
        var b = p.Assign(task, p.AddResource("B"));

        ProjectScheduler.Schedule(p);

        Assert.Equal(2 * Day, task.DurationMinutes);
        Assert.Equal(2 * Day, b.WorkMinutes);
        Assert.Equal(4 * Day, task.WorkMinutes);
        Assert.Equal(D(6, 17), task.Finish);
    }

    [Fact]
    public void NonEffortDrivenAddsWork()
    {
        var p = NewProject();
        var task = p.AddTask("Review", 4 * Day);
        p.Assign(task, p.AddResource("A"));
        p.Assign(task, p.AddResource("B"));

        ProjectScheduler.Schedule(p);

        Assert.Equal(4 * Day, task.DurationMinutes);
        Assert.Equal(8 * Day, task.WorkMinutes);
    }

    [Fact]
    public void EffortDrivenFixedDurationLowersUnits()
    {
        var p = NewProject();
        var task = p.AddTask("Paint", 4 * Day);
        task.Type = TaskType.FixedDuration;
        task.IsEffortDriven = true;
        var a = p.Assign(task, p.AddResource("A"));
        var b = p.Assign(task, p.AddResource("B"));

        ProjectScheduler.Schedule(p);

        Assert.Equal(4 * Day, task.DurationMinutes);
        Assert.Equal(0.5, a.Units, 3);
        Assert.Equal(0.5, b.Units, 3);
    }

    [Fact]
    public void FixedWorkDurationChangeAdjustsUnits()
    {
        var p = NewProject();
        var task = p.AddTask("Write", 4 * Day);
        task.Type = TaskType.FixedWork;
        var a = p.Assign(task, p.AddResource("Writer"));

        p.SetDuration(task, 8 * Day);

        Assert.Equal(0.5, a.Units, 3);
        Assert.Equal(4 * Day, a.WorkMinutes);
    }

    [Fact]
    public void FixedUnitsUnitChangeKeepsWorkAndChangesDuration()
    {
        var p = NewProject();
        var task = p.AddTask("Test", 2 * Day);
        var a = p.Assign(task, p.AddResource("Tester"), 0.5);
        Assert.Equal(Day, a.WorkMinutes);

        p.SetUnits(a, 1.0);
        ProjectScheduler.Schedule(p);

        Assert.Equal(Day, task.DurationMinutes);
        Assert.Equal(D(5, 17), task.Finish);
    }

    [Fact]
    public void UnassigningOnEffortDrivenTaskRestoresDuration()
    {
        var p = NewProject();
        var task = p.AddTask("Dig", 4 * Day);
        task.IsEffortDriven = true;
        var a = p.AddResource("A");
        var b = p.AddResource("B");
        p.Assign(task, a);
        p.Assign(task, b);

        p.Unassign(task, b);

        Assert.Equal(4 * Day, task.DurationMinutes);
        Assert.Equal(4 * Day, task.Assignments.Single().WorkMinutes);
    }

    [Fact]
    public void ResourceCalendarStretchesTask()
    {
        var p = NewProject();
        var task = p.AddTask("Install", 2 * Day);
        var tech = p.AddResource("Technician");
        tech.Calendar!.AddHoliday("Day off", new DateOnly(2026, 1, 6));
        p.Assign(task, tech);

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(7, 17), task.Finish);
        Assert.Equal(3 * Day, task.DurationMinutes);
    }

    [Fact]
    public void TaskCalendarIntersectsResourceCalendar()
    {
        var p = NewProject();
        var task = p.AddTask("Morning job", Day);
        var mornings = Calendars.WorkingCalendar.CreateStandard("Mornings");
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            mornings.SetWorkWeekDay(day, Calendars.WorkingTime.FromHours(8, 12));
        task.Calendar = mornings;
        p.Assign(task, p.AddResource("Worker"));

        ProjectScheduler.Schedule(p);

        Assert.Equal(D(6, 12), task.Finish); // 8 hours of mornings
    }

    [Fact]
    public void BellContourStretchesAssignment()
    {
        var p = NewProject();
        var task = p.AddTask("Peak work", 2 * Day);
        var a = p.Assign(task, p.AddResource("R"));
        a.Contour = WorkContour.Bell;

        ProjectScheduler.Schedule(p);

        Assert.Equal(2 * Day, a.WorkMinutes);
        Assert.Equal(D(8, 17), task.Finish); // average 50% units => twice as long
        Assert.Equal(a.WorkMinutes, Timephased.WorkBetween(a, a.Start, a.Finish), 3);
        Assert.True(Timephased.WorkBetween(a, D(5, 8), D(5, 17)) < Timephased.WorkBetween(a, D(6, 8), D(6, 17)));
    }

    [Fact]
    public void MaterialAndCostResources()
    {
        var p = NewProject();
        var task = p.AddTask("Pour", Day);
        var concrete = p.AddResource("Concrete", ResourceType.Material);
        concrete.MaterialLabel = "m3";
        concrete.StandardRate = 20;
        var permit = p.AddResource("Permit", ResourceType.Cost);
        p.Assign(task, concrete, 10);
        p.Assign(task, permit).CostAmount = 500;
        task.FixedCost = 50;

        ProjectScheduler.Schedule(p);

        Assert.Equal(750m, task.Cost);
        Assert.Equal(0, task.WorkMinutes);
        Assert.Equal(Day, task.DurationMinutes);
    }

    [Fact]
    public void DatedRateChangeIsApplied()
    {
        var p = NewProject();
        var task = p.AddTask("Long job", 4 * Day);
        var r = p.AddResource("R");
        r.StandardRate = 50;
        r.AddRateChange(0, new CostRate(D(7, 0), 100));
        p.Assign(task, r);

        ProjectScheduler.Schedule(p);

        Assert.Equal(16 * 50m + 16 * 100m, task.Cost);
    }

    [Fact]
    public void RateTableSelection()
    {
        var p = NewProject();
        var task = p.AddTask("Job", Day);
        var r = p.AddResource("R");
        r.StandardRate = 50;
        r.CostRateTables[1][0] = new CostRate(DateTime.MinValue, 80);
        p.Assign(task, r).CostRateTable = 1;

        ProjectScheduler.Schedule(p);

        Assert.Equal(640m, task.Cost);
    }

    [Theory]
    [InlineData(CostAccrual.Prorated, 400)]
    [InlineData(CostAccrual.Start, 800)]
    [InlineData(CostAccrual.End, 0)]
    public void ActualCostFollowsAccrual(CostAccrual accrual, decimal expected)
    {
        var p = NewProject();
        var task = p.AddTask("Job", 2 * Day);
        var r = p.AddResource("R");
        r.StandardRate = 50;
        r.AccrueAt = accrual;
        p.Assign(task, r);
        task.PercentComplete = 50;

        ProjectScheduler.Schedule(p);

        Assert.Equal(expected, task.ActualCost);
        Assert.Equal(Day, task.ActualWorkMinutes);
    }

    [Fact]
    public void SummaryRollsUpCostAndWork()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", Day);
        p.Indent(a);
        p.Indent(b);
        var r = p.AddResource("R");
        r.StandardRate = 10;
        p.Assign(a, r);
        b.FixedCost = 25;
        phase.FixedCost = 5;

        ProjectScheduler.Schedule(p);

        Assert.Equal(80m + 25m + 5m, phase.Cost);
        Assert.Equal(Day, phase.WorkMinutes);
        Assert.Equal(110m, p.Cost);
    }

    [Fact]
    public void ParallelAssignmentsAreOverallocated()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", 2 * Day), r);
        p.Assign(p.AddTask("B", 2 * Day), r);

        ProjectScheduler.Schedule(p);

        Assert.True(r.IsOverallocated);
        Assert.Equal(2.0, r.PeakUnits, 3);
        var over = Assert.Single(ResourceAnalysis.FindOverallocations(p));
        Assert.Equal((D(5, 8), D(6, 17)), (over.From, over.To));
    }

    [Fact]
    public void HalfTimeAssignmentsFitTogether()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", 2 * Day), r, 0.5);
        p.Assign(p.AddTask("B", 2 * Day), r, 0.5);

        ProjectScheduler.Schedule(p);

        Assert.False(r.IsOverallocated);
        Assert.Equal(1.0, r.PeakUnits, 3);
    }

    [Fact]
    public void LevelingDelaysLaterTask()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Assign(a, r);
        p.Assign(b, r);

        var result = ResourceLeveler.Level(p);

        Assert.Equal(new[] { b }, result.DelayedTasks);
        Assert.Empty(result.Unresolved);
        Assert.Equal(D(5, 8), a.Start);
        Assert.Equal(D(7, 8), b.Start);
        Assert.False(r.IsOverallocated);
    }

    [Fact]
    public void LevelingRespectsPriority()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Assign(a, r);
        p.Assign(b, r);
        b.Priority = 900;

        ResourceLeveler.Level(p, new LevelingOptions { Order = LevelingOrder.PriorityStandard });

        Assert.Equal(D(5, 8), b.Start);
        Assert.Equal(D(7, 8), a.Start);
    }

    [Fact]
    public void LevelingPrefersTaskWithMoreFloat()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var critical = p.AddTask("Critical", 2 * Day);
        var next = p.AddTask("Next", 3 * Day);
        var slack = p.AddTask("Slack", 2 * Day);
        p.Link(critical, next);
        p.Assign(slack, r);
        p.Assign(critical, r);

        ResourceLeveler.Level(p);

        Assert.Equal(D(5, 8), critical.Start);
        Assert.Equal(D(7, 8), slack.Start);
    }

    [Fact]
    public void LevelingWithinSlackDoesNotMoveFinish()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Assign(a, r);
        p.Assign(b, r);

        var result = ResourceLeveler.Level(p, new LevelingOptions { LevelWithinAvailableSlack = true });

        Assert.Empty(result.DelayedTasks);
        Assert.Single(result.Unresolved);
        Assert.Equal(D(6, 17), p.Finish);

        p.AddTask("Long", 5 * Day);
        result = ResourceLeveler.Level(p, new LevelingOptions { LevelWithinAvailableSlack = true });
        Assert.Single(result.DelayedTasks);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ClearLevelingRemovesDelays()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Assign(a, r);
        p.Assign(b, r);
        ResourceLeveler.Level(p);

        ResourceLeveler.ClearLeveling(p);

        Assert.Equal(0, b.LevelingDelayMinutes);
        Assert.Equal(D(5, 8), b.Start);
    }

    [Fact]
    public void SingleTaskOverallocationIsUnresolved()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", 2 * Day), r, 2.0);

        var result = ResourceLeveler.Level(p);

        Assert.Single(result.Unresolved);
    }

    [Fact]
    public void RemovingResourceRemovesAssignments()
    {
        var p = NewProject();
        var r = p.AddResource("R");
        var task = p.AddTask("A", Day);
        p.Assign(task, r);
        p.RemoveResource(r);
        Assert.Empty(task.Assignments);
        Assert.Empty(p.Resources);
    }

    [Fact]
    public void InvalidAssignmentsAreRejected()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var child = p.AddTask("Child", Day);
        p.Indent(child);
        var r = p.AddResource("R");
        var budget = p.AddResource("Budget", ResourceType.Cost);
        budget.IsBudget = true;

        Assert.Throws<InvalidOperationException>(() => p.Assign(phase, r));
        Assert.Throws<InvalidOperationException>(() => p.Assign(child, budget));
        p.Assign(child, r);
        Assert.Throws<InvalidOperationException>(() => p.Assign(child, r));
    }
}
