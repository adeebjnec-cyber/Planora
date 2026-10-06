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

    /// <summary>How over-allocation is judged; Microsoft Project levels day by day by default.</summary>
    public OverallocationBasis Basis { get; set; } = OverallocationBasis.DayByDay;

    /// <summary>Only delay tasks if the project finish date does not move.</summary>
    public bool LevelWithinAvailableSlack { get; set; }

    /// <summary>Remove earlier leveling delays (and leveling splits) before leveling again.</summary>
    public bool ClearLevelingValuesFirst { get; set; } = true;

    /// <summary>
    /// Allow leveling to interrupt a task that is already under way when the conflict starts, pausing its
    /// remaining work until the resource is free, instead of delaying the whole task.
    /// </summary>
    public bool CanSplitRemainingWork { get; set; }

    /// <summary>
    /// "Leveling can adjust individual assignments on a task": on a task with several resources, delay
    /// just the over-allocated resource's assignment instead of the whole task.
    /// </summary>
    public bool LevelIndividualAssignments { get; set; } = true;

    /// <summary>Level only these resources (Microsoft Project's Level Resource); null = all.</summary>
    public IReadOnlyCollection<Resource>? Resources { get; set; }

    /// <summary>Only resolve over-allocations that overlap this range (null = whole project).</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int MaxIterations { get; set; } = 5000;
}

/// <param name="DelayedTasks">Tasks that received a leveling delay or split.</param>
/// <param name="Unresolved">Over-allocations leveling could not remove (e.g. a single task over-using a resource).</param>
public sealed record LevelingResult(IReadOnlyList<ProjectTask> DelayedTasks, IReadOnlyList<Overallocation> Unresolved);

/// <summary>
/// Resolves resource over-allocations by delaying tasks (<see cref="ProjectTask.LevelingDelayMinutes"/>),
/// delaying single assignments on multi-resource tasks, or, when allowed, splitting remaining work.
/// Resources with <see cref="Resource.CanLevel"/> off, and proposed bookings, are left alone. Repeatedly takes the earliest over-allocation, keeps
/// the highest-ranked tasks, and moves the lowest-ranked movable task's work until the resource is free,
/// then reschedules.
/// </summary>
public static class ResourceLeveler
{
    public static LevelingResult Level(Project project, LevelingOptions? options = null)
    {
        options ??= new LevelingOptions();
        if (options.ClearLevelingValuesFirst)
        {
            foreach (var task in project.Tasks)
                ClearTask(task);
        }
        ProjectScheduler.Schedule(project);

        var skipped = new HashSet<(Resource, DateTime)>();
        var delayed = new List<ProjectTask>();

        for (int iteration = 0; iteration < options.MaxIterations; iteration++)
        {
            var conflict = InRange(ResourceAnalysis.FindOverallocations(project, options.Basis), options)
                .FirstOrDefault(o => !skipped.Contains((o.Resource, o.From)));
            if (conflict is null)
                break;

            var competing = ResourceAnalysis.ScheduledAssignments(project, conflict.Resource)
                .Where(a => a.IsWork && a.WorkMinutes > 0 && a.Start < conflict.To && a.Finish > conflict.From)
                .ToList();
            var victim = competing.Count < 2
                ? null
                : Rank(competing, project, options.Order).LastOrDefault(a => CanDelay(a.Task));
            if (victim is null)
            {
                skipped.Add((conflict.Resource, conflict.From));
                continue;
            }

            // The resource next has room when another competing assignment finishes (after the victim
            // starts), or at the end of the over-allocated period.
            var finishes = competing.Where(a => a != victim && a.Finish > victim.Start).Select(a => a.Finish).ToList();
            var resumeAt = finishes.Count > 0 ? finishes.Min() : conflict.To;
            var task = victim.Task;
            var finishBefore = project.Finish;
            var splitsBefore = task.Splits.ToList();
            long delayBefore = task.LevelingDelayMinutes;
            long assignmentDelayBefore = victim.LevelingDelayMinutes;

            if (!MoveAssignment(victim, resumeAt, options) && !Move(project, task, conflict.From, resumeAt, options))
            {
                skipped.Add((conflict.Resource, conflict.From));
                continue;
            }
            ProjectScheduler.Schedule(project);

            if (options.LevelWithinAvailableSlack && project.Finish > finishBefore)
            {
                task.LevelingDelayMinutes = delayBefore;
                victim.LevelingDelayMinutes = assignmentDelayBefore;
                task.RestoreSplits(splitsBefore);
                ProjectScheduler.Schedule(project);
                skipped.Add((conflict.Resource, conflict.From));
                continue;
            }

            if (!delayed.Contains(task))
                delayed.Add(task);
        }

        return new LevelingResult(delayed, InRange(ResourceAnalysis.FindOverallocations(project, options.Basis), options).ToList());
    }

