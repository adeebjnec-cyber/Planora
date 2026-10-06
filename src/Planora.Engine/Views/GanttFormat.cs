using System.Globalization;
using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

/// <summary>Which tasks a bar style draws (Microsoft Project's "Show For ... Tasks").</summary>
public enum BarKind
{
    Task,
    CriticalTask,
    ManualTask,
    InactiveTask,
    Summary,
    Milestone,
    Progress,
    Baseline,
}

public enum BarShape
{
    /// <summary>A full-height bar.</summary>
    Bar,

    /// <summary>A thin bar through the middle of the row.</summary>
    Thin,

    /// <summary>Not drawn (its texts still are).</summary>
    Hidden,
}

/// <summary>
/// One bar style: how bars of a kind are drawn and which fields are written around them. A style with a
/// <see cref="FlagField"/> applies only to tasks whose flag field is Yes, and overrides the kind's own style.
/// </summary>
public sealed class BarStyle
{
    public string Name { get; set; } = "";
    public BarKind Kind { get; set; }

    /// <summary>A Yes/No field (Marked, Flag1-20, Critical, ...) the task must have set, or null for every task of the kind.</summary>
    public string? FlagField { get; set; }

    /// <summary>Colours as #RRGGBB.</summary>
    public string Fill { get; set; } = "#8AB4E6";
    public string Border { get; set; } = "#3E6FB0";
    public BarShape Shape { get; set; } = BarShape.Bar;

    /// <summary>Field keys of the texts drawn left of, right of, above and inside the bar (null = none).</summary>
    public string? LeftText { get; set; }
    public string? RightText { get; set; }
    public string? TopText { get; set; }
    public string? InsideText { get; set; }

    public BarStyle Copy() => (BarStyle)MemberwiseClone();
}

/// <summary>Rows (and bar texts) whose look a text style changes, as in Microsoft Project's Text Styles.</summary>
public enum TextItem
{
    BarText,
    CriticalTasks,
    SummaryTasks,
    MilestoneTasks,
    MarkedTasks,
    ExternalTasks,
}

public sealed class TextStyle
{
    public TextItem Item { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>#RRGGBB, or null to keep the normal colour.</summary>
    public string? Color { get; set; }

    /// <summary>Font size in points, or null to keep the normal size.</summary>
    public double? Size { get; set; }

    public TextStyle Copy() => (TextStyle)MemberwiseClone();
}

public enum TimescaleUnit
{
    Hours,
    Days,
    Weeks,
    ThirdsOfMonths,
    Months,
    Quarters,
    HalfYears,
    Years,
}

/// <summary>One timescale tier: its unit, how many units per label and the label's date format.</summary>
public sealed class TimescaleTierSetting
{
    public TimescaleUnit Unit { get; set; } = TimescaleUnit.Days;
    public int Count { get; set; } = 1;

    /// <summary>A .NET date format; "{Q}" stands for the quarter number and "{H}" for the half-year. Quote literal letters, e.g. 'Q'{Q} yyyy.</summary>
    public string Label { get; set; } = "%d";

    public TimescaleTierSetting Copy() => (TimescaleTierSetting)MemberwiseClone();

    /// <summary>Formats one tier label for a period starting at <paramref name="date"/>.</summary>
    public string Format(DateTime date)
    {
        string format = Label;
        string quarter = ((date.Month - 1) / 3 + 1).ToString(CultureInfo.CurrentCulture);
        string half = ((date.Month - 1) / 6 + 1).ToString(CultureInfo.CurrentCulture);
        // Protect the placeholders from the date formatter, then fill them in.
        format = format.Replace("{Q}", "'\u0001'").Replace("{H}", "'\u0002'");
        string text;
        try
        {
            text = date.ToString(format, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            text = Label;
        }
        return text.Replace("\u0001", quarter).Replace("\u0002", half);
    }
}

/// <summary>The timescale: automatic (follows the zoom) or one to three fixed tiers.</summary>
public sealed class TimescaleSettings
{
    public bool Automatic { get; set; } = true;

    /// <summary>Tiers from top to bottom; 1 to 3 are used.</summary>
    public List<TimescaleTierSetting> Tiers { get; set; } = new()
    {
        new() { Unit = TimescaleUnit.Months, Label = "MMMM yyyy" },
        new() { Unit = TimescaleUnit.Weeks, Label = "d MMM" },
        new() { Unit = TimescaleUnit.Days, Label = "%d" },
    };

    /// <summary>How many tiers are shown (1-3) when not automatic.</summary>
    public int TierCount { get; set; } = 2;

    /// <summary>Shade non-working time in the chart.</summary>
    public bool ShadeNonWorkingTime { get; set; } = true;

    public TimescaleSettings Copy() => new()
    {
        Automatic = Automatic,
        Tiers = Tiers.Select(t => t.Copy()).ToList(),
        TierCount = TierCount,
        ShadeNonWorkingTime = ShadeNonWorkingTime,
    };
}

/// <summary>Chart gridlines and date lines (Microsoft Project's Gridlines dialog).</summary>
public sealed class GridlineSettings
{
    public bool RowLines { get; set; }
    public bool TopTierColumns { get; set; }
    public bool BottomTierColumns { get; set; }
    public bool CurrentDate { get; set; } = true;
    public bool StatusDate { get; set; } = true;
    public bool ProjectStart { get; set; }
    public bool ProjectFinish { get; set; }

