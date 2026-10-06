namespace Planora.Engine.Model;

public sealed class ScheduleOptions
{
    /// <summary>Working minutes in one "day" of duration (Microsoft Project default: 8 hours).</summary>
    public int MinutesPerDay { get; set; } = 480;

    /// <summary>Working minutes in one "week" of duration (Microsoft Project default: 40 hours).</summary>
    public int MinutesPerWeek { get; set; } = 2400;

    /// <summary>Working days in one "month" of duration (Microsoft Project default: 20).</summary>
    public int DaysPerMonth { get; set; } = 20;

    /// <summary>Tasks with total float at or below this many minutes are critical.</summary>
    public long CriticalSlackLimitMinutes { get; set; }

    /// <summary>
    /// When true, tasks without successors get late finish = early finish, so every chain has its own
    /// critical path. When false (default), their late finish is the project finish.
    /// </summary>
    public bool CalculateMultipleCriticalPaths { get; set; }

    /// <summary>
    /// When true and a status date is set, work that should have happened before the status date but
    /// did not is moved to start at the status date: unstarted tasks start no earlier than it, and the
    /// remaining part of in-progress tasks is split off and resumes at it.
    /// </summary>
    public bool RescheduleUncompletedWorkToStatusDate { get; set; }

    /// <summary>
    /// "Tasks will always honor their constraint dates": when true (default), must-on and no-later-than
    /// constraints hold a task even against its links; when false, links win.
    /// </summary>
    public bool HonorConstraints { get; set; } = true;

    /// <summary>"Autolink inserted or moved tasks": a task inserted inside a finish-to-start chain joins it.</summary>
    public bool AutolinkInsertedTasks { get; set; } = true;

    // ---- Defaults for new tasks (Microsoft Project's Options > Schedule) ----

    public TaskType DefaultTaskType { get; set; } = TaskType.FixedUnits;
    public bool NewTasksEffortDriven { get; set; }
    public bool NewTasksManuallyScheduled { get; set; }
}
