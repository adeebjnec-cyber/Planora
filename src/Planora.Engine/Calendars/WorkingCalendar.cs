namespace Planora.Engine.Calendars;

/// <summary>
/// A calendar of working time: a work week plus exceptions (holidays, special days).
/// A calendar may derive from a base calendar (e.g. a resource calendar based on the
/// project's Standard calendar); anything not set locally is inherited from the base.
/// Lookup order for a date: own exception, base exception, own work week, base work week.
/// </summary>
public sealed class WorkingCalendar
{
    /// <summary>Guards against calendars with no working time at all.</summary>
    private const int MaxConsecutiveNonWorkingDays = 3660;

    private readonly IReadOnlyList<WorkingTime>?[] _week = new IReadOnlyList<WorkingTime>?[7];

    public WorkingCalendar(string name, WorkingCalendar? baseCalendar = null)
    {
        Name = name;
        BaseCalendar = baseCalendar;
    }

    public string Name { get; set; }
    public WorkingCalendar? BaseCalendar { get; }
    public List<CalendarException> Exceptions { get; } = new();

    /// <summary>Microsoft Project's "Standard" calendar: Mon-Fri 08:00-12:00, 13:00-17:00.</summary>
    public static WorkingCalendar CreateStandard(string name = "Standard")
    {
        var calendar = new WorkingCalendar(name);
        var day = new[] { WorkingTime.FromHours(8, 12), WorkingTime.FromHours(13, 17) };
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            calendar.SetWorkWeekDay(d, d is DayOfWeek.Saturday or DayOfWeek.Sunday ? Array.Empty<WorkingTime>() : day);
        return calendar;
    }

    /// <summary>Microsoft Project's "24 Hours" calendar.</summary>
    public static WorkingCalendar Create24Hours(string name = "24 Hours")
    {
        var calendar = new WorkingCalendar(name);
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            calendar.SetWorkWeekDay(d, WorkingTime.FromHours(0, 24));
        return calendar;
    }

    /// <summary>Microsoft Project's "Night Shift" calendar (Mon night to Sat morning, 23:00-08:00 with a break 03:00-04:00).</summary>
    public static WorkingCalendar CreateNightShift(string name = "Night Shift")
    {
        var calendar = new WorkingCalendar(name);
        var lateNight = WorkingTime.FromHours(23, 24);
        var earlyMorning = new[] { WorkingTime.FromHours(0, 3), WorkingTime.FromHours(4, 8) };
        calendar.SetWorkWeekDay(DayOfWeek.Sunday);
        calendar.SetWorkWeekDay(DayOfWeek.Monday, lateNight);
        foreach (var d in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            calendar.SetWorkWeekDay(d, earlyMorning[0], earlyMorning[1], lateNight);
        calendar.SetWorkWeekDay(DayOfWeek.Saturday, earlyMorning);
        return calendar;
    }

    /// <summary>Sets the working times for a day of the week. No times means a non-working day.</summary>
    public void SetWorkWeekDay(DayOfWeek day, params WorkingTime[] times) =>
        _week[(int)day] = WorkingTime.Normalize(times);

    /// <summary>Makes a day of the week inherit its working times from the base calendar.</summary>
    public void InheritWorkWeekDay(DayOfWeek day)
    {
        if (BaseCalendar is null)
            throw new InvalidOperationException($"Calendar '{Name}' has no base calendar to inherit from.");
        _week[(int)day] = null;
    }

    public bool IsWorkWeekDayInherited(DayOfWeek day) => _week[(int)day] is null;

    public IReadOnlyList<WorkingTime> GetWorkWeekDay(DayOfWeek day) =>
        _week[(int)day] ?? BaseCalendar?.GetWorkWeekDay(day) ?? Array.Empty<WorkingTime>();

    public CalendarException AddException(string name, DateOnly from, DateOnly to, params WorkingTime[] workingTimes)
    {
        var exception = new CalendarException(name, from, to, workingTimes);
        Exceptions.Add(exception);
        return exception;
    }

    public CalendarException AddHoliday(string name, DateOnly date) => AddException(name, date, date);

    /// <summary>
    /// A calendar that is working only when both calendars are working, used when a task with its own
    /// calendar is assigned a resource with its own calendar.
    /// </summary>
    public static WorkingCalendar Intersect(WorkingCalendar first, WorkingCalendar second) =>
        first == second ? first : new WorkingCalendar($"{first.Name} + {second.Name}") { _intersection = (first, second) };

    private (WorkingCalendar First, WorkingCalendar Second)? _intersection;

    /// <summary>The effective working times on a date, after exceptions and inheritance.</summary>
    public IReadOnlyList<WorkingTime> GetWorkingTimes(DateOnly date)
    {
        if (_intersection is var (first, second))
            return IntersectTimes(first.GetWorkingTimes(date), second.GetWorkingTimes(date));
        return FindException(date)?.WorkingTimes ?? GetWorkWeekDay(date.DayOfWeek);
    }

    private static IReadOnlyList<WorkingTime> IntersectTimes(IReadOnlyList<WorkingTime> a, IReadOnlyList<WorkingTime> b)
    {
        var result = new List<WorkingTime>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            var from = a[i].From > b[j].From ? a[i].From : b[j].From;
            var to = a[i].To < b[j].To ? a[i].To : b[j].To;
            if (from < to)
                result.Add(new WorkingTime(from, to));
            if (a[i].To < b[j].To)
                i++;
            else
                j++;
        }
        return result;
    }

