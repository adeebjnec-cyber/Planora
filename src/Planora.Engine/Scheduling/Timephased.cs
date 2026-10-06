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

    public static double FractionBetween(Assignment assignment, DateTime from, DateTime to) =>
        to <= from ? 0 : CumulativeFraction(assignment, to) - CumulativeFraction(assignment, from);

    /// <summary>Average units the assignment works at between two moments (counting only its working time).</summary>
    public static double UnitsBetween(Assignment assignment, DateTime from, DateTime to)
    {
        var cal = assignment.EffectiveCalendar;
        if (cal is null || !assignment.IsWork)
            return 0;
        double minutes = cal.WorkingTimeBetween(from, to).TotalMinutes;
        return minutes <= 0 ? 0 : WorkBetween(assignment, from, to) / minutes;
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
        if (assignment.Contour != WorkContour.Flat && assignment.Segments.Count == 1 && assignment.EffectiveCalendar is { } cal)
        {
            var segment = assignment.Segments[0];
            double total = cal.WorkingTimeBetween(segment.Start, segment.Finish).TotalMinutes;
            for (int i = 1; i < 10; i++)
                yield return cal.AddWorkingTime(segment.Start, TimeSpan.FromMinutes(total * i / 10));
        }
    }

    private static double ActiveMinutes(Assignment assignment, WorkingCalendar cal, DateTime moment)
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