    /// <summary>#RRGGBB colour of the row and column lines.</summary>
    public string Color { get; set; } = "#D8D8D8";

    public GridlineSettings Copy() => (GridlineSettings)MemberwiseClone();
}

/// <summary>
/// Formatting of the Gantt Chart, saved with the project: bar styles, text styles, timescale and gridlines.
/// </summary>
public sealed class GanttFormat
{
    public List<BarStyle> BarStyles { get; set; } = DefaultBarStyles();
    public List<TextStyle> TextStyles { get; set; } = DefaultTextStyles();
    public TimescaleSettings Timescale { get; set; } = new();
    public GridlineSettings Gridlines { get; set; } = new();

    public GanttFormat Copy() => new()
    {
        BarStyles = BarStyles.Select(b => b.Copy()).ToList(),
        TextStyles = TextStyles.Select(t => t.Copy()).ToList(),
        Timescale = Timescale.Copy(),
        Gridlines = Gridlines.Copy(),
    };

    /// <summary>Microsoft Project-like default styles.</summary>
    public static List<BarStyle> DefaultBarStyles() => new()
    {
        new() { Name = "Task", Kind = BarKind.Task, Fill = "#8AB4E6", Border = "#3E6FB0", RightText = "ResourceNames" },
        new() { Name = "Critical", Kind = BarKind.CriticalTask, Fill = "#F08E82", Border = "#C04030", RightText = "ResourceNames" },
        new() { Name = "Manual Task", Kind = BarKind.ManualTask, Fill = "#B9E3EA", Border = "#3A9AAE", RightText = "ResourceNames" },
        new() { Name = "Inactive Task", Kind = BarKind.InactiveTask, Fill = "#FFFFFF", Border = "#9A9A9A" },
        new() { Name = "Summary", Kind = BarKind.Summary, Fill = "#202020", Border = "#202020", Shape = BarShape.Thin },
        new() { Name = "Milestone", Kind = BarKind.Milestone, Fill = "#202020", Border = "#202020", RightText = "Start" },
        new() { Name = "Progress", Kind = BarKind.Progress, Fill = "#1F3A60", Border = "#1F3A60", Shape = BarShape.Thin },
        new() { Name = "Baseline", Kind = BarKind.Baseline, Fill = "#A6A6A6", Border = "#A6A6A6", Shape = BarShape.Thin },
    };

    public static List<TextStyle> DefaultTextStyles() => new()
    {
        new() { Item = TextItem.BarText, Size = 8.5 },
        new() { Item = TextItem.CriticalTasks },
        new() { Item = TextItem.SummaryTasks, Bold = true },
        new() { Item = TextItem.MilestoneTasks },
        new() { Item = TextItem.MarkedTasks },
        new() { Item = TextItem.ExternalTasks, Color = "#808080" },
    };

    /// <summary>
    /// The style a task's bar is drawn with: the kind's own style, overridden by the last flag-field style
    /// whose flag the task has set.
    /// </summary>
    public BarStyle StyleFor(Project project, ProjectTask task, BarKind kind)
    {
        var style = BarStyles.LastOrDefault(s => s.Kind == kind && s.FlagField is null) ?? DefaultBarStyles().First(s => s.Kind == kind);
        if (kind is BarKind.Task or BarKind.CriticalTask or BarKind.ManualTask or BarKind.Milestone or BarKind.Summary)
        {
            foreach (var flagged in BarStyles.Where(s => s.FlagField is not null && (s.Kind == kind || s.Kind == BarKind.Task && kind is BarKind.CriticalTask or BarKind.ManualTask)))
            {
                if (TaskFields.Find(project, flagged.FlagField!) is { } field && field.Get(project, task) is true)
                    style = flagged;
            }
        }
        return style;
    }

    /// <summary>The style of an item, or a plain one.</summary>
    public TextStyle Text(TextItem item) => TextStyles.FirstOrDefault(t => t.Item == item) ?? new TextStyle { Item = item };

    /// <summary>
    /// The row text style for a task: the most specific matching item (marked, then milestone, critical,
    /// summary), or null for the normal look.
    /// </summary>
    public TextStyle? RowStyleFor(ProjectTask task, bool showCritical)
    {
        var items = new List<TextItem>();
        if (task.IsSummary)
            items.Add(TextItem.SummaryTasks);
        if (showCritical && task.IsCritical)
            items.Add(TextItem.CriticalTasks);
        if (!task.IsSummary && (task.IsMilestone || task.DurationMinutes == 0))
            items.Add(TextItem.MilestoneTasks);
        if (task.IsMarked)
            items.Add(TextItem.MarkedTasks);
        if (task.SubprojectPath is not null)
            items.Add(TextItem.ExternalTasks);
        TextStyle? result = null;
        foreach (var item in items)
        {
            var style = Text(item);
            if (style.Bold || style.Italic || style.Color is not null || style.Size is not null)
                result = style;
        }
        return result;
    }
}
