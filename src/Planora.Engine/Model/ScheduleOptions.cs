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
}
