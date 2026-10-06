using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class TaskSearchTests
{
    private static Project Sample()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        p.AddTask("Design screens", 480);
        p.AddTask("Build backend", 960);
        p.AddTask("Design API", 480);
        p.AddTask("Test", 480);
        return p;
    }

    [Fact]
    public void FindNextWrapsAroundAndSearchesBackwards()
    {
        var p = Sample();
        var test = new FilterCondition("Name", FilterOperator.Contains, "design");
        var first = TaskSearch.FindNext(p, p.Tasks, null, test);
        Assert.Equal("Design screens", first!.Name);
        var second = TaskSearch.FindNext(p, p.Tasks, first, test);
        Assert.Equal("Design API", second!.Name);
        Assert.Equal(first, TaskSearch.FindNext(p, p.Tasks, second, test));      // wraps
        Assert.Equal(second, TaskSearch.FindNext(p, p.Tasks, first, test, forward: false));
        Assert.Null(TaskSearch.FindNext(p, p.Tasks, null, test, matchCase: true));
    }

    [Fact]
    public void FindByOtherFieldTypes()
    {
        var p = Sample();
        var found = TaskSearch.FindNext(p, p.Tasks, null, new FilterCondition("Duration", FilterOperator.GreaterThan, "1d"));
        Assert.Equal("Build backend", found!.Name);
    }

    [Fact]
    public void ReplaceAllInNamesAndCustomText()
    {
        var p = Sample();
        p.Tasks[0].CustomValues["Text1"] = "Design phase";
        Assert.Equal(2, TaskSearch.ReplaceAll(p, p.Tasks, "Name", "design", "Plan"));
        Assert.Equal(new[] { "Plan screens", "Build backend", "Plan API", "Test" }, p.Tasks.Select(t => t.Name));
        Assert.Equal(1, TaskSearch.Replace(p, p.Tasks[0], "Text1", "phase", "$1 stage"));
        Assert.Equal("Design $1 stage", p.Tasks[0].CustomValues["Text1"]);
        Assert.Equal(0, TaskSearch.Replace(p, p.Tasks[0], "Duration", "1", "2"));
    }
}
