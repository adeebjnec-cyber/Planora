using System.Globalization;
using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>One fact about what drives a task, or a problem with it.</summary>
public sealed record InspectorItem(string Heading, string Text, bool IsWarning = false);

/// <summary>
/// Task Inspector, as in Microsoft Project: what sets a task's start (the driving predecessor, a constraint,
/// an actual start, a leveling delay, the project start, a manual date), which calendar it uses, and
/// problems such as a missed deadline, negative slack or over-allocated resources. Call on a scheduled project.
/// </summary>
public static class TaskInspector
{
    public static IReadOnlyList<InspectorItem> Inspect(Project project, ProjectTask task)
    {
        var items = new List<InspectorItem>();
        var c = CultureInfo.CurrentCulture;
        string Date(DateTime d) => d.ToString("ddd d MMM yyyy HH:mm", c);
        string Dur(long minutes) => DurationText.Format(minutes, project.Options);

        items.Add(new InspectorItem("Start", Date(task.Start)));
        if (task.IsSummary)
        {
            items.Add(new InspectorItem("Summary task", "Its dates come from its subtasks: the earliest start and the latest finish below it."));
            return items;
        }

        if (task.Mode == TaskMode.ManuallyScheduled)
            items.Add(new InspectorItem("Manually scheduled", "The start and finish are the dates you entered; links and constraints do not move it. Switch to Auto Scheduled to let Planora schedule it."));
        else if (task.ActualStart is { } actual)
            items.Add(new InspectorItem("Actual start", $"Work started on {Date(actual)}, so the start is fixed."));
        else
        {
            bool driven = false;
            foreach (var link in task.Predecessors)
            {
                if (project.GetTask(link.PredecessorId) is not { } predecessor || !TaskPath.IsDriving(project, predecessor, task, link))
                    continue;
                string type = link.Type switch { LinkType.StartToStart => "starts", LinkType.FinishToFinish => "finishes", LinkType.StartToFinish => "starts", _ => "finishes" };
                string lag = link.LagMinutes == 0 && link.LagPercent == 0 ? "" : $" with a lag of {DurationText.FormatLag(link, project.Options)}";
                items.Add(new InspectorItem("Predecessor", $"{project.IndexOf(predecessor) + 1}. {predecessor.Name} {type} on {Date(link.Type is LinkType.StartToStart or LinkType.StartToFinish ? predecessor.Start : predecessor.Finish)} ({Code(link.Type)}{lag})."));
                driven = true;
            }
            foreach (var external in task.ExternalPredecessors.Where(x => x.Finish is not null))
                items.Add(new InspectorItem("External predecessor", $"{external.TaskName} in {Path.GetFileName(external.ProjectPath)} ({Code(external.Type)}), as last refreshed."));
            if (task.ConstraintType is not (ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible) && task.ConstraintDate is { } date)
            {
                bool binding = task.ConstraintType switch
                {
                    ConstraintType.StartNoEarlierThan or ConstraintType.MustStartOn => Math.Abs((task.Start - (task.Calendar ?? project.Calendar).NextWorkingMoment(date)).TotalMinutes) < 1 || task.Start == date,
                    ConstraintType.FinishNoEarlierThan or ConstraintType.MustFinishOn => Math.Abs((task.Finish - date).TotalMinutes) < 1,
                    _ => false,
                };
                items.Add(new InspectorItem("Constraint", $"{Fields.TaskFields.ConstraintName(task.ConstraintType)} {Date(date)}" + (binding ? " — this sets the task's date." : ".")));
                driven |= binding;
            }
            if (task.ConstraintType == ConstraintType.AsLateAsPossible)
                items.Add(new InspectorItem("As Late As Possible", "The task is scheduled as late as its successors and the project finish allow."));
            if (task.LevelingDelayMinutes > 0)
            {
                items.Add(new InspectorItem("Leveling delay", $"Resource leveling delayed the task by {DurationText.Format(task.LevelingDelayMinutes, project.Options, DurationUnit.Days, isElapsed: true)}."));
                driven = true;
            }
            if (project.StatusDate is { } status && project.Options.RescheduleUncompletedWorkToStatusDate && task.Start >= status && task.PercentComplete == 0
                && Math.Abs((task.Start - (task.Calendar ?? project.Calendar).NextWorkingMoment(status)).TotalMinutes) < 1)
            {
                items.Add(new InspectorItem("Status date", $"Unfinished work is rescheduled to start after the status date ({Date(status)})."));
                driven = true;
            }
            if (!driven && task.Predecessors.Count == 0 && task.Start <= (task.Calendar ?? project.Calendar).NextWorkingMoment(project.StartDate))
                items.Add(new InspectorItem("Project start", $"The task has no predecessors, so it starts when the project starts ({Date(project.StartDate)})."));
            else if (!driven && task.Assignments.Any(a => a.DelayMinutes > 0 || a.LevelingDelayMinutes > 0))
                items.Add(new InspectorItem("Assignment delay", "An assignment's delay sets when work begins."));
            else if (!driven)
                items.Add(new InspectorItem("Calendar", "The start moved to the next working time on the task's calendar."));
        }

        var calendar = task.Calendar is { } own ? $"Task calendar: {own.Name}" + (task.IgnoreResourceCalendar ? " (resource calendars ignored)." : ".") : $"Project calendar: {project.Calendar.Name}.";
        items.Add(new InspectorItem("Calendar", calendar));

        // Problems, as Microsoft Project lists them with repair options.
        if (task.MissesDeadline && task.Deadline is { } deadline)
            items.Add(new InspectorItem("Missed deadline", $"The task finishes after its deadline ({Date(deadline)}). Shorten it or its predecessors, add resources, or move the deadline.", IsWarning: true));
        if (task.TotalFloatMinutes < 0)
            items.Add(new InspectorItem("Negative slack", $"Total slack is {Dur(task.TotalFloatMinutes)}: a constraint or deadline cannot be met with the current links.", IsWarning: true));
        foreach (var assignment in task.Assignments.Where(a => a.IsWork))
        {
            if (ResourceAnalysis.FindOverallocations(project, assignment.Resource, OverallocationBasis.DayByDay).Any(o => o.From < task.Finish && o.To > task.Start))
                items.Add(new InspectorItem("Over-allocated resource", $"{assignment.Resource.Name} is over-allocated while working on this task. Level resources, or move or reassign the work.", IsWarning: true));
        }
        if (task.Mode == TaskMode.ManuallyScheduled && task.Predecessors.Any(l => project.GetTask(l.PredecessorId) is { } p && l.Type == LinkType.FinishToStart && p.Finish > task.Start))
            items.Add(new InspectorItem("Link conflict", "A predecessor finishes after this manually scheduled task starts. Respect Links or switch the task to Auto Scheduled.", IsWarning: true));
        return items;
    }

    private static string Code(LinkType type) => type switch
    {
        LinkType.StartToStart => "SS",
        LinkType.FinishToFinish => "FF",
        LinkType.StartToFinish => "SF",
        _ => "FS",
    };
}
