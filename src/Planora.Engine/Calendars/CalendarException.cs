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

    /// <summary>Repeats on the same dates every year from <see cref="From"/>'s year onward (e.g. public holidays).</summary>
    public bool RepeatYearly { get; set; }

    public bool Covers(DateOnly date)
    {
        if (!RepeatYearly)
            return date >= From && date <= To;
        if (date < From)
            return false;

        // The occurrence starting this year or last year (a range may cross New Year).
        int length = To.DayNumber - From.DayNumber;
        foreach (int year in new[] { date.Year, date.Year - 1 })
        {
            var start = new DateOnly(year, From.Month, Math.Min(From.Day, DateTime.DaysInMonth(year, From.Month)));
            if (date >= start && date.DayNumber <= start.DayNumber + length)
                return true;
        }
        return false;
    }
}
