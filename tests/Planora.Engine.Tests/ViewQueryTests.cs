using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class ViewQueryTests
{
    private const int Day = 480;

    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    /// <summary>Phase(A 3d critical -> B 1d), C 2d independent, M milestone after B.</summary>
    private static (Project P, ProjectTask Phase, ProjectTask A, ProjectTask B, ProjectTask C, ProjectTask M) Sample()
    {
        var p = new Project("Test", D(5));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("Alpha", 3 * Day);
        var b = p.AddTask("Bravo", Day);
        p.Indent(a);
        p.Indent(b);
        var c = p.AddTask("Charlie", 2 * Day);
        var m = p.AddTask("Done", 0);
        m.IsMilestone = true;
        p.Link(a, b);
        p.Link(b, m);
        a.PercentComplete = 100;
        a.ActualStart = D(5);
        b.Priority = 800;
        ProjectScheduler.Schedule(p);
        return (p, phase, a, b, c, m);
    }

    [Fact]
    public void FieldsFormatValues()
    {
        var (p, _, a, _, _, m) = Sample();
        Assert.Equal("3 days", TaskFields.Get(p, "Duration").Format(p, a));
        Assert.Equal("100%", TaskFields.Get(p, "PercentComplete").Format(p, a));
        Assert.Equal("Yes", TaskFields.Get(p, "Milestone").Format(p, m));
        Assert.Equal("Complete", TaskFields.Get(p, "Status").Format(p, a));
        Assert.Equal("NA", TaskFields.Get(p, "BaselineStart").Format(p, a));
        Assert.Equal("2", TaskFields.Get(p, "ID").Format(p, a));
        Assert.Equal("3", TaskFields.Get(p, "Successors").Format(p, a));
    }

    [Fact]
    public void CustomFieldsUseAliasesAndPickLists()
    {
        var (p, _, a, _, _, _) = Sample();
        p.CustomFields.Add(new CustomField("Text1", "Department") { ValueList = { "IT", "HR" } });

        Assert.Equal("Department", TaskFields.Get(p, "Text1").Name);
        Assert.True(TaskFields.TrySetCustom(p, a, "Text1", "it"));
        Assert.False(TaskFields.TrySetCustom(p, a, "Text1", "Sales"));
        Assert.True(TaskFields.TrySetCustom(p, a, "Number2", "42.5"));
        Assert.False(TaskFields.TrySetCustom(p, a, "Number2", "lots"));
        Assert.True(TaskFields.TrySetCustom(p, a, "Flag3", "yes"));
        Assert.Equal(42.5, TaskFields.Get(p, "Number2").Get(p, a));
        Assert.Equal(true, a.CustomValues["Flag3"]);

        Assert.True(TaskFields.TrySetCustom(p, a, "Number2", ""));
        Assert.False(a.CustomValues.ContainsKey("Number2"));
        Assert.Throws<ArgumentException>(() => new CustomField("Text31"));
    }

    [Fact]
    public void BuiltinFilters()
    {
        var (p, phase, a, b, c, m) = Sample();
        List<ProjectTask> Run(string name) => ViewCatalog.Filter(p, name).Apply(p, p.Tasks);

        Assert.Equal(new[] { a }, Run("Completed Tasks"));
        Assert.Equal(new[] { m }, Run("Milestones"));
        Assert.Equal(new[] { phase }, Run("Summary Tasks"));
        Assert.Equal(p.Tasks, Run("All Tasks"));
        // Critical keeps the summary above critical tasks; Alpha is complete, so not critical.
        Assert.Equal(new[] { phase, b, m }, Run("Critical"));
    }

    [Fact]
    public void CustomFilterWithAndOr()
    {
        var (p, _, a, b, c, _) = Sample();
        var filter = new FilterDefinition("Long or important",
            new FilterCondition("Duration", FilterOperator.GreaterThanOrEqual, "2d"),
            new FilterCondition("Summary", FilterOperator.Equals, "No"),
            new FilterCondition("Priority", FilterOperator.GreaterThan, "700", Or: true));

        Assert.Equal(new[] { a, b, c }, filter.Apply(p, p.Tasks));
    }

    [Fact]
    public void TextAndDateFilters()
    {
        var (p, _, a, _, c, _) = Sample();
        var contains = new FilterDefinition("x", new FilterCondition("Name", FilterOperator.Contains, "ar"));
        Assert.Equal(new[] { c }, contains.Apply(p, p.Tasks));

        var byDate = new FilterDefinition("x", new FilterCondition("Start", FilterOperator.Equals, new DateTime(2026, 1, 5).ToString("d")));
        Assert.Contains(a, byDate.Apply(p, p.Tasks));
        Assert.Contains(c, byDate.Apply(p, p.Tasks));
    }

    [Fact]
    public void SortKeepingOutline()
    {
        var (p, phase, a, b, c, m) = Sample();
        var sort = new SortDefinition { Keys = { new SortKey("Duration", Descending: true) } };

        Assert.Equal(new[] { phase, a, b, c, m }, sort.Apply(p, p.Tasks));

        sort.KeepOutlineStructure = false;
        Assert.Equal(new[] { phase, a, c, b, m }, sort.Apply(p, p.Tasks));
    }

    [Fact]
    public void SortByNameAscending()
    {
        var (p, _, a, b, c, m) = Sample();
        var sort = new SortDefinition { Keys = { new SortKey("Name") }, KeepOutlineStructure = false };
        Assert.Equal(new[] { a, b, c, m }, sort.Apply(p, p.Tasks.Where(t => !t.IsSummary)));
    }

    [Fact]
    public void GroupByFlagWithRollups()
    {
        var (p, _, a, b, c, m) = Sample();
        var groups = ViewCatalog.Group(p, "Milestones").Apply(p, p.Tasks);

        Assert.Equal(new[] { "Milestone: Yes", "Milestone: No" }, groups.Select(g => g.Label));
        Assert.Equal(new[] { m }, groups[0].Tasks);
        Assert.Equal(new[] { a, b, c }, groups[1].Tasks);
        Assert.Equal(D(5), groups[1].Start);
    }

    [Fact]
    public void GroupInIntervalsAndNested()
    {
        var (p, _, a, b, c, m) = Sample();
        var group = new GroupDefinition("Bands", new GroupLevel("Duration", Interval: 2 * Day), new GroupLevel("Critical"));
        var groups = group.Apply(p, p.Tasks);

        // 0-2d: Bravo (1d), Done (0d); 2-4d: Alpha (3d), Charlie (2d).
        Assert.Equal(new[] { "Duration: 0 days - 2 days", "Duration: 2 days - 4 days" }, groups.Select(g => g.Label));
        Assert.All(groups, g => Assert.NotEmpty(g.Subgroups));
        Assert.Equal(4, groups.Sum(g => g.AllTasks.Count()));
    }

    [Fact]
    public void GroupByTextField()
    {
        var (p, _, a, b, c, _) = Sample();
        TaskFields.TrySetCustom(p, a, "Text1", "IT");
        TaskFields.TrySetCustom(p, c, "Text1", "it");
        var groups = new GroupDefinition("Dept", new GroupLevel("Text1")).Apply(p, p.Tasks);

        Assert.Equal(2, groups.Count);
        Assert.Equal("No Text1", groups[0].Label);
        Assert.Equal(new[] { a, c }, groups[1].Tasks);
    }

    [Fact]
    public void ProjectDefinitionsOverrideBuiltins()
    {
        var (p, _, _, _, _, _) = Sample();
        p.Tables.Add(new TableDefinition("Entry", new TableColumn("Name"), new TableColumn("Text1")));
        Assert.Equal(2, ViewCatalog.Table(p, "Entry").Columns.Count);
        Assert.Equal(TableDefinition.Builtin.Count, ViewCatalog.Tables(p).Count);
        Assert.Equal("All Tasks", ViewCatalog.Filter(p, "Missing").Name);
    }

    [Fact]
    public void DefinitionsAndCustomValuesRoundTrip()
    {
        var (p, _, a, _, _, _) = Sample();
        p.CustomFields.Add(new CustomField("Cost1", "Budget") { ValueList = { } });
        TaskFields.TrySetCustom(p, a, "Cost1", "123.45");
        TaskFields.TrySetCustom(p, a, "Date2", new DateTime(2026, 3, 1).ToString("d"));
        TaskFields.TrySetCustom(p, a, "Flag1", "yes");
        p.Tables.Add(new TableDefinition("Mine", new TableColumn("Name", 200), new TableColumn("Cost1", 80, "Budget")));
        p.Filters.Add(new FilterDefinition("Big", new FilterCondition("Cost1", FilterOperator.GreaterThan, "100")) { ShowRelatedSummaryRows = true });
        p.Groups.Add(new GroupDefinition("ByFlag", new GroupLevel("Flag1", Descending: true)));
        p.SavedViews.Add(new SavedView { Name = "My view", Table = "Mine", Filter = "Big", Sort = new SortDefinition { Keys = { new SortKey("Cost1", true) } } });

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        var la = loaded.Tasks.Single(t => t.Name == "Alpha");

        Assert.Equal(123.45m, la.CustomValues["Cost1"]);
        Assert.Equal(new DateTime(2026, 3, 1), la.CustomValues["Date2"]);
        Assert.Equal(true, la.CustomValues["Flag1"]);
        Assert.Equal("Budget", loaded.CustomFields.Single().Alias);
        Assert.Equal("Budget", loaded.Tables.Single().Columns[1].Title);
        Assert.True(loaded.Filters.Single().ShowRelatedSummaryRows);
        Assert.True(loaded.Groups.Single().Levels[0].Descending);
        Assert.Equal("Cost1", loaded.SavedViews.Single().Sort.Keys[0].FieldKey);
        ProjectScheduler.Schedule(loaded);
        Assert.Equal(new[] { la.Parent, la }, ViewCatalog.Filter(loaded, "Big").Apply(loaded, loaded.Tasks));
    }
}
