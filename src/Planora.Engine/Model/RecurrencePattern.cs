namespace Planora.Engine.Model;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// When a recurring task repeats. Monthly and yearly patterns use either a fixed day of the month
/// (<see cref="DayOfMonth"/>) or a relative day such as "the second Tuesday"
/// (<see cref="WeekOfMonth"/> + <see cref="DayOfWeek"/>, with week 5 meaning "last").
/// The range ends after <see cref="Occurrences"/> or on <see cref="End"/>, whichever is set.
/// </summary>
public sealed class RecurrencePattern
{
    public const int MaxOccurrences = 999;

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Weekly;

    /// <summary>Duration of each generated occurrence, in working minutes.</summary>
    public long OccurrenceDurationMinutes { get; set; }

    /// <summary>Every N days / weeks / months / years.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>Days of the week for weekly patterns.</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    public int? DayOfMonth { get; set; }
    public int? WeekOfMonth { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>Month (1-12) for yearly patterns.</summary>
    public int Month { get; set; } = 1;

    public DateOnly Start { get; set; }
    public DateOnly? End { get; set; }
    public int? Occurrences { get; set; }

    /// <summary>When true, occurrences on non-working days are dropped; otherwise they move to the next working time.</summary>
    public bool SkipNonWorkingDays { get; set; } = true;

    public IEnumerable<DateOnly> Dates()
    {
        if (Interval < 1)
            throw new InvalidOperationException("Recurrence interval must be at least 1.");
        if (End is null && Occurrences is null)
            throw new InvalidOperationException("A recurrence needs an end date or a number of occurrences.");

        int limit = Math.Min(Occurrences ?? MaxOccurrences, MaxOccurrences);
        int produced = 0;
        foreach (var date in Candidates())
        {
            if (date < Start)
                continue;
            if (End is { } end && date > end)
                yield break;
            yield return date;
            if (++produced >= limit)
                yield break;
        }
    }

    private IEnumerable<DateOnly> Candidates()
    {
        // Hard stop so a pattern that never matches cannot loop forever.
        var stop = Start.AddYears(100);
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var d = Start; d <= stop; d = d.AddDays(Interval))
                    yield return d;
                break;

            case RecurrenceFrequency.Weekly:
                if (DaysOfWeek.Count == 0)
                    throw new InvalidOperationException("A weekly recurrence needs at least one day of the week.");
                var days = DaysOfWeek.Distinct().OrderBy(d => d).ToArray();
                for (var weekStart = Start.AddDays(-(int)Start.DayOfWeek); weekStart <= stop; weekStart = weekStart.AddDays(7 * Interval))
                {
                    foreach (var day in days)
                        yield return weekStart.AddDays((int)day);
                }
                break;

            case RecurrenceFrequency.Monthly:
                for (var month = new DateOnly(Start.Year, Start.Month, 1); month <= stop; month = month.AddMonths(Interval))
                    yield return DayInMonth(month.Year, month.Month);
                break;

            case RecurrenceFrequency.Yearly:
                for (int year = Start.Year; year <= stop.Year; year += Interval)
                    yield return DayInMonth(year, Month);
                break;
        }
    }

    private DateOnly DayInMonth(int year, int month)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        if (WeekOfMonth is not { } week)
            return new DateOnly(year, month, Math.Min(DayOfMonth ?? Start.Day, daysInMonth));

        if (week >= 5)
        {
            var last = new DateOnly(year, month, daysInMonth);
            return last.AddDays(-(((int)last.DayOfWeek - (int)DayOfWeek + 7) % 7));
        }
        var first = new DateOnly(year, month, 1);
        return first.AddDays(((int)DayOfWeek - (int)first.DayOfWeek + 7) % 7 + 7 * (week - 1));
    }
}
