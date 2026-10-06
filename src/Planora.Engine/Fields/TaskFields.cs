using System.Globalization;
using Planora.Engine.Model;

namespace Planora.Engine.Fields;

/// <summary>A task field that can be shown as a column, filtered, sorted and grouped on.</summary>
public sealed class FieldDefinition
{
    public FieldDefinition(string key, string name, FieldType type, Func<Project, ProjectTask, object?> get)
    {
        Key = key;
        Name = name;
        Type = type;
        Get = get;
    }

    /// <summary>Stable identifier used in saved tables, filters and groups.</summary>
    public string Key { get; }

    /// <summary>Column title.</summary>
    public string Name { get; }

    public FieldType Type { get; }
    public Func<Project, ProjectTask, object?> Get { get; }

    /// <summary>Custom fields can be edited generically; built-in fields are edited through dedicated UI.</summary>
    public bool IsCustom { get; init; }

    /// <summary>Calculated by a formula, so read-only.</summary>
    public bool IsCalculated { get; init; }

    public string Format(Project project, ProjectTask task) => FieldValue.Format(Get(project, task), Type, project.Options);

    public override string ToString() => Name;
}

/// <summary>The catalogue of task fields, built-in and custom, as in Microsoft Project's field list.</summary>
public static class TaskFields
{
    private static readonly List<FieldDefinition> Builtin = new()
    {
        F("ID", "ID", FieldType.Number, (p, t) => (double)(p.IndexOf(t) + 1)),
        F("UniqueID", "Unique ID", FieldType.Number, (_, t) => (double)t.Id),
        F("Name", "Task Name", FieldType.Text, (_, t) => t.Name),
        F("WBS", "WBS", FieldType.Text, (_, t) => t.WbsCode),
        F("OutlineLevel", "Outline Level", FieldType.Number, (_, t) => (double)t.OutlineLevel),
        F("Mode", "Task Mode", FieldType.Text, (_, t) => t.Mode == TaskMode.AutoScheduled ? "Auto Scheduled" : "Manually Scheduled"),
        F("Duration", "Duration", FieldType.Duration, (_, t) => t.DurationMinutes),
        F("Start", "Start", FieldType.Date, (_, t) => t.Start),
        F("Finish", "Finish", FieldType.Date, (_, t) => t.Finish),
        F("Predecessors", "Predecessors", FieldType.Text, (p, t) => PredecessorText.Format(p, t)),
        F("Successors", "Successors", FieldType.Text, Successors),
        F("ResourceNames", "Resource Names", FieldType.Text, (_, t) => ResourceNamesText.Format(t)),
        F("PercentComplete", "% Complete", FieldType.Percent, (_, t) => t.PercentComplete),
        F("PhysicalPercentComplete", "Physical % Complete", FieldType.Percent, (_, t) => t.PhysicalPercentComplete),
        F("EarnedValueMethod", "Earned Value Method", FieldType.Text, (_, t) => t.EarnedValueMethod == EarnedValueMethod.PhysicalPercentComplete ? "Physical % Complete" : "% Complete"),
        F("Status", "Status", FieldType.Text, Status),
        F("Critical", "Critical", FieldType.Flag, (_, t) => t.IsCritical),
        F("Milestone", "Milestone", FieldType.Flag, (_, t) => t.IsMilestone || !t.IsSummary && t.DurationMinutes == 0),
        F("Summary", "Summary", FieldType.Flag, (_, t) => t.IsSummary),
        F("Active", "Active", FieldType.Flag, (_, t) => t.IsActive),
        F("Estimated", "Estimated", FieldType.Flag, (_, t) => t.IsEstimated),
        F("Recurring", "Recurring", FieldType.Flag, (_, t) => t.IsRecurring),
        F("EarlyStart", "Early Start", FieldType.Date, (_, t) => t.EarlyStart),
        F("EarlyFinish", "Early Finish", FieldType.Date, (_, t) => t.EarlyFinish),
        F("LateStart", "Late Start", FieldType.Date, (_, t) => t.LateStart),
        F("LateFinish", "Late Finish", FieldType.Date, (_, t) => t.LateFinish),
        F("TotalSlack", "Total Slack", FieldType.Duration, (_, t) => t.TotalFloatMinutes),
        F("FreeSlack", "Free Slack", FieldType.Duration, (_, t) => t.FreeFloatMinutes),
        F("ConstraintType", "Constraint Type", FieldType.Text, (_, t) => ConstraintName(t.ConstraintType)),
        F("ConstraintDate", "Constraint Date", FieldType.Date, (_, t) => t.ConstraintDate),
        F("Deadline", "Deadline", FieldType.Date, (_, t) => t.Deadline),
        F("DeadlineMissed", "Deadline Missed", FieldType.Flag, (_, t) => t.MissesDeadline),
        F("Priority", "Priority", FieldType.Number, (_, t) => (double)t.Priority),
        F("Type", "Type", FieldType.Text, (_, t) => t.Type switch { TaskType.FixedDuration => "Fixed Duration", TaskType.FixedWork => "Fixed Work", _ => "Fixed Units" }),
        F("EffortDriven", "Effort Driven", FieldType.Flag, (_, t) => t.IsEffortDriven),
        F("Calendar", "Task Calendar", FieldType.Text, (_, t) => t.Calendar?.Name ?? ""),
        F("LevelingDelay", "Leveling Delay", FieldType.Duration, (_, t) => t.LevelingDelayMinutes),
        F("Notes", "Notes", FieldType.Text, (_, t) => t.Notes),
        F("Hyperlink", "Hyperlink", FieldType.Text, (_, t) => t.Hyperlink),
        F("HyperlinkAddress", "Hyperlink Address", FieldType.Text, (_, t) => t.HyperlinkAddress),
        F("HyperlinkSubAddress", "Hyperlink SubAddress", FieldType.Text, (_, t) => t.HyperlinkSubAddress),
        F("Marked", "Marked", FieldType.Flag, (_, t) => t.IsMarked),
        F("HideBar", "Hide Bar", FieldType.Flag, (_, t) => t.HideBar),
        F("ActualStart", "Actual Start", FieldType.Date, (_, t) => t.ActualStart),
        F("ActualFinish", "Actual Finish", FieldType.Date, (_, t) => t.ActualFinish),
        F("ActualDuration", "Actual Duration", FieldType.Duration, (_, t) => t.ActualDurationMinutes),
        F("RemainingDuration", "Remaining Duration", FieldType.Duration, (_, t) => t.RemainingDurationMinutes),
        F("Work", "Work", FieldType.Work, (_, t) => t.WorkMinutes),
        F("ActualWork", "Actual Work", FieldType.Work, (_, t) => t.ActualWorkMinutes),
        F("OvertimeWork", "Overtime Work", FieldType.Work, (p, t) => p.Subtree(t).SelectMany(x => x.Assignments).Sum(a => a.OvertimeWorkMinutes)),
        F("RegularWork", "Regular Work", FieldType.Work, (p, t) => t.WorkMinutes - p.Subtree(t).SelectMany(x => x.Assignments).Sum(a => a.OvertimeWorkMinutes)),
        F("RemainingWork", "Remaining Work", FieldType.Work, (_, t) => t.RemainingWorkMinutes),
        F("Cost", "Cost", FieldType.Cost, (_, t) => t.Cost),
        F("FixedCost", "Fixed Cost", FieldType.Cost, (_, t) => t.FixedCost),
        F("FixedCostAccrual", "Fixed Cost Accrual", FieldType.Text, (_, t) => t.FixedCostAccrual.ToString()),
        F("ActualCost", "Actual Cost", FieldType.Cost, (_, t) => t.ActualCost),
        F("RemainingCost", "Remaining Cost", FieldType.Cost, (_, t) => t.RemainingCost),
        F("BaselineStart", "Baseline Start", FieldType.Date, (_, t) => t.Baselines[0]?.Start),
        F("BaselineFinish", "Baseline Finish", FieldType.Date, (_, t) => t.Baselines[0]?.Finish),
        F("BaselineDuration", "Baseline Duration", FieldType.Duration, (_, t) => t.Baselines[0]?.DurationMinutes),
        F("BaselineWork", "Baseline Work", FieldType.Work, (_, t) => t.Baselines[0]?.WorkMinutes),
        F("BaselineCost", "Baseline Cost", FieldType.Cost, (_, t) => t.Baselines[0]?.Cost),
        F("StartVariance", "Start Variance", FieldType.Duration, (_, t) => t.StartVarianceMinutes),
        F("FinishVariance", "Finish Variance", FieldType.Duration, (_, t) => t.FinishVarianceMinutes),
        F("DurationVariance", "Duration Variance", FieldType.Duration, (_, t) => t.DurationVarianceMinutes),
        F("WorkVariance", "Work Variance", FieldType.Work, (_, t) => t.WorkVarianceMinutes),
        F("CostVariance", "Cost Variance", FieldType.Cost, (_, t) => t.CostVariance),
        F("BCWS", "BCWS", FieldType.Cost, (_, t) => t.EarnedValue.Bcws),
        F("BCWP", "BCWP", FieldType.Cost, (_, t) => t.EarnedValue.Bcwp),
        F("ACWP", "ACWP", FieldType.Cost, (_, t) => t.EarnedValue.Acwp),
        F("SV", "SV", FieldType.Cost, (_, t) => t.EarnedValue.Sv),
        F("CV", "CV", FieldType.Cost, (_, t) => t.EarnedValue.Cv),
        F("BAC", "BAC", FieldType.Cost, (_, t) => t.EarnedValue.Bac),
        F("EAC", "EAC", FieldType.Cost, (_, t) => t.EarnedValue.Eac),
        F("VAC", "VAC", FieldType.Cost, (_, t) => t.EarnedValue.Vac),
        F("SPI", "SPI", FieldType.Number, (_, t) => t.EarnedValue.Spi is { } spi ? (double)spi : null),
        F("CPI", "CPI", FieldType.Number, (_, t) => t.EarnedValue.Cpi is { } cpi ? (double)cpi : null),
    };

