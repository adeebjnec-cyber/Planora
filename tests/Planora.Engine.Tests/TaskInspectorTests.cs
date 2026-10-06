using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TaskInspectorTests
{
    private const int Day = 480;

    [Fact]
    public void ExplainsWhatDrivesTheStartAndListsProblems()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var a = p.AddTask("Design", 3 * Day);
        var b = p.AddTask("Build", 2 * Day);
        var c = p.AddTask("Fixed", Day);
        var d = p.AddTask("Free", Day);
        p.Link(a, b);
        c.ConstraintType = ConstraintType.StartNoEarlierThan;
        c.ConstraintDate = new DateTime(2026, 1, 12, 8, 0, 0);
        b.Deadline = new DateTime(2026, 1, 7, 17, 0, 0);
        ProjectScheduler.Schedule(p);

        var build = TaskInspector.Inspect(p, b);
        Assert.Contains(build, i => i.Heading == "Predecessor" && i.Text.Contains("Design"));
        Assert.Contains(build, i => i.Heading == "Missed deadline" && i.IsWarning);

        var fixedTask = TaskInspector.Inspect(p, c);
        Assert.Contains(fixedTask, i => i.Heading == "Constraint" && i.Text.Contains("sets the task's date"));

        var free = TaskInspector.Inspect(p, d);
        Assert.Contains(free, i => i.Heading == "Project start");
        Assert.DoesNotContain(free, i => i.IsWarning);
    }
}
