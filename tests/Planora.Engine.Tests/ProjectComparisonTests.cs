using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Reports;

namespace Planora.Engine.Tests;

public class ProjectComparisonTests
{
    [Fact]
    public void ListsAddedRemovedAndChangedTasks()
    {
        var before = new Project("Plan", new DateTime(2026, 1, 5, 8, 0, 0));
        var a = before.AddTask("Design", 480);
        before.AddTask("Old step", 480);
        var c = before.AddTask("Build", 960);
        before.Link(a, c);

        var now = ProjectFile.Deserialize(ProjectFile.Serialize(before));
        now.SetDuration(now.Tasks[0], 3 * 480);
        now.RemoveTask(now.Tasks[1]);
        now.AddTask("New step", 480);
        now.Tasks[1].Name = "Build it";      // renamed: still matched by unique ID

        var report = ProjectComparison.Compare(now, before, "v1");
        var tables = report.Blocks.OfType<TableBlock>().ToList();
        var changed = tables[0];
        Assert.Contains(changed.Rows, r => r[0].EndsWith("Design") && r[1] == "Duration");
        Assert.Contains(changed.Rows, r => r[1] == "Name" && r[2] == "Build" && r[3] == "Build it");
        Assert.Contains(changed.Rows, r => r[0].EndsWith("Build it") && r[1] == "Start");
        Assert.Contains(tables[1].Rows, r => r[0].EndsWith("New step"));
        Assert.Contains(tables[2].Rows, r => r[0] == "Old step");
    }
}
