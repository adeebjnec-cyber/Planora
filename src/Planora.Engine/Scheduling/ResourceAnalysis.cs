using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>A period where a resource is asked to work more than its maximum units.</summary>
public sealed record Overallocation(Resource Resource, DateTime From, DateTime To, double PeakUnits);

/// <summary>Resource loading: totals, peak units and over-allocation. Run after scheduling.</summary>
public static class ResourceAnalysis
{
    private const double Tolerance = 1e-6;

    /// <summary>Scheduled work assignments of a resource (skips inactive tasks).</summary>
    public static IEnumerable<Assignment> ScheduledAssignments(Project project, Resource resource) =>
        project.AssignmentsOf(resource).Where(a => a.EffectiveCalendar is not null);

    public static IReadOnlyList<Overallocation> FindOverallocations(Project project) =>
        project.Resources.SelectMany(r => FindOverallocations(project, r)).OrderBy(o => o.From).ToList();

    public static IReadOnlyList<Overallocation> FindOverallocations(Project project, Resource resource) =>
        Analyse(project, resource).Overallocations;

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

    private static (double Peak, List<Overallocation> Overallocations) Analyse(Project project, Resource resource)
    {
        var result = new List<Overallocation>();
        if (resource.Type != ResourceType.Work)
            return (0, result);

        var assignments = ScheduledAssignments(project, resource).Where(a => a.WorkMinutes > 0 && a.Finish > a.Start).ToList();
        var points = assignments.SelectMany(Timephased.Breakpoints).Distinct().OrderBy(p => p).ToList();

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

            if (units > resource.MaxUnits + Tolerance)
            {
                if (result.Count > 0 && result[^1].To == from)
                    result[^1] = result[^1] with { To = to, PeakUnits = Math.Max(result[^1].PeakUnits, units) };
                else
                    result.Add(new Overallocation(resource, from, to, units));
            }
        }
        return (peak, result);
    }
}
