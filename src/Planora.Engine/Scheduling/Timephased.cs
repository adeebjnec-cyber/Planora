using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>
/// How an assignment's work (or material, or cost) is spread over time, following its work contour
/// and working segments. Used by costing, over-allocation checks and the usage views.
/// </summary>
public static class Timephased
{
    /// <summary>Fraction (0-1) of the assignment's total amount that is scheduled before <paramref name="moment"/>.</summary>
    public static double CumulativeFraction(Assignment assignment, DateTime moment)
    {
        var cal = assignment.EffectiveCalendar;
        if (cal is null || assignment.Segments.Count == 0)
            return 0;

        double total = ActiveMinutes(assignment, cal, assignment.Finish);
        if (total <= 0)
            return moment >= assignment.Finish ? 1 : 0;

        double done = Math.Clamp(ActiveMinutes(assignment, cal, moment), 0, total);
        if (assignment.IsContoured)
        {
            double totalWork = assignment.Pieces.Sum(p => (double)p.WorkMinutes);
            if (totalWork <= 0)
                return 0;
            double worked = assignment.Pieces.Sum(p => p.WorkMinutes * Math.Clamp((done - p.Offset) / p.Length, 0, 1));
            return worked / totalWork;
        }
        var shape = WorkContours.Shape(assignment.Contour);
        double shapeTotal = shape.Sum();
        double part = total / shape.Count;
        double accumulated = 0;
        for (int i = 0; i < shape.Count; i++)
        {
            double partStart = i * part;
            if (done <= partStart)
                break;
            accumulated += (Math.Min(done, partStart + part) - partStart) / part * shape[i];
        }
        return accumulated / shapeTotal;
    }

    /// <summary>Work in minutes scheduled between two moments.</summary>
    public static double WorkBetween(Assignment assignment, DateTime from, DateTime to) =>
        assignment.IsWork ? assignment.WorkMinutes * FractionBetween(assignment, from, to) : 0;

    /// <summary>Material quantity used between two moments (material resources).</summary>
    public static double MaterialBetween(Assignment assignment, DateTime from, DateTime to) =>
        assignment.Resource.Type == ResourceType.Material ? assignment.Units * FractionBetween(assignment, from, to) : 0;

    /// <summary>Cost incurred between two moments, spread like the work.</summary>
    public static decimal CostBetween(Assignment assignment, DateTime from, DateTime to) =>
        assignment.Cost * (decimal)FractionBetween(assignment, from, to);

    /// <summary>
    /// Actual work done between two moments: the scheduled work up to the task's complete-through point,
    /// scaled to the assignment's actual work.
    /// </summary>
    public static double ActualWorkBetween(Assignment assignment, DateTime from, DateTime to) =>
        assignment.ActualWorkMinutes * ActualShare(assignment, from, to);

    /// <summary>Actual cost incurred between two moments, spread like the actual work.</summary>
    public static decimal ActualCostBetween(Assignment assignment, DateTime from, DateTime to) =>
        assignment.ActualCost * (decimal)ActualShare(assignment, from, to);

    /// <summary>Share (0-1) of the work done so far that falls between two moments.</summary>
    private static double ActualShare(Assignment assignment, DateTime from, DateTime to)
    {
        var task = assignment.Task;
        if (task.PercentComplete <= 0 || to <= from)
            return 0;
        var cut = task.PercentComplete >= 100 ? assignment.Finish : task.CompleteThrough;
        double done = FractionBetween(assignment, assignment.Start, cut);
        if (done <= 0)
            return from <= assignment.Start && assignment.Start < to ? 1 : 0;
        var end = to < cut ? to : cut;
        return end <= from ? 0 : FractionBetween(assignment, from, end) / done;
    }

    /// <summary>Baseline work (index 0 = Baseline) between two moments, spread evenly over the baseline dates' working time.</summary>
    public static double BaselineWorkBetween(Assignment assignment, int baseline, DateTime from, DateTime to) =>
        assignment.Baselines[baseline] is { } b ? b.WorkMinutes * BaselineShare(assignment.EffectiveCalendar, b, from, to) : 0;

    /// <summary>Baseline cost between two moments, spread like the baseline work.</summary>
    public static decimal BaselineCostBetween(Assignment assignment, int baseline, DateTime from, DateTime to) =>
        assignment.Baselines[baseline] is { } b ? b.Cost * (decimal)BaselineShare(assignment.EffectiveCalendar, b, from, to) : 0;

    /// <summary>Baseline cost of a task between two moments (for tasks with no assignments, e.g. fixed costs only).</summary>
    public static decimal BaselineCostBetween(ProjectTask task, WorkingCalendar calendar, int baseline, DateTime from, DateTime to) =>
        task.Baselines[baseline] is { } b ? b.Cost * (decimal)BaselineShare(calendar, b, from, to) : 0;

