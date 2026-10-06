namespace Planora.Engine.Model;

/// <summary>How an assignment's work is distributed over its duration (Microsoft Project's work contours).</summary>
public enum WorkContour
{
    Flat,
    BackLoaded,
    FrontLoaded,
    DoublePeak,
    EarlyPeak,
    LatePeak,
    Bell,
    Turtle,
}

public static class WorkContours
{
    /// <summary>
    /// Each contour splits the assignment into ten equal parts and works each part at a percentage of
    /// the assigned units. The peak is always 100%, so non-flat contours stretch the assignment.
    /// </summary>
    private static readonly Dictionary<WorkContour, int[]> Shapes = new()
    {
        [WorkContour.Flat] = new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 },
        [WorkContour.BackLoaded] = new[] { 10, 15, 25, 50, 50, 75, 75, 100, 100, 100 },
        [WorkContour.FrontLoaded] = new[] { 100, 100, 100, 75, 75, 50, 50, 25, 15, 10 },
        [WorkContour.DoublePeak] = new[] { 25, 50, 100, 50, 25, 25, 50, 100, 50, 25 },
        [WorkContour.EarlyPeak] = new[] { 25, 50, 100, 100, 75, 50, 50, 25, 15, 10 },
        [WorkContour.LatePeak] = new[] { 10, 15, 25, 50, 50, 75, 100, 100, 50, 25 },
        [WorkContour.Bell] = new[] { 10, 20, 40, 80, 100, 100, 80, 40, 20, 10 },
        [WorkContour.Turtle] = new[] { 25, 50, 75, 100, 100, 100, 100, 75, 50, 25 },
    };

    public static IReadOnlyList<int> Shape(WorkContour contour) => Shapes[contour];

    /// <summary>Average fraction of the assigned units worked over the assignment (1.0 for flat).</summary>
    public static double AverageFactor(WorkContour contour) => Shapes[contour].Sum() / 1000.0;
}

/// <summary>A resource assigned to a task.</summary>
public sealed class Assignment
{
    private double _units;
    private long _workMinutes;

    internal Assignment(ProjectTask task, Resource resource, double units)
    {
        Task = task;
        Resource = resource;
        Units = units;
    }

    public ProjectTask Task { get; }
    public Resource Resource { get; }

    /// <summary>
    /// Work resources: share of the resource's time (1.0 = 100%). Material resources: quantity of material.
    /// Not used for cost resources.
    /// </summary>
    public double Units
    {
        get => _units;
        set => _units = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Units cannot be negative.");
    }

    /// <summary>Work in minutes (work resources only).</summary>
    public long WorkMinutes
    {
        get => _workMinutes;
        set => _workMinutes = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Work cannot be negative.");
    }

    /// <summary>Amount for cost resources, entered by the user.</summary>
    public decimal CostAmount { get; set; }

    public WorkContour Contour { get; set; } = WorkContour.Flat;

    /// <summary>Cost rate table to use, 0-4 (A-E).</summary>
    public int CostRateTable { get; set; }

    /// <summary>Working time after the task start before this assignment begins.</summary>
    public long DelayMinutes { get; set; }

    /// <summary>Baseline work and cost (index 0 = Baseline, 1-10 = Baseline1-10).</summary>
    public BaselineValues?[] Baselines { get; } = new BaselineValues?[Baseline.Count];

    // ---- Computed by the scheduler ----

    public DateTime Start { get; internal set; }
    public DateTime Finish { get; internal set; }
    public decimal Cost { get; internal set; }
    public long ActualWorkMinutes { get; internal set; }
    public decimal ActualCost { get; internal set; }

    /// <summary>Calendar the assignment is worked on (resource calendar, possibly intersected with the task calendar).</summary>
    internal Calendars.WorkingCalendar? EffectiveCalendar { get; set; }

    /// <summary>The periods the assignment is actually working (one, or the task's segments if the task is split).</summary>
    public IReadOnlyList<TaskSegment> Segments { get; internal set; } = Array.Empty<TaskSegment>();

    public long RemainingWorkMinutes => WorkMinutes - ActualWorkMinutes;
    public decimal RemainingCost => Cost - ActualCost;

    /// <summary>Whether this assignment does work over time (and so drives the task's duration).</summary>
    public bool IsWork => Resource.Type == ResourceType.Work;

    /// <summary>Working minutes this assignment needs to complete its work at its units and contour.</summary>
    public long SpanMinutes => Units <= 0
        ? Task.DurationMinutes
        : (long)Math.Round(WorkMinutes / (Units * WorkContours.AverageFactor(Contour)));

    public override string ToString() => $"{Resource.Name} on {Task.Name}";
}
