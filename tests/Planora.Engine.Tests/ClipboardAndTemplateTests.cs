using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ClipboardAndTemplateTests
{
    private const int Day = 480;

    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    private static (Project P, ProjectTask Phase, ProjectTask A, ProjectTask B, ProjectTask C) Sample()
    {
        var p = new Project("Source", D(5));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", Day);
        p.Indent(a);
        p.Indent(b);
        var c = p.AddTask("C", Day);
        p.Link(a, b, LinkType.StartToStart, 60);
        p.Link(b, c);
        a.Notes = "note";
        a.Deadline = D(20, 17);
        ResourceNamesText.Apply(p, a, "Alice[50%]");
        Fields.TaskFields.TrySetCustom(p, b, "Text1", "tag");
        ProjectScheduler.Schedule(p);
        return (p, phase, a, b, c);
    }

    [Fact]
    public void CopyAndPasteKeepsStructureLinksAndAssignments()
    {
        var (source, phase, _, _, _) = Sample();
        var data = TaskClipboard.Copy(source, new[] { phase });

        var target = new Project("Target", D(5));
        target.AddTask("Existing", Day);
        var pasted = TaskClipboard.Paste(target, data, 1);

        Assert.Equal(new[] { "Existing", "Phase", "A", "B" }, target.Tasks.Select(t => t.Name));
        Assert.Equal(new[] { 1, 2, 2 }, pasted.Select(t => t.OutlineLevel));
        var a = pasted[1];
        var b = pasted[2];
        Assert.Equal(a.Id, b.Predecessors.Single().PredecessorId);
        Assert.Equal(LinkType.StartToStart, b.Predecessors.Single().Type);
        Assert.Equal("note", a.Notes);
        Assert.Equal(D(20, 17), a.Deadline);
        Assert.Equal("Alice[50%]", ResourceNamesText.Format(a));
        Assert.Equal("tag", b.CustomValues["Text1"]);
        Assert.Single(target.Resources);
    }

    [Fact]
    public void CopyingSubtasksDropsTheirSummaryAndOutsideLinks()
    {
        var (source, _, a, b, c) = Sample();
        var data = TaskClipboard.Copy(source, new[] { b, c });

        var target = new Project("Target", D(5));
        var pasted = TaskClipboard.Paste(target, data, 0);

        Assert.Equal(new[] { "B", "C" }, pasted.Select(t => t.Name));
        Assert.All(pasted, t => Assert.Equal(1, t.OutlineLevel));
        Assert.Empty(pasted[0].Predecessors); // link from A was outside the copy
        Assert.Single(pasted[1].Predecessors);
        Assert.Empty(target.Resources); // only A had a resource
    }

    [Fact]
    public void PasteIntoSameProjectCreatesNewTasks()
    {
        var (p, _, a, _, _) = Sample();
        var data = TaskClipboard.Copy(p, new[] { a });
        var pasted = TaskClipboard.Paste(p, data, p.Tasks.Count);

        Assert.Equal(5, p.Tasks.Count);
        Assert.NotEqual(a.Id, pasted[0].Id);
        Assert.Single(p.Resources); // Alice matched by name
        ProjectScheduler.Schedule(p);
    }

    [Fact]
    public void PasteTextCreatesIndentedTasks()
    {
        var p = new Project("Target", D(5));
        var tasks = TaskClipboard.PasteText(p, "Design\t3d\n  Sketch\t1d\n  Review\nShip\t0d\n", 0);

        Assert.Equal(new[] { 1, 2, 2, 1 }, tasks.Select(t => t.OutlineLevel));
        Assert.Equal(3 * Day, tasks[0].DurationMinutes);
        Assert.True(tasks[2].IsEstimated);
        Assert.Equal(0, tasks[3].DurationMinutes);
    }

    [Fact]
    public void TemplatesDropProgressAndMoveDates()
    {
        var (p, _, a, _, _) = Sample();
        a.PercentComplete = 50;
        a.ActualStart = D(5);
        p.SaveBaseline();
        var path = Path.Combine(Path.GetTempPath(), $"planora-{Guid.NewGuid():N}{ProjectTemplate.Extension}");
        try
        {
            ProjectTemplate.Save(p, path);
            var fresh = ProjectTemplate.Instantiate(path, D(12), "New project");
            ProjectScheduler.Schedule(fresh);

            var copyA = fresh.Tasks.Single(t => t.Name == "A");
            Assert.Equal("New project", fresh.Name);
            Assert.Equal(0, copyA.PercentComplete);
            Assert.Null(copyA.ActualStart);
            Assert.Null(copyA.Baselines[0]);
            Assert.Equal(D(27, 17), copyA.Deadline); // moved by 7 days
            Assert.Equal(D(12), copyA.Start);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
