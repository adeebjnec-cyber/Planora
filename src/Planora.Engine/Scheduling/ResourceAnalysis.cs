using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>A period where a resource is asked to work more than it has available.</summary>
public sealed record Overallocation(Resource Resource, DateTime From, DateTime To, double PeakUnits);

/// <summary>
/// How over-allocation is judged (Microsoft Project's "Look for overallocations on a ... basis"):
/// moment by moment, or by comparing the work in each period with the time available in it.
/// </summary>
public enum OverallocationBasis
{
    MinuteByMinute,
    HourByHour,
    DayByDay,
    WeekByWeek,
    MonthByMonth,
}

/// <summary>Resource loading: totals, peak units and over-allocation. Run after scheduling.</summary>
public static class ResourceAnalysis
{
    private const double Tolerance = 1e-6;

    /// <summary>Scheduled work assignments of a resource (skips inactive tasks).</summary>
    public static IEnumerable<Assignment> ScheduledAssignments(Project project, Resource resource) =>
        project.AssignmentsOf(resource).Where(a => a.EffectiveCalendar is not null);

    public static IReadOnlyList<Overallocation> FindOverallocations(Project project, OverallocationBasis basis = OverallocationBasis.MinuteByMinute) =>
        project.Resources.SelectMany(r => FindOverallocations(project, r, basis)).OrderBy(o => o.From).ToList();

    public static IReadOnlyList<Overallocation> FindOverallocations(Project project, Resource resource, OverallocationBasis basis = OverallocationBasis.MinuteByMinute) =>
        basis == OverallocationBasis.MinuteByMinute ? Analyse(project, resource).Overallocations : ByPeriod(project, resource, basis);

    internal static void UpdateResourceTotals(Project project)
    {
        foreach (var resource in project.Resources)
        {
            var assignments = ScheduledAssignments(project, resource).ToList();
            resource.WorkMinutes = assignments.Where(a => a.IsWork).Sum(a => a.WorkMinutes);
            resource.Cost = assignments.Sum(a => a.Cost);
            resource.ActualCost = assignments.Sum(a => a.ActualCost);

            var (peak, overallocations) = Analyse(project, resource);
            resource.PeakUnits = peak;
            resource.IsOverallocated = overallocations.Count > 0;
        }
    }

    private static List<Assignment> WorkingAssignments(Project project, Resource resource) =>
        ScheduledAssignments(project, resource).Where(a => a.WorkMinutes > 0 && a.Finish > a.Start).ToList();

    /// <summary>Moment by moment: units in use between consecutive change points, against availability.</summary>
    private static (double Peak, List<Overallocation> Overallocations) Analyse(Project project, Resource resource)
    {
        var result = new List<Overallocation>();
        if (resource.Type != ResourceType.Work)
            return (0, result);

        var assignments = WorkingAssignments(project, resource);
        var points = assignments.SelectMany(Timephased.Breakpoints)
            .Concat(resource.Availability.SelectMany(p => p.To is { } to ? new[] { p.From, to } : new[] { p.From }))
            .Distinct().OrderBy(p => p).ToList();

        double peak = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var (from, to) = (points[i], points[i + 1]);
            double units = 0;
            foreach (var a in assignments)
            {
                if (a.Start < to && a.Finish > from)
                    units += Timephased.UnitsBetween(a, from, to);
            }
            peak = Math.Max(peak, units);

            if (units > resource.MaxUnitsAt(from) + Tolerance)
            {
                if (result.Count > 0 && result[^1].To == from)
                    result[^1] = result[^1] with { To = to, PeakUnits = Math.Max(result[^1].PeakUnits, units) };
                else
                    result.Add(new Overallocation(resource, from, to, units));
            }
        }

        // Report periods in working time (an availability change at midnight starts at 08:00, say).
        var calendar = resource.Calendar ?? project.Calendar;
        for (int i = 0; i < result.Count; i++)
        {
            var start = calendar.NextWorkingMoment(result[i].From);
            var end = calendar.PreviousWorkingMoment(result[i].To);
            if (end > start)
                result[i] = result[i] with { From = start, To = end };
        }
        return (peak, result);
    }

    /// <summary>
    /// Period by period: a period is over-allocated when the regular work scheduled in it exceeds the
    /// resource's working time in it multiplied by its available units.
    /// </summary>
    private static List<Overallocation> ByPeriod(Project project, Resource resource, OverallocationBasis basis)
    {
        var result = new List<Overallocation>();
        if (resource.Type != ResourceType.Work)
            return result;
        var assignments = WorkingAssignments(project, resource);
        if (assignments.Count == 0)
            return result;

        var calendar = resource.Calendar ?? project.Calendar;
        var first = PeriodStart(assignments.Min(a => a.Start), basis);
        var last = assignments.Max(a => a.Finish);
        for (var from = first; from < last; from = NextPeriod(from, basis))
        {
            var to = NextPeriod(from, basis);
            double work = assignments.Sum(a => Timephased.WorkBetween(a, from, to) * (a.WorkMinutes > 0 ? (double)a.RegularWorkMinutes / a.WorkMinutes : 0));
            double capacity = Capacity(resource, calendar, from, to);
            if (work > capacity + 0.5)
            {
                double minutes = calendar.WorkingTimeBetween(from, to).TotalMinutes;
                double units = minutes > 0 ? work / minutes : double.PositiveInfinity;
                if (result.Count > 0 && result[^1].To == from)
                    result[^1] = result[^1] with { To = to, PeakUnits = Math.Max(result[^1].PeakUnits, units) };
                else
                    result.Add(new Overallocation(resource, from, to, units));
            }
        }
        return result;
    }

    /// <summary>Working minutes in a period times the units available (availability may change inside it).</summary>
    private static double Capacity(Resource resource, WorkingCalendar calendar, DateTime from, DateTime to)
    {
        var edges = resource.Availability
            .SelectMany(p => p.To is { } end ? new[] { p.From, end } : new[] { p.From })
            .Where(d => d > from && d < to)
            .Prepend(from).Append(to).Distinct().OrderBy(d => d).ToList();
        double capacity = 0;
        for (int i = 0; i + 1 < edges.Count; i++)
            capacity += calendar.WorkingTimeBetween(edges[i], edges[i + 1]).TotalMinutes * resource.MaxUnitsAt(edges[i]);
        return capacity;
    }

    private static DateTime PeriodStart(DateTime moment, OverallocationBasis basis) => basis switch
    {
        OverallocationBasis.HourByHour => moment.Date.AddHours(moment.Hour),
        OverallocationBasis.WeekByWeek => moment.Date.AddDays(-(((int)moment.DayOfWeek + 6) % 7)),
        OverallocationBasis.MonthByMonth => new DateTime(moment.Year, moment.Month, 1),
        _ => moment.Date,
    };

    private static DateTime NextPeriod(DateTime start, OverallocationBasis basis) => basis switch
    {
        OverallocationBasis.HourByHour => start.AddHours(1),
        OverallocationBasis.WeekByWeek => start.AddDays(7),
        OverallocationBasis.MonthByMonth => start.AddMonths(1),
        _ => start.AddDays(1),
    };
}
