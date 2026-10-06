namespace Planora.Engine.Calendars;

/// <summary>
/// A date range that overrides the normal work week: a holiday (no working times)
/// or a day with special working hours.
/// </summary>
public sealed class CalendarException
{
    public CalendarException(string name, DateOnly from, DateOnly to, IEnumerable<WorkingTime>? workingTimes = null)
    {
        if (to < from)
            throw new ArgumentException("Exception end date is before its start date.");
        Name = name;
        From = from;
        To = to;
        WorkingTimes = WorkingTime.Normalize(workingTimes ?? Array.Empty<WorkingTime>());
    }

    public string Name { get; set; }
    public DateOnly From { get; }
    public DateOnly To { get; }

    /// <summary>Working times on the covered days. Empty means the days are non-working.</summary>
    public IReadOnlyList<WorkingTime> WorkingTimes { get; }

    public bool IsWorking => WorkingTimes.Count > 0;

    public bool Covers(DateOnly date) => date >= From && date <= To;
}
