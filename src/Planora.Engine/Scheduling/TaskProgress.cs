using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>How Update Project sets progress on tasks that fall across the "complete through" date.</summary>
public enum UpdateProgressMode
{
    /// <summary>"Set 0% - 100% complete": in proportion to the time elapsed by the date.</summary>
    Proportional,

    /// <summary>"Set 0% or 100% complete only": only tasks finished by the date are marked complete.</summary>
    ZeroOrHundred,
}

/// <summary>
/// Progress updates as in Microsoft Project's Tracking commands: % complete buttons, Mark on Track,
/// Update Tasks and Update Project. Callers reschedule the project afterwards.
/// </summary>
public static class TaskProgress
{
    private static readonly WorkingCalendar Elapsed = WorkingCalendar.Create24Hours("Elapsed");

    /// <summary>Sets % complete and fills in actual start/finish from the scheduled dates.</summary>
    public static void SetPercentComplete(ProjectTask task, double percent)
    {
        RequireLeaf(task);
        task.PercentComplete = Math.Clamp(percent, 0, 100);
        if (task.PercentComplete > 0)
            task.ActualStart ??= task.Start;
        else
            task.ActualStart = null;
        task.ActualFinish = task.PercentComplete >= 100 ? task.Finish : null;
    }

    /// <summary>Mark on Track: the task is done exactly as scheduled up to the status date.</summary>
    public static void MarkOnTrack(Project project, ProjectTask task, DateTime statusDate)
    {
        RequireLeaf(task);
        if (statusDate <= task.Start)
            return;
        if (statusDate >= task.Finish || task.DurationMinutes == 0)
        {
            SetPercentComplete(task, 100);
            return;
        }
        var cal = CalendarOf(project, task);
        long done = Math.Min(task.DurationMinutes, WorkDoneBy(task, cal, statusDate));
        SetPercentComplete(task, Math.Round(100.0 * done / task.DurationMinutes, 2));
    }

    /// <summary>
    /// Update Tasks: any of the values may be given. Actual and remaining duration together reset the
    /// duration; an actual finish completes the task and fixes its duration from the actual dates.
    /// </summary>
    public static void UpdateTask(Project project, ProjectTask task, double? percentComplete = null,
        long? actualDurationMinutes = null, long? remainingDurationMinutes = null,
        DateTime? actualStart = null, DateTime? actualFinish = null)
    {
        RequireLeaf(task);
        var cal = CalendarOf(project, task);

        if (actualStart is { } start)
            task.ActualStart = start;

        if (actualFinish is { } finish)
        {
            task.ActualStart ??= task.Start;
            if (finish < task.ActualStart)
                throw new ArgumentException("The actual finish is before the actual start.");
            task.DurationMinutes = Math.Max(0, cal.WorkingMinutesBetween(task.ActualStart.Value, finish));
            task.PercentComplete = 100;
            task.ActualFinish = finish;
            return;
        }

        if (actualDurationMinutes is { } actual)
        {
            if (actual < 0)
                throw new ArgumentException("Actual duration cannot be negative.");
            long remaining = remainingDurationMinutes ?? Math.Max(0, task.DurationMinutes - actual);
            if (remaining < 0)
                throw new ArgumentException("Remaining duration cannot be negative.");
            task.DurationMinutes = actual + remaining;
            double percent = task.DurationMinutes == 0 ? 100 : 100.0 * actual / task.DurationMinutes;
            var keepStart = task.ActualStart;
            SetPercentComplete(task, Math.Round(percent, 2));
            if (keepStart is not null && task.PercentComplete > 0)
                task.ActualStart = keepStart;
            if (task.ActualFinish is not null)
                task.ActualFinish = cal.AddWorkingMinutes(task.ActualStart ?? task.Start, task.DurationMinutes);
            return;
        }

        if (remainingDurationMinutes is { } rem)
        {
            if (rem < 0)
                throw new ArgumentException("Remaining duration cannot be negative.");
            long done = task.ActualDurationMinutes;
            task.DurationMinutes = done + rem;
            task.PercentComplete = task.DurationMinutes == 0 ? 100 : Math.Round(100.0 * done / task.DurationMinutes, 2);
            task.ActualFinish = rem == 0 && task.ActualStart is not null ? cal.AddWorkingMinutes(task.ActualStart.Value, task.DurationMinutes) : null;
            return;
        }

        if (percentComplete is { } p)
        {
            var keepStart = task.ActualStart;
            SetPercentComplete(task, p);
            if (keepStart is not null && task.PercentComplete > 0)
                task.ActualStart = keepStart;
        }
    }

