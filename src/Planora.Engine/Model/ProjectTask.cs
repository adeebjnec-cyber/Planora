using Planora.Engine.Calendars;

namespace Planora.Engine.Model;

public sealed class ProjectTask
{
    private readonly List<ProjectTask> _children = new();
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

    public bool IsMilestone { get; set; }

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

    public override string ToString() => $"{Id}: {Name}";
}
