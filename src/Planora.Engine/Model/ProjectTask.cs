using Planora.Engine.Calendars;

namespace Planora.Engine.Model;

public sealed class ProjectTask
{
    private readonly List<ProjectTask> _children = new();
    private readonly List<TaskSplit> _splits = new();
    private double _percentComplete;
    private long _durationMinutes;

    internal ProjectTask(int id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Unique, stable identifier (Microsoft Project's "Unique ID").</summary>
    public int Id { get; }

    public string Name { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>1 for top-level tasks; set through <see cref="Project.Indent"/> / <see cref="Project.Outdent"/>.</summary>
    public int OutlineLevel { get; internal set; } = 1;

    public bool IsActive { get; set; } = true;
    public TaskMode Mode { get; set; } = TaskMode.AutoScheduled;

    /// <summary>Duration in working minutes. Computed by the scheduler for summary tasks.</summary>
    public long DurationMinutes
    {
        get => _durationMinutes;
        set => _durationMinutes = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Duration cannot be negative.");
    }

    /// <summary>Duration runs on continuous clock time (24x7) instead of working time.</summary>
    public bool IsElapsedDuration { get; set; }

    /// <summary>Duration is a guess (shown with "?" in Microsoft Project).</summary>
    public bool IsEstimated { get; set; }

    public bool IsMilestone { get; set; }

    /// <summary>Interruptions in the task, ordered by <see cref="TaskSplit.AfterWorkMinutes"/>.</summary>
    public IReadOnlyList<TaskSplit> Splits => _splits;

    /// <summary>Set on the summary task that owns generated occurrences of a recurring task.</summary>
    public RecurrencePattern? Recurrence { get; internal set; }

    public bool IsRecurring => Recurrence is not null;

    /// <summary>Working minutes of the duration already done, from <see cref="PercentComplete"/>.</summary>
    public long ActualDurationMinutes => (long)Math.Round(DurationMinutes * PercentComplete / 100.0);

    public long RemainingDurationMinutes => DurationMinutes - ActualDurationMinutes;

    /// <summary>
    /// Pauses the task for <paramref name="gapMinutes"/> of working time after <paramref name="afterWorkMinutes"/>
    /// of its work. A split at the same point as an existing one lengthens that gap.
    /// </summary>
    public void Split(long afterWorkMinutes, long gapMinutes)
    {
        if (afterWorkMinutes <= 0 || afterWorkMinutes >= DurationMinutes)
            throw new ArgumentOutOfRangeException(nameof(afterWorkMinutes), "A split must fall inside the task's duration.");
        if (gapMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(gapMinutes), "A split gap must be positive.");

        int existing = _splits.FindIndex(s => s.AfterWorkMinutes == afterWorkMinutes);
        if (existing >= 0)
            _splits[existing] = _splits[existing] with { GapMinutes = _splits[existing].GapMinutes + gapMinutes };
        else
            _splits.Add(new TaskSplit(afterWorkMinutes, gapMinutes));
        _splits.Sort((a, b) => a.AfterWorkMinutes.CompareTo(b.AfterWorkMinutes));
    }

    public void RemoveSplits() => _splits.Clear();

    /// <summary>Working time from start to finish, including split gaps.</summary>
    internal long SpanMinutes => DurationMinutes + _splits.Where(s => s.AfterWorkMinutes < DurationMinutes).Sum(s => s.GapMinutes);

    public ConstraintType ConstraintType { get; set; } = ConstraintType.AsSoonAsPossible;
    public DateTime? ConstraintDate { get; set; }
    public DateTime? Deadline { get; set; }

    /// <summary>Task calendar; null means the project calendar is used.</summary>
    public WorkingCalendar? Calendar { get; set; }

    public double PercentComplete
    {
        get => _percentComplete;
        set => _percentComplete = value is >= 0 and <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Percent complete must be 0-100.");
    }

    /// <summary>Start/finish typed in by the user for manually scheduled tasks.</summary>
    public DateTime? ManualStart { get; set; }
    public DateTime? ManualFinish { get; set; }

    public DateTime? ActualStart { get; set; }
    public DateTime? ActualFinish { get; set; }

    public List<TaskLink> Predecessors { get; } = new();

    // ---- Hierarchy (maintained by Project.RebuildHierarchy) ----

    public ProjectTask? Parent { get; internal set; }
    public IReadOnlyList<ProjectTask> Children => _children;
    internal List<ProjectTask> ChildList => _children;
    public bool IsSummary => _children.Count > 0;
    public string WbsCode { get; internal set; } = "";

    // ---- Scheduling results (set by ProjectScheduler) ----

    public DateTime Start { get; internal set; }
    public DateTime Finish { get; internal set; }
    public DateTime EarlyStart { get; internal set; }
    public DateTime EarlyFinish { get; internal set; }
    public DateTime LateStart { get; internal set; }
    public DateTime LateFinish { get; internal set; }
    public long TotalFloatMinutes { get; internal set; }
    public long FreeFloatMinutes { get; internal set; }
    public bool IsCritical { get; internal set; }
    public bool MissesDeadline { get; internal set; }

    /// <summary>The working pieces of the task; a single segment unless the task is split.</summary>
    public IReadOnlyList<TaskSegment> Segments { get; internal set; } = Array.Empty<TaskSegment>();

    /// <summary>The moment up to which the task's work is complete (used for progress bars and progress lines).</summary>
    public DateTime CompleteThrough { get; internal set; }

    public override string ToString() => $"{Id}: {Name}";
}
