namespace Planora.Engine.Calendars;

/// <summary>A span of working time within a single day, e.g. 08:00-12:00. <c>To</c> may be 24:00.</summary>
public readonly record struct WorkingTime
{
    public WorkingTime(TimeSpan from, TimeSpan to)
    {
        if (from < TimeSpan.Zero || to > TimeSpan.FromDays(1) || from >= to)
            throw new ArgumentException($"Invalid working time {from}-{to}.");
        From = from;
        To = to;
    }

    public TimeSpan From { get; }
    public TimeSpan To { get; }
    public TimeSpan Length => To - From;

    public static WorkingTime FromHours(double from, double to) =>
        new(TimeSpan.FromHours(from), TimeSpan.FromHours(to));

    public override string ToString() => $"{From:hh\\:mm}-{(To == TimeSpan.FromDays(1) ? "24:00" : To.ToString("hh\\:mm"))}";

    /// <summary>Sorts the spans and rejects overlaps.</summary>
    internal static IReadOnlyList<WorkingTime> Normalize(IEnumerable<WorkingTime> times)
    {
        var sorted = times.OrderBy(t => t.From).ToArray();
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].From < sorted[i - 1].To)
                throw new ArgumentException($"Working times {sorted[i - 1]} and {sorted[i]} overlap.");
        }
        return sorted;
    }
}