    /// <summary>Built-in fields followed by every custom field slot (named by alias where set).</summary>
    public static IReadOnlyList<FieldDefinition> All(Project project) =>
        Builtin.Concat(CustomField.AllSlots().Select(key => Custom(project, key))).ToList();

    public static FieldDefinition? Find(Project project, string key)
    {
        var builtin = Builtin.FirstOrDefault(f => f.Key == key);
        if (builtin is not null)
            return builtin;
        return CustomField.TypeOfKey(key) is not null ? Custom(project, key) : null;
    }

    public static FieldDefinition Get(Project project, string key) =>
        Find(project, key) ?? throw new KeyNotFoundException($"Unknown field '{key}'.");

    private static FieldDefinition Custom(Project project, string key)
    {
        var definition = project.CustomFields.FirstOrDefault(c => c.Key == key);
        var type = CustomField.TypeOfKey(key)!.Value;
        if (definition is { IsCalculated: true })
        {
            return new FieldDefinition(key, definition.DisplayName, type, (p, t) => Calculate(p, t, definition.Formula!, type))
            {
                IsCustom = true,
                IsCalculated = true,
            };
        }
        return new FieldDefinition(key, definition?.DisplayName ?? key, type,
            (_, t) => t.CustomValues.TryGetValue(key, out var v) ? v : null)
        {
            IsCustom = true,
        };
    }

