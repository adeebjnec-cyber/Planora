namespace Planora.Engine.Calendars;

/// <summary>
/// Working hours that apply for a date range instead of the calendar's default week (Microsoft Project's
/// "Work Weeks", e.g. summer hours). Days not set use the default week.
/// </summary>
public sealed class WorkWeek
{
    private readonly IReadOnlyList<WorkingTime>?[] _days = new IReadOnlyList<WorkingTime>?[7];

    public WorkWeek(string name, DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentException("A work week cannot end before it starts.");
        Name = name;
        From = from;
        To = to;
    }

    public string Name { get; set; }
    public DateOnly From { get; }
    public DateOnly To { get; }

    public bool Covers(DateOnly date) => date >= From && date <= To;

    /// <summary>Sets a day's hours in this work week; no times makes it non-working.</summary>
    public void SetDay(DayOfWeek day, params WorkingTime[] times) => _days[(int)day] = WorkingTime.Normalize(times);

    /// <summary>Makes a day use the default week again.</summary>
    public void ClearDay(DayOfWeek day) => _days[(int)day] = null;

    /// <summary>The day's hours in this work week, or null when the default week applies.</summary>
    public IReadOnlyList<WorkingTime>? Day(DayOfWeek day) => _days[(int)day];

    internal WorkWeek Copy()
    {
        var copy = new WorkWeek(Name, From, To);
        Array.Copy(_days, copy._days, 7);
        return copy;
    }
}
