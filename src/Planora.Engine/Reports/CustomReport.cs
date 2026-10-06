using System.Globalization;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.Engine.Reports;

public enum CustomElementKind
{
    /// <summary>A heading and a paragraph of text.</summary>
    Text,

    /// <summary>Key project figures (start, finish, work, cost, % complete...).</summary>
    ProjectFigures,

    /// <summary>A table of tasks with chosen fields, after a filter.</summary>
    TaskTable,

    /// <summary>A bar chart of one numeric field for top-level tasks or for the filtered tasks.</summary>
    TaskChart,

    /// <summary>A table of resources with their work and cost.</summary>
    ResourceTable,
}

/// <summary>One element of a custom report.</summary>
public sealed class CustomReportElement
{
    public CustomElementKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Filter name applied to the tasks (task tables and charts).</summary>
    public string Filter { get; set; } = "All Tasks";

    /// <summary>Field keys of a task table's columns.</summary>
    public List<string> Fields { get; set; } = new() { "Name", "Start", "Finish", "PercentComplete" };

    /// <summary>The numeric field charted (e.g. Cost, Work, PercentComplete) and an optional second series.</summary>
    public string ChartField { get; set; } = "Cost";
    public string? SecondChartField { get; set; }

    /// <summary>Chart top-level tasks (phases) rather than every filtered task.</summary>
    public bool TopLevelOnly { get; set; } = true;

    /// <summary>Show only the first N rows (0 = all).</summary>
    public int MaxRows { get; set; }

    public CustomReportElement Copy() => new()
    {
        Kind = Kind, Title = Title, Text = Text, Filter = Filter, Fields = Fields.ToList(),
        ChartField = ChartField, SecondChartField = SecondChartField, TopLevelOnly = TopLevelOnly, MaxRows = MaxRows,
    };
}

/// <summary>A report designed by the user (Report > New Report), saved with the project.</summary>
public sealed class CustomReport
{
    public string Name { get; set; } = "Custom Report";
    public List<CustomReportElement> Elements { get; set; } = new();

    public CustomReport Copy() => new() { Name = Name, Elements = Elements.Select(e => e.Copy()).ToList() };

    /// <summary>Builds the report's blocks for a scheduled project.</summary>
    public List<ReportBlock> Build(Project project)
    {
        var blocks = new List<ReportBlock>();
        foreach (var element in Elements)
        {
            if (element.Title.Length > 0 && element.Kind != CustomElementKind.TaskChart)
                blocks.Add(new HeadingBlock(element.Title));
            switch (element.Kind)
            {
                case CustomElementKind.Text:
                    if (element.Text.Length > 0)
                        blocks.Add(new ParagraphBlock(element.Text));
                    break;
                case CustomElementKind.ProjectFigures:
                    blocks.Add(Figures(project));
                    break;
                case CustomElementKind.TaskTable:
                    blocks.Add(TaskTable(project, element));
                    break;
                case CustomElementKind.TaskChart:
                    blocks.Add(TaskChart(project, element));
                    break;
                case CustomElementKind.ResourceTable:
                    blocks.Add(ResourceTable(project));
                    break;
            }
        }
        if (blocks.Count == 0)
            blocks.Add(new ParagraphBlock("This report is empty: use Edit Report to add tables, charts and text."));
        return blocks;
    }

    private static List<ProjectTask> Tasks(Project project, CustomReportElement element)
    {
        var filter = ViewCatalog.Filter(project, element.Filter);
        var tasks = filter.IsAllTasks ? project.Tasks.ToList() : filter.Apply(project, project.Tasks);
        tasks = tasks.Where(t => t.IsActive).ToList();
        return element.MaxRows > 0 ? tasks.Take(element.MaxRows).ToList() : tasks;
    }

    private static FiguresBlock Figures(Project project)
    {
        var leaves = project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        long total = leaves.Sum(t => t.DurationMinutes);
        double percent = total > 0 ? leaves.Sum(t => t.DurationMinutes * t.PercentComplete) / total : 0;
        string Date(DateTime d) => d.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);
        return new FiguresBlock(new[]
        {
            ("Start", Date(project.Start)),
            ("Finish", Date(project.Finish)),
            ("% Complete", percent.ToString("0", CultureInfo.CurrentCulture) + "%"),
            ("Work", DurationText.Format(project.WorkMinutes, project.Options, DurationUnit.Hours)),
            ("Cost", project.Cost.ToString("C", CultureInfo.CurrentCulture)),
            ("Tasks", leaves.Count.ToString(CultureInfo.CurrentCulture)),
        });
    }

    private static TableBlock TaskTable(Project project, CustomReportElement element)
    {
        var fields = element.Fields.Select(k => TaskFields.Find(project, k)).OfType<FieldDefinition>().ToList();
        if (fields.Count == 0)
            fields.Add(TaskFields.Get(project, "Name"));
        var tasks = Tasks(project, element);
        return new TableBlock(
            fields.Select(f => f.Name).ToList(),
            tasks.Select(t => (IReadOnlyList<string>)fields.Select(f => f.Key == "Name" ? new string(' ', (t.OutlineLevel - 1) * 3) + t.Name : f.Format(project, t)).ToList()).ToList(),
            tasks.Select(t => TaskFields.Get(project, "Status").Format(project, t) == "Late").ToList());
    }

    private static ReportBlock TaskChart(Project project, CustomReportElement element)
    {
        var field = TaskFields.Find(project, element.ChartField);
        if (field is null)
            return new ParagraphBlock($"'{element.ChartField}' is not a field.");
        var second = element.SecondChartField is { } key ? TaskFields.Find(project, key) : null;
        var tasks = element.TopLevelOnly ? project.Tasks.Where(t => t.Parent is null && t.IsActive).ToList() : Tasks(project, element).Where(t => !t.IsSummary).ToList();
        if (element.MaxRows > 0)
            tasks = tasks.Take(element.MaxRows).ToList();
        var bars = tasks.Select(t => new ChartBar(t.Name, Number(field.Get(project, t), field.Type), field.Format(project, t),
            second is null ? null : Number(second.Get(project, t), second.Type))).ToList();
        string title = element.Title.Length > 0 ? element.Title : $"{field.Name} by task";
        return new BarChartBlock(title, bars, field.Name, second?.Name, field.Type == FieldType.Percent ? 100 : null);
    }

    /// <summary>A field value as a chart number (work and durations in hours).</summary>
    private static double Number(object? value, FieldType type) => value switch
    {
        double d => d,
        decimal m => (double)m,
        long minutes => minutes / 60.0,
        bool b => b ? 1 : 0,
        _ => 0,
    };

    private static TableBlock ResourceTable(Project project)
    {
        var resources = project.Resources.Where(r => !r.IsBudget).ToList();
        return new TableBlock(
            new[] { "Resource", "Type", "Work", "Cost", "Peak Units" },
            resources.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name, r.Type.ToString(), r.Type == ResourceType.Work ? DurationText.Format(r.WorkMinutes, project.Options, DurationUnit.Hours) : "",
                r.Cost.ToString("C", CultureInfo.CurrentCulture), r.Type == ResourceType.Work ? (r.PeakUnits * 100).ToString("0", CultureInfo.CurrentCulture) + "%" : "",
            }).ToList(),
            resources.Select(r => r.IsOverallocated).ToList());
    }
}