    /// <summary>
    /// On a task whose dates come from several resources, delays only this assignment until
    /// <paramref name="resumeAt"/>. Returns false when the whole task should move instead.
    /// </summary>
    private static bool MoveAssignment(Assignment victim, DateTime resumeAt, LevelingOptions options)
    {
        var task = victim.Task;
        if (!options.LevelIndividualAssignments || task.Splits.Count > 0 || victim.IsContoured
            || task.Assignments.Count(a => a.IsWork && a.Units > 0) < 2 || victim.EffectiveCalendar is not { } cal)
            return false;
        long delay = cal.WorkingMinutesBetween(victim.Start, resumeAt);
        if (delay <= 0)
            return false;
        victim.LevelingDelayMinutes += delay;
        return true;
    }

    /// <summary>
    /// Moves a task's work out of the way until <paramref name="resumeAt"/>: splits it at the conflict
    /// when it is already under way there (and splitting is allowed), otherwise delays its start.
    /// Returns false if nothing could be moved.
    /// </summary>
    private static bool Move(Project project, ProjectTask task, DateTime conflictFrom, DateTime resumeAt, LevelingOptions options)
    {
        var cal = task.Calendar ?? project.Calendar;
        if (options.CanSplitRemainingWork && task.Start < conflictFrom && task.DurationMinutes > 0 && !task.Assignments.Any(a => a.IsContoured))
        {
            long workBefore = WorkBefore(task, cal, conflictFrom);
            long gap = cal.WorkingMinutesBetween(conflictFrom, resumeAt);
            if (workBefore > 0 && workBefore < task.DurationMinutes && gap > 0)
            {
                task.Split(workBefore, gap, fromLeveling: true);
                return true;
            }
        }

        long delay = (long)Math.Ceiling((resumeAt - task.Start).TotalMinutes);
        if (delay <= 0)
            return false;
        task.LevelingDelayMinutes += delay;
        return true;
    }

    /// <summary>Working minutes of the task's own work (split gaps excluded) before a moment.</summary>
    private static long WorkBefore(ProjectTask task, Calendars.WorkingCalendar cal, DateTime moment)
    {
        double minutes = 0;
        foreach (var segment in task.Segments)
        {
            if (moment <= segment.Start)
                break;
            minutes += cal.WorkingTimeBetween(segment.Start, moment < segment.Finish ? moment : segment.Finish).TotalMinutes;
        }
        return (long)Math.Round(minutes);
    }

    /// <summary>Removes leveling delays and leveling splits from the given tasks (or all tasks) and reschedules.</summary>
    public static void ClearLeveling(Project project, IEnumerable<ProjectTask>? tasks = null)
    {
        foreach (var task in tasks ?? project.Tasks)
            ClearTask(task);
        ProjectScheduler.Schedule(project);
    }

    private static void ClearTask(ProjectTask task)
    {
        task.LevelingDelayMinutes = 0;
        task.RemoveLevelingSplits();
        foreach (var a in task.Assignments)
            a.LevelingDelayMinutes = 0;
    }

    private static IEnumerable<Overallocation> InRange(IEnumerable<Overallocation> overallocations, LevelingOptions options) =>
        overallocations.Where(o => o.Resource.CanLevel && o.Resource.BookingType == BookingType.Committed
            && (options.Resources is null || options.Resources.Contains(o.Resource))
            && (options.From is null || o.To > options.From) && (options.To is null || o.From < options.To));

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