    /// <summary>Formula result for a task; "#ERROR" (text fields) or null when the formula fails.</summary>
    private static object? Calculate(Project project, ProjectTask task, string formula, FieldType type)
    {
        try
        {
            return Formula.Convert(Formula.Parse(formula).Evaluate(project, task), type);
        }
        catch (FormulaException)
        {
            // Checked here rather than in a filter: filters run before inner frames unwind.
            if (Formula.IsNested)
                throw;
            return type == FieldType.Text ? "#ERROR" : null;
        }
    }
    /// <summary>Sets a custom field from typed text. Returns false if the text does not fit the field type or pick list.</summary>
    public static bool TrySetCustom(Project project, ProjectTask task, string key, string? text)
    {
        var type = CustomField.TypeOfKey(key) ?? throw new ArgumentException($"'{key}' is not a custom field.");
        var definition = project.CustomFields.FirstOrDefault(c => c.Key == key);
        if (definition is { IsCalculated: true } || !FieldValue.TryParse(text, type, project.Options, out var value))
            return false;
        if (value is string s && definition is { ValueList.Count: > 0 } && !definition.ValueList.Contains(s, StringComparer.CurrentCultureIgnoreCase))
            return false;
        if (value is null)
            task.CustomValues.Remove(key);
        else
            task.CustomValues[key] = value;
        return true;
    }

    public static string ConstraintName(ConstraintType type) => type switch
    {
        ConstraintType.AsLateAsPossible => "As Late As Possible",
        ConstraintType.MustStartOn => "Must Start On",
        ConstraintType.MustFinishOn => "Must Finish On",
        ConstraintType.StartNoEarlierThan => "Start No Earlier Than",
        ConstraintType.StartNoLaterThan => "Start No Later Than",
        ConstraintType.FinishNoEarlierThan => "Finish No Earlier Than",
        ConstraintType.FinishNoLaterThan => "Finish No Later Than",
        _ => "As Soon As Possible",
    };

    /// <summary>Microsoft Project's Status field: Complete, Future Task, On Schedule or Late, at the status date (or now).</summary>
    private static object Status(Project project, ProjectTask task)
    {
        var statusDate = project.StatusDate ?? DateTime.Now;
        if (task.PercentComplete >= 100)
            return "Complete";
        if (task.Start > statusDate)
            return "Future Task";
        return task.CompleteThrough >= statusDate || task.CompleteThrough >= task.Finish ? "On Schedule" : "Late";
    }

    private static object Successors(Project project, ProjectTask task) =>
        string.Join(",", project.Tasks
            .Where(t => t.Predecessors.Any(l => l.PredecessorId == task.Id))
            .Select(t => (project.IndexOf(t) + 1).ToString(CultureInfo.InvariantCulture)));

    private static FieldDefinition F(string key, string name, FieldType type, Func<Project, ProjectTask, object?> get) =>
        new(key, name, type, get);
}
