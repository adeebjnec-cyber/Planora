using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ResourceParityTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    [Fact]
    public void AvailabilityChangesOverTime()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        r.Availability.Add(new AvailabilityPeriod(D(7, 0), D(9, 0), 0.5)); // half-time Wed-Thu
        p.Assign(p.AddTask("A", 5 * Day), r);
        ProjectScheduler.Schedule(p);

        var over = Assert.Single(ResourceAnalysis.FindOverallocations(p, r));
        Assert.Equal((D(7), D(8, 17)), (over.From, over.To));
        Assert.Equal(0.5, r.MaxUnitsAt(D(7, 10)));
        Assert.Equal(1.0, r.MaxUnitsAt(D(9, 10)));
    }

    [Fact]
    public void OvertimeShortensTheTaskAndCostsMore()
    {
        var p = new Project("Test", D(5));
        var task = p.AddTask("Job", 2 * Day);
        var r = p.AddResource("R");
        r.StandardRate = 10;
        r.OvertimeRate = 15;
        var a = p.Assign(task, r);
        a.OvertimeWorkMinutes = 4 * 60;

        ProjectScheduler.Schedule(p);

        Assert.Equal(16 * 60, a.WorkMinutes);
        Assert.Equal(12 * 60, a.RegularWorkMinutes);
        Assert.Equal(D(6, 12), task.Finish); // 12 regular hours: Mon + Tue morning
        Assert.Equal(12 * 10m + 4 * 15m, task.Cost);
        Assert.False(r.IsOverallocated); // overtime is outside working time
    }

    [Fact]
    public void DayByDayBasisIgnoresBriefPeaks()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", 240), r);
        p.Assign(p.AddTask("B", 240), r);
        ProjectScheduler.Schedule(p);

        Assert.NotEmpty(ResourceAnalysis.FindOverallocations(p, r, OverallocationBasis.MinuteByMinute));
        Assert.Empty(ResourceAnalysis.FindOverallocations(p, r, OverallocationBasis.DayByDay));
        Assert.True(r.IsOverallocated); // the indicator still flags the moment-by-moment peak

        var result = ResourceLeveler.Level(p); // levels day by day, as Microsoft Project does by default
        Assert.Empty(result.DelayedTasks);

        result = ResourceLeveler.Level(p, new LevelingOptions { Basis = OverallocationBasis.MinuteByMinute });
        Assert.Single(result.DelayedTasks);
    }

    [Fact]
    public void WeekByWeekBasis()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", 2 * Day), r);
        p.Assign(p.AddTask("B", 2 * Day), r);
        ProjectScheduler.Schedule(p);

        // 32 hours in a 40-hour week is fine week by week, but not day by day.
        Assert.Empty(ResourceAnalysis.FindOverallocations(p, r, OverallocationBasis.WeekByWeek));
        Assert.NotEmpty(ResourceAnalysis.FindOverallocations(p, r, OverallocationBasis.DayByDay));
    }

    [Fact]
    public void LevelingCanSplitRemainingWork()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        var a = p.AddTask("Long", 4 * Day);
        var b = p.AddTask("Urgent", Day);
        b.ConstraintType = ConstraintType.StartNoEarlierThan;
        b.ConstraintDate = D(7);
        b.Priority = 900;
        p.Assign(a, r);
        p.Assign(b, r);

        var result = ResourceLeveler.Level(p, new LevelingOptions { Order = LevelingOrder.PriorityStandard, CanSplitRemainingWork = true });

        Assert.Equal(new[] { a }, result.DelayedTasks);
        Assert.Empty(result.Unresolved);
        Assert.Equal(D(7), b.Start);
        Assert.Equal(new[] { new TaskSegment(D(5), D(6, 17)), new TaskSegment(D(8), D(9, 17)) }, a.Segments);
        Assert.True(a.Splits.Single().FromLeveling);

        ResourceLeveler.ClearLeveling(p);
        Assert.Empty(a.Splits);
    }

    [Fact]
    public void WithoutSplittingTheWholeTaskIsDelayed()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        var a = p.AddTask("Long", 4 * Day);
        var b = p.AddTask("Urgent", Day);
        b.ConstraintType = ConstraintType.StartNoEarlierThan;
        b.ConstraintDate = D(7);
        b.Priority = 900;
        p.Assign(a, r);
        p.Assign(b, r);

        ResourceLeveler.Level(p, new LevelingOptions { Order = LevelingOrder.PriorityStandard });

        Assert.Empty(a.Splits);
        Assert.Equal(D(8), a.Start);
    }

    [Fact]
    public void ClearLevelingKeepsUserSplits()
    {
        var p = new Project("Test", D(5));
        var task = p.AddTask("A", 3 * Day);
        task.Split(Day, Day);
        task.Split(2 * Day, Day, fromLeveling: true);
        ResourceLeveler.ClearLeveling(p);
        Assert.Equal(new[] { new TaskSplit(Day, Day) }, task.Splits);
    }

    [Fact]
    public void AvailabilityOvertimeAndLevelingSplitsAreSaved()
    {
        var p = new Project("Test", D(5));
        var r = p.AddResource("R");
        r.Availability.Add(new AvailabilityPeriod(D(7, 0), null, 0.5));
        var task = p.AddTask("A", 3 * Day);
        task.Split(Day, Day, fromLeveling: true);
        p.Assign(task, r).OvertimeWorkMinutes = 120;

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.Equal(r.Availability, loaded.Resources[0].Availability);
        Assert.Equal(120, loaded.Tasks[0].Assignments[0].OvertimeWorkMinutes);
        Assert.True(loaded.Tasks[0].Splits[0].FromLeveling);

        var viaXml = MspdiFile.Read(MspdiFile.Write(p));
        Assert.Equal(0.5, viaXml.Resources[0].Availability.Single().Units);
        Assert.Null(viaXml.Resources[0].Availability.Single().To);
        Assert.Equal(120, viaXml.Tasks[0].Assignments[0].OvertimeWorkMinutes);
    }
}
