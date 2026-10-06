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

    /// <summary>
    /// Repeats the exception on a pattern (daily, weekly, monthly, yearly) starting at <see cref="From"/>,
    /// as Microsoft Project's exception "Recurrence pattern". Each occurrence lasts as long as From..To.
    /// </summary>
    public Model.RecurrencePattern? Recurrence
    {
        get => _recurrence;
        set
        {
            _recurrence = value;
            _occurrences = null;
        }
    }

    private Model.RecurrencePattern? _recurrence;
    private HashSet<DateOnly>? _occurrences;

    /// <summary>Start dates of all occurrences (the exception's own range when it does not repeat).</summary>
    public IEnumerable<DateOnly> OccurrenceStarts()
    {
        if (_recurrence is null)
            return new[] { From };
        _recurrence.Start = From;
        return _occurrences ??= _recurrence.Dates().ToHashSet();
    }

    public bool Covers(DateOnly date)
    {
        if (_recurrence is not null)
        {
            if (date < From)
                return false;
            int span = To.DayNumber - From.DayNumber;
            var starts = (HashSet<DateOnly>)OccurrenceStarts();
            for (int back = 0; back <= span; back++)
            {
                if (starts.Contains(date.AddDays(-back)))
                    return true;
            }
            return false;
        }
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
