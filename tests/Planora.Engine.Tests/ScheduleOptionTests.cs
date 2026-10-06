using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ScheduleOptionTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static (Project, ProjectTask, ProjectTask) Chain(ConstraintType type, DateTime date)
    {
        var p = new Project("Test", D(5, 8));
        var a = p.AddTask("A", 3 * Day);
        var b = p.AddTask("B", Day);
        p.Link(a, b);
        b.ConstraintType = type;
        b.ConstraintDate = date;
        return (p, a, b);
    }

    [Fact]
    public void HonouredStartNoLaterThanHoldsTaskAgainstItsLink()
    {
        var (p, a, b) = Chain(ConstraintType.StartNoLaterThan, D(6, 8));
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(6, 8), b.Start);
        Assert.True(a.TotalFloatMinutes < 0);   // the violated link shows on the predecessor
    }

    [Fact]
    public void LinksWinWhenConstraintsAreNotHonoured()
    {
        var (p, _, b) = Chain(ConstraintType.StartNoLaterThan, D(6, 8));
        p.Options.HonorConstraints = false;
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(8, 8), b.Start);
        Assert.True(b.TotalFloatMinutes < 0);
    }

    [Fact]
    public void MustStartOnYieldsToLinksOnlyWhenNotHonoured()
    {
        var (p, _, b) = Chain(ConstraintType.MustStartOn, D(6, 8));
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(6, 8), b.Start);

        p.Options.HonorConstraints = false;
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(8, 8), b.Start);

        b.ConstraintDate = D(12, 8);   // later than the link allows: the constraint still applies
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(12, 8), b.Start);
    }

    [Fact]
    public void FinishNoLaterThanPullsTaskBackWhenHonoured()
    {
        var (p, _, b) = Chain(ConstraintType.FinishNoLaterThan, D(7, 17));
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(7, 17), b.Finish);
    }

    [Fact]
    public void InsertedTaskJoinsChainAndDeletedTaskIsBridged()
    {
        var p = new Project("Test", D(5, 8));
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", Day);
        p.Link(a, b, LinkType.FinishToStart, 60);

        var x = p.InsertTask(1, "X", Day);
        Assert.True(p.AutolinkInserted(x));
        Assert.Contains(x.Predecessors, l => l.PredecessorId == a.Id);
        Assert.Contains(b.Predecessors, l => l.PredecessorId == x.Id && l.LagMinutes == 60);
        Assert.DoesNotContain(b.Predecessors, l => l.PredecessorId == a.Id);

        p.RemoveTaskAutolinked(x);
        Assert.Contains(b.Predecessors, l => l.PredecessorId == a.Id);
    }

    [Fact]
    public void InsertingOutsideAChainLinksNothing()
    {
        var p = new Project("Test", D(5, 8));
        p.AddTask("A", Day);
        p.AddTask("B", Day);
        var x = p.InsertTask(1, "X", Day);
        Assert.False(p.AutolinkInserted(x));
        Assert.Empty(x.Predecessors);
    }
}

public class MoveTaskTests
{
    [Fact]
    public void MovingASummaryCarriesItsSubtasksPastItsSibling()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var a = p.AddTask("A", 480);
        var s = p.AddTask("S");
        var s1 = p.AddTask("S1", 480);
        var s2 = p.AddTask("S2", 480);
        p.Indent(s1);
        p.Indent(s2);
        var b = p.AddTask("B", 480);

        Assert.True(p.MoveTask(s, up: true));
        Assert.Equal(new[] { "S", "S1", "S2", "A", "B" }, p.Tasks.Select(t => t.Name));
        Assert.False(p.MoveTask(s, up: true));

        Assert.True(p.MoveTask(s, up: false));
        Assert.True(p.MoveTask(s, up: false));
        Assert.Equal(new[] { "A", "B", "S", "S1", "S2" }, p.Tasks.Select(t => t.Name));
        Assert.Equal(s, s1.Parent);

        Assert.False(p.MoveTask(s1, up: true));   // stays inside its summary
        Assert.True(p.MoveTask(s1, up: false));
        Assert.Equal(new[] { "A", "B", "S", "S2", "S1" }, p.Tasks.Select(t => t.Name));
        Assert.False(p.MoveTask(s1, up: false));
    }
}
