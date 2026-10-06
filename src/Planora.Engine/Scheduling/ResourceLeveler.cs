using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>Which tasks keep their dates when tasks compete for a resource (Microsoft Project's leveling order).</summary>
public enum LevelingOrder
{
    /// <summary>Lower task rows keep their dates; higher rows are delayed.</summary>
    IdOnly,

    /// <summary>Tasks with less total float, then earlier start, then higher priority keep their dates.</summary>
    Standard,

    /// <summary>Priority first, then the standard order.</summary>
    PriorityStandard,
}

public sealed class LevelingOptions
{
    public LevelingOrder Order { get; set; } = LevelingOrder.Standard;

    /// <summary>Only delay tasks if the project finish date does not move.</summary>
    public bool LevelWithinAvailableSlack { get; set; }

    /// <summary>Remove earlier leveling delays before leveling again.</summary>
    public bool ClearLevelingValuesFirst { get; set; } = true;

    /// <summary>Only resolve over-allocations that overlap this range (null = whole project).</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int MaxIterations { get; set; } = 5000;
}

/// <param name="DelayedTasks">Tasks that received a leveling delay.</param>
/// <param name="Unresolved">Over-allocations leveling could not remove (e.g. a single task over-using a resource).</param>
public sealed record LevelingResult(IReadOnlyList<ProjectTask> DelayedTasks, IReadOnlyList<Overallocation> Unresolved);

/// <summary>
/// Resolves resource over-allocations by delaying tasks (setting <see cref="ProjectTask.LevelingDelayMinutes"/>).
/// Repeatedly takes the earliest over-allocation, keeps the highest-ranked tasks, and delays the
/// lowest-ranked movable task until the resource is free, then reschedules.
/// </summary>
public static class ResourceLeveler
{
    public static LevelingResult Level(Project project, LevelingOptions? options = null)
    {
        options ??= new LevelingOptions();
        if (options.ClearLevelingValuesFirst)
        {
            foreach (var task in project.Tasks)
                task.LevelingDelayMinutes = 0;
        }
        ProjectScheduler.Schedule(project);

        var skipped = new HashSet<(Resource, DateTime)>();
        var delayed = new List<ProjectTask>();

        for (int iteration = 0; iteration < options.MaxIterations; iteration++)
        {
            var conflict = InRange(ResourceAnalysis.FindOverallocations(project), options)
                .FirstOrDefault(o => !skipped.Contains((o.Resource, o.From)));
            if (conflict is null)
                break;

            var competing = ResourceAnalysis.ScheduledAssignments(project, conflict.Resource)
                .Where(a => a.IsWork && a.Units > 0 && a.Start < conflict.To && a.Finish > conflict.From)
                .ToList();
            var victim = competing.Count < 2
                ? null
                : Rank(competing, project, options.Order).LastOrDefault(a => CanDelay(a.Task));
            if (victim is null)
            {
                skipped.Add((conflict.Resource, conflict.From));
                continue;
            }

            // Assignment edges are breakpoints, so every competitor covers the whole conflict period;
            // the earliest of their finishes is when the resource next has room.
            var resumeAt = competing.Where(a => a != victim).Min(a => a.Finish);
            long delay = (long)Math.Ceiling((resumeAt - victim.Start).TotalMinutes);
            var finishBefore = project.Finish;

            victim.Task.LevelingDelayMinutes += delay;
            ProjectScheduler.Schedule(project);

            if (options.LevelWithinAvailableSlack && project.Finish > finishBefore)
            {
                victim.Task.LevelingDelayMinutes -= delay;
                ProjectScheduler.Schedule(project);
                skipped.Add((conflict.Resource, conflict.From));
                continue;
            }

            if (!delayed.Contains(victim.Task))
                delayed.Add(victim.Task);
        }

        return new LevelingResult(delayed, InRange(ResourceAnalysis.FindOverallocations(project), options).ToList());
    }

    /// <summary>Removes leveling delays from the given tasks (or all tasks) and reschedules.</summary>
    public static void ClearLeveling(Project project, IEnumerable<ProjectTask>? tasks = null)
    {
        foreach (var task in tasks ?? project.Tasks)
            task.LevelingDelayMinutes = 0;
        ProjectScheduler.Schedule(project);
    }

    private static IEnumerable<Overallocation> InRange(IEnumerable<Overallocation> overallocations, LevelingOptions options) =>
        overallocations.Where(o => (options.From is null || o.To > options.From) && (options.To is null || o.From < options.To));

    private static bool CanDelay(ProjectTask task) =>
        task.Mode == TaskMode.AutoScheduled
        && task.ActualStart is null
        && task.PercentComplete == 0
        && task.Priority < 1000
        && task.ConstraintType is not (ConstraintType.MustStartOn or ConstraintType.MustFinishOn)
        && !task.IsSummary;

    /// <summary>Orders assignments from "keeps its dates" to "delayed first".</summary>
    private static IEnumerable<Assignment> Rank(List<Assignment> assignments, Project project, LevelingOrder order)
    {
        var ranked = assignments.OrderBy(a => CanDelay(a.Task) ? 1 : 0);
        IOrderedEnumerable<Assignment> Standard(IOrderedEnumerable<Assignment> q) => q
            .ThenBy(a => a.Task.TotalFloatMinutes)
            .ThenBy(a => a.Start)
            .ThenByDescending(a => a.Task.Priority);

        ranked = order switch
        {
            LevelingOrder.IdOnly => ranked,
            LevelingOrder.PriorityStandard => Standard(ranked.ThenByDescending(a => a.Task.Priority)),
            _ => Standard(ranked),
        };
        return ranked.ThenBy(a => project.IndexOf(a.Task));
    }
}