    public bool IsWorkingDay(DateOnly date) => GetWorkingTimes(date).Count > 0;

    private CalendarException? FindException(DateOnly date)
    {
        // Later exceptions win over earlier ones.
        for (int i = Exceptions.Count - 1; i >= 0; i--)
        {
            if (Exceptions[i].Covers(date))
                return Exceptions[i];
        }
        return BaseCalendar?.FindException(date);
    }

    /// <summary>The first moment at or after <paramref name="moment"/> that is inside working time (used for starts).</summary>
    public DateTime NextWorkingMoment(DateTime moment)
    {
        var date = DateOnly.FromDateTime(moment);
        for (int scanned = 0; scanned <= MaxConsecutiveNonWorkingDays; scanned++, date = date.AddDays(1))
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            foreach (var wt in GetWorkingTimes(date))
            {
                var start = dayStart + wt.From;
                var end = dayStart + wt.To;
                if (moment < end)
                    return moment > start ? moment : start;
            }
        }
        throw NoWorkingTime();
    }

    /// <summary>The last moment at or before <paramref name="moment"/> that ends working time (used for finishes).</summary>
    public DateTime PreviousWorkingMoment(DateTime moment)
    {
        var date = DateOnly.FromDateTime(moment);
        for (int scanned = 0; scanned <= MaxConsecutiveNonWorkingDays; scanned++, date = date.AddDays(-1))
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var times = GetWorkingTimes(date);
            for (int i = times.Count - 1; i >= 0; i--)
            {
                var start = dayStart + times[i].From;
                var end = dayStart + times[i].To;
                if (moment > start)
                    return moment < end ? moment : end;
            }
        }
        throw NoWorkingTime();
    }

    /// <summary>
    /// Moves <paramref name="moment"/> by an amount of working time. Positive amounts move forward and
    /// land on the end of the consumed time (finish-style); negative amounts move backward and land on
    /// the start of the consumed time (start-style). Zero returns the moment unchanged.
    /// </summary>
    public DateTime AddWorkingTime(DateTime moment, TimeSpan amount)
    {
        if (amount == TimeSpan.Zero)
            return moment;
        return amount > TimeSpan.Zero ? AddForward(moment, amount) : AddBackward(moment, -amount);
    }

    public DateTime AddWorkingMinutes(DateTime moment, long minutes) =>
        AddWorkingTime(moment, TimeSpan.FromMinutes(minutes));

    private DateTime AddForward(DateTime moment, TimeSpan remaining)
    {
        var date = DateOnly.FromDateTime(moment);
        int idleDays = 0;
        while (true)
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            bool worked = false;
            foreach (var wt in GetWorkingTimes(date))
            {
                var end = dayStart + wt.To;
                if (end <= moment)
                    continue;
                var current = Max(dayStart + wt.From, moment);
                var available = end - current;
                worked = true;
                if (remaining <= available)
                    return current + remaining;
                remaining -= available;
            }
            idleDays = worked ? 0 : idleDays + 1;
            if (idleDays > MaxConsecutiveNonWorkingDays)
                throw NoWorkingTime();
            date = date.AddDays(1);
        }
    }

    private DateTime AddBackward(DateTime moment, TimeSpan remaining)
    {
        var date = DateOnly.FromDateTime(moment);
        int idleDays = 0;
        while (true)
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var times = GetWorkingTimes(date);
            bool worked = false;
            for (int i = times.Count - 1; i >= 0; i--)
            {
                var start = dayStart + times[i].From;
                if (start >= moment)
                    continue;
                var current = Min(dayStart + times[i].To, moment);
                var available = current - start;
                worked = true;
                if (remaining <= available)
                    return current - remaining;
                remaining -= available;
            }
            idleDays = worked ? 0 : idleDays + 1;
            if (idleDays > MaxConsecutiveNonWorkingDays)
                throw NoWorkingTime();
            date = date.AddDays(-1);
        }
    }

    /// <summary>Working time between two moments; negative if <paramref name="to"/> is before <paramref name="from"/>.</summary>
    public TimeSpan WorkingTimeBetween(DateTime from, DateTime to)
    {
        if (to < from)
            return -WorkingTimeBetween(to, from);

        var total = TimeSpan.Zero;
        var last = DateOnly.FromDateTime(to);
        for (var date = DateOnly.FromDateTime(from); date <= last; date = date.AddDays(1))
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            foreach (var wt in GetWorkingTimes(date))
            {
                var start = Max(dayStart + wt.From, from);
                var end = Min(dayStart + wt.To, to);
                if (end > start)
                    total += end - start;
            }
        }
        return total;
    }

    public long WorkingMinutesBetween(DateTime from, DateTime to) =>
        (long)Math.Round(WorkingTimeBetween(from, to).TotalMinutes);

    public override string ToString() => Name;

    private InvalidOperationException NoWorkingTime() =>
        new($"Calendar '{Name}' has no working time within {MaxConsecutiveNonWorkingDays} days.");

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