    /// <summary>
    /// Update Project, "Update work as complete through": tasks scheduled before the date are marked
    /// complete; tasks across it get progress up to it (or none, in 0%-or-100% mode). Progress is never
    /// reduced. Returns the tasks changed.
    /// </summary>
    public static IReadOnlyList<ProjectTask> UpdateCompleteThrough(Project project, DateTime through, UpdateProgressMode mode, IEnumerable<ProjectTask>? tasks = null)
    {
        var changed = new List<ProjectTask>();
        foreach (var task in Leaves(project, tasks))
        {
            if (task.Mode == TaskMode.ManuallyScheduled && task.ManualStart is null || task.Start >= through || task.PercentComplete >= 100)
                continue;
            double before = task.PercentComplete;
            if (task.Finish <= through)
                SetPercentComplete(task, 100);
            else if (mode == UpdateProgressMode.Proportional)
            {
                var cal = CalendarOf(project, task);
                double percent = task.DurationMinutes == 0 ? 100 : Math.Round(100.0 * WorkDoneBy(task, cal, through) / task.DurationMinutes, 2);
                if (percent > task.PercentComplete)
                {
                    var keepStart = task.ActualStart;
                    SetPercentComplete(task, percent);
                    if (keepStart is not null)
                        task.ActualStart = keepStart;
                }
            }
            if (task.PercentComplete != before)
                changed.Add(task);
        }
        return changed;
    }

    /// <summary>
    /// Update Project, "Reschedule uncompleted work to start after": unstarted tasks that would start
    /// earlier get a Start No Earlier Than constraint; the rest of in-progress tasks is split off to
    /// resume at the date. Returns the tasks changed.
    /// </summary>
    public static IReadOnlyList<ProjectTask> RescheduleUncompletedWork(Project project, DateTime after, IEnumerable<ProjectTask>? tasks = null)
    {
        var changed = new List<ProjectTask>();
        foreach (var task in Leaves(project, tasks))
        {
            if (task.Mode != TaskMode.AutoScheduled || task.PercentComplete >= 100 || task.Finish <= after && task.PercentComplete > 0 && task.RemainingDurationMinutes == 0)
                continue;
            var cal = CalendarOf(project, task);
            if (task.PercentComplete == 0)
            {
                if (task.Start >= after)
                    continue;
                if (task.ConstraintType is ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible or ConstraintType.StartNoEarlierThan)
                {
                    task.ConstraintType = ConstraintType.StartNoEarlierThan;
                    task.ConstraintDate = task.ConstraintDate is { } existing && existing > after ? existing : after;
                    changed.Add(task);
                }
                continue;
            }

            var completeThrough = cal.AddWorkingMinutes(cal.NextWorkingMoment(task.ActualStart ?? task.Start), task.ActualDurationMinutes);
            var resume = cal.NextWorkingMoment(after);
            long gap = cal.WorkingMinutesBetween(completeThrough, resume);
            if (gap <= 0 || task.Splits.Any(s => s.AfterWorkMinutes == task.ActualDurationMinutes))
                continue;
            task.Split(task.ActualDurationMinutes, gap);
            changed.Add(task);
        }
        return changed;
    }

    /// <summary>Working minutes of the task's own work (split gaps excluded) scheduled before a moment.</summary>
    private static long WorkDoneBy(ProjectTask task, WorkingCalendar cal, DateTime moment)
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

    private static IEnumerable<ProjectTask> Leaves(Project project, IEnumerable<ProjectTask>? tasks) =>
        (tasks ?? project.Tasks).SelectMany(project.Subtree).Distinct().Where(t => !t.IsSummary && t.IsActive);

    private static WorkingCalendar CalendarOf(Project project, ProjectTask task) =>
        task.IsElapsedDuration ? Elapsed : task.Calendar ?? project.Calendar;

    private static void RequireLeaf(ProjectTask task)
    {
        if (task.IsSummary)
            throw new InvalidOperationException("Progress is entered on subtasks; a summary task rolls it up.");
    }
}
