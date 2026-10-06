using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TaskPathTests
{
    private const int Day = 480;

    [Fact]
    public void FindsAllAndDrivingPredecessorsAndSuccessors()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var a = p.AddTask("A", 3 * Day);      // drives D
        var b = p.AddTask("B", Day);          // also a predecessor of D, but finishes earlier
        var c = p.AddTask("C", Day);          // predecessor of A
        var d = p.AddTask("D", Day);
        var e = p.AddTask("E", Day);          // driven by D
        var f = p.AddTask("F", Day);          // successor of D, but held later by a constraint
        p.Link(c, a);
        p.Link(a, d);
        p.Link(b, d);
        p.Link(d, e);
        p.Link(d, f);
        f.ConstraintType = ConstraintType.StartNoEarlierThan;
        f.ConstraintDate = new DateTime(2026, 2, 2, 8, 0, 0);
        ProjectScheduler.Schedule(p);

        var all = TaskPath.Find(p, new[] { d }, TaskPathKind.Predecessors | TaskPathKind.Successors);
        Assert.Equal(new[] { a, b, c, e, f }.ToHashSet(), all.Keys.ToHashSet());

        var driving = TaskPath.Find(p, new[] { d }, TaskPathKind.DrivingPredecessors);
        Assert.Equal(new[] { a, c }.ToHashSet(), driving.Keys.ToHashSet());

        var driven = TaskPath.Find(p, new[] { d }, TaskPathKind.DrivenSuccessors);
        Assert.Equal(new[] { e }, driven.Keys);
    }
}
