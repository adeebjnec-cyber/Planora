namespace Planora.Engine.Views;

public sealed record TableColumn(string FieldKey, double Width = 90, string? Title = null);

/// <summary>A named set of columns for the task sheet, like Microsoft Project's tables.</summary>
public sealed class TableDefinition
{
    public TableDefinition(string name, params TableColumn[] columns)
    {
        Name = name;
        Columns = columns.ToList();
    }

    public string Name { get; set; }
    public List<TableColumn> Columns { get; set; }

    private static TableColumn C(string key, double width = 90) => new(key, width);

    public static readonly IReadOnlyList<TableDefinition> Builtin = new[]
    {
        new TableDefinition("Entry", C("Name", 220), C("Duration", 80), C("Start", 100), C("Finish", 100),
            C("Predecessors"), C("ResourceNames", 130), C("PercentComplete", 80), C("TotalSlack", 80), C("Cost")),
        new TableDefinition("Schedule", C("Name", 220), C("Start", 100), C("Finish", 100), C("LateStart", 100),
            C("LateFinish", 100), C("FreeSlack", 80), C("TotalSlack", 80)),
        new TableDefinition("Cost", C("Name", 220), C("FixedCost"), C("FixedCostAccrual", 100), C("Cost"),
            C("BaselineCost"), C("CostVariance"), C("ActualCost"), C("RemainingCost")),
        new TableDefinition("Tracking", C("Name", 220), C("ActualStart", 100), C("ActualFinish", 100), C("PercentComplete", 80),
            C("ActualDuration", 90), C("RemainingDuration", 100), C("ActualCost"), C("ActualWork")),
        new TableDefinition("Variance", C("Name", 220), C("Start", 100), C("Finish", 100), C("BaselineStart", 100),
            C("BaselineFinish", 100), C("StartVariance", 90), C("FinishVariance", 90)),
        new TableDefinition("Work", C("Name", 220), C("Work"), C("BaselineWork"), C("WorkVariance"),
            C("ActualWork"), C("RemainingWork"), C("PercentComplete", 80)),
        new TableDefinition("Baseline", C("Name", 220), C("BaselineDuration", 90), C("BaselineStart", 100),
            C("BaselineFinish", 100), C("BaselineWork"), C("BaselineCost")),
        new TableDefinition("Summary", C("Name", 220), C("Duration", 80), C("Start", 100), C("Finish", 100),
            C("PercentComplete", 80), C("Cost"), C("Work")),
        new TableDefinition("Earned Value", C("Name", 220), C("BCWS"), C("BCWP"), C("ACWP"), C("SV"), C("CV"),
            C("EAC"), C("BAC"), C("VAC")),
        new TableDefinition("Constraint Dates", C("Name", 220), C("Duration", 80), C("ConstraintType", 150), C("ConstraintDate", 100)),
        new TableDefinition("Delay", C("Name", 220), C("LevelingDelay", 90), C("Duration", 80), C("Start", 100),
            C("Finish", 100), C("Successors"), C("ResourceNames", 130)),
    };
}

/// <summary>
/// A saved view: which screen to show, with a table, filter, group and sort. Screen names match the
/// application's view kinds (e.g. "Gantt", "ResourceSheet").
/// </summary>
public sealed class SavedView
{
    public required string Name { get; set; }
    public string Screen { get; set; } = "Gantt";
    public string Table { get; set; } = "Entry";
    public string Filter { get; set; } = "All Tasks";
    public string Group { get; set; } = "No Group";
    public SortDefinition Sort { get; set; } = new();
}