    private static double BaselineShare(WorkingCalendar? calendar, BaselineValues b, DateTime from, DateTime to)
    {
        if (to <= from)
            return 0;
        var cal = calendar ?? Elapsed;
        double total = cal.WorkingTimeBetween(b.Start, b.Finish).TotalMinutes;
        if (total <= 0)
            return from <= b.Start && b.Start < to ? 1 : 0;
        var start = from > b.Start ? from : b.Start;
        var end = to < b.Finish ? to : b.Finish;
        return end <= start ? 0 : cal.WorkingTimeBetween(start, end).TotalMinutes / total;
    }

    /// <summary>
    /// A task's fixed cost between two moments: all at the start or finish, or prorated over its working
    /// time, according to its accrual.
    /// </summary>
    public static decimal FixedCostBetween(ProjectTask task, WorkingCalendar calendar, DateTime from, DateTime to)
    {
        if (task.FixedCost == 0 || to <= from)
            return 0;
        switch (task.FixedCostAccrual)
        {
            case CostAccrual.Start:
                return from <= task.Start && task.Start < to ? task.FixedCost : 0;
            case CostAccrual.End:
                return from < task.Finish && task.Finish <= to ? task.FixedCost : 0;
            default:
                double total = calendar.WorkingTimeBetween(task.Start, task.Finish).TotalMinutes;
                if (total <= 0)
                    return from <= task.Start && task.Start < to ? task.FixedCost : 0;
                var start = from > task.Start ? from : task.Start;
                var end = to < task.Finish ? to : task.Finish;
                return end <= start ? 0 : task.FixedCost * (decimal)(calendar.WorkingTimeBetween(start, end).TotalMinutes / total);
        }
    }

    private static readonly WorkingCalendar Elapsed = WorkingCalendar.Create24Hours("Elapsed");

    public static double FractionBetween(Assignment assignment, DateTime from, DateTime to) =>
        to <= from ? 0 : CumulativeFraction(assignment, to) - CumulativeFraction(assignment, from);

    /// <summary>Average units the assignment works at between two moments (regular work over its working time).</summary>
    public static double UnitsBetween(Assignment assignment, DateTime from, DateTime to)
    {
        var cal = assignment.EffectiveCalendar;
        if (cal is null || !assignment.IsWork)
            return 0;
        double minutes = cal.WorkingTimeBetween(from, to).TotalMinutes;
        if (minutes <= 0 || assignment.WorkMinutes <= 0)
            return 0;
        // Overtime is outside working time, so it does not count against available units.
        double regularShare = (double)assignment.RegularWorkMinutes / assignment.WorkMinutes;
        return WorkBetween(assignment, from, to) * regularShare / minutes;
    }

    /// <summary>Work in each period between consecutive <paramref name="boundaries"/>.</summary>
    public static double[] WorkByPeriod(Assignment assignment, IReadOnlyList<DateTime> boundaries)
    {
        var result = new double[Math.Max(0, boundaries.Count - 1)];
        for (int i = 0; i < result.Length; i++)
            result[i] = WorkBetween(assignment, boundaries[i], boundaries[i + 1]);
        return result;
    }

    /// <summary>Moments where the assignment's working rate may change: segment edges and contour steps.</summary>
    internal static IEnumerable<DateTime> Breakpoints(Assignment assignment)
    {
        foreach (var segment in assignment.Segments)
        {
            yield return segment.Start;
            yield return segment.Finish;
        }
        if (assignment.IsContoured && assignment.Segments.Count == 1 && assignment.EffectiveCalendar is { } contourCal)
        {
            var start = assignment.Segments[0].Start;
            foreach (var piece in assignment.Pieces)
            {
                yield return contourCal.AddWorkingMinutes(start, piece.Offset);
                yield return contourCal.AddWorkingMinutes(start, piece.End);
            }
            yield break;
        }        if (assignment.Contour != WorkContour.Flat && assignment.Segments.Count == 1 && assignment.EffectiveCalendar is { } cal)
        {
            var segment = assignment.Segments[0];
            double total = cal.WorkingTimeBetween(segment.Start, segment.Finish).TotalMinutes;
            for (int i = 1; i < 10; i++)
                yield return cal.AddWorkingTime(segment.Start, TimeSpan.FromMinutes(total * i / 10));
        }
    }

    /// <summary>Working minutes of the assignment's own working periods (split gaps excluded) before a moment.</summary>
    internal static double ActiveMinutes(Assignment assignment, WorkingCalendar cal, DateTime moment)
    {
        double minutes = 0;
        foreach (var segment in assignment.Segments)
        {
            if (segment.Start >= moment)
                break;
            var end = segment.Finish < moment ? segment.Finish : moment;
            minutes += cal.WorkingTimeBetween(segment.Start, end).TotalMinutes;
        }
        return minutes;
    }
}
