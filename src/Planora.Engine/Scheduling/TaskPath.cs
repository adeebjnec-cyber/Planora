using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>Which links Task Path follows (Microsoft Project's Format > Task Path).</summary>
[Flags]
public enum TaskPathKind
{
    None = 0,

    /// <summary>Every task the selected task depends on, directly or not.</summary>
    Predecessors = 1,

    /// <summary>Only the predecessors that actually set the task's dates (the chain that drives it).</summary>
    DrivingPredecessors = 2,

    /// <summary>Every task that depends on the selected task.</summary>
    Successors = 4,

    /// <summary>Only the successors whose dates the selected task drives.</summary>
    DrivenSuccessors = 8,
}

/// <summary>Finds the tasks on the selected tasks' paths through the dependency links.</summary>
public static class TaskPath
{
    /// <summary>The tasks on each requested path (the selected tasks themselves excluded). Call on a scheduled project.</summary>
    public static Dictionary<ProjectTask, TaskPathKind> Find(Project project, IEnumerable<ProjectTask> selected, TaskPathKind kinds)
    {
        var result = new Dictionary<ProjectTask, TaskPathKind>();
        var start = selected.Where(t => !t.IsSummary).ToList();
        if (start.Count == 0 || kinds == TaskPathKind.None)
            return result;

        // Successor lists, built once.
        var successors = project.Tasks.ToDictionary(t => t, _ => new List<(ProjectTask Task, TaskLink Link)>());
        foreach (var task in project.Tasks)
        {
            foreach (var link in task.Predecessors)
            {
                if (project.GetTask(link.PredecessorId) is { } predecessor)
                    successors[predecessor].Add((task, link));
            }
        }

        void Walk(TaskPathKind kind, bool backwards, bool drivingOnly)
        {
            var queue = new Queue<ProjectTask>(start);
            var seen = new HashSet<ProjectTask>(start);
            while (queue.Count > 0)
            {
                var task = queue.Dequeue();
                var next = backwards
                    ? task.Predecessors.Select(l => (Task: project.GetTask(l.PredecessorId), Link: l, Successor: task))
                    : successors[task].Select(s => ((ProjectTask?)s.Task, s.Link, Successor: s.Task));
                foreach (var (other, link, successor) in next)
                {
                    if (other is null || !seen.Add(other))
                        continue;
                    var predecessor = backwards ? other : task;
                    if (drivingOnly && !IsDriving(project, predecessor, successor, link))
                        continue;
                    result[other] = result.TryGetValue(other, out var existing) ? existing | kind : kind;
                    queue.Enqueue(other);
                }
            }
        }

        if (kinds.HasFlag(TaskPathKind.Predecessors))
            Walk(TaskPathKind.Predecessors, backwards: true, drivingOnly: false);
        if (kinds.HasFlag(TaskPathKind.DrivingPredecessors))
            Walk(TaskPathKind.DrivingPredecessors, backwards: true, drivingOnly: true);
        if (kinds.HasFlag(TaskPathKind.Successors))
            Walk(TaskPathKind.Successors, backwards: false, drivingOnly: false);
        if (kinds.HasFlag(TaskPathKind.DrivenSuccessors))
            Walk(TaskPathKind.DrivenSuccessors, backwards: false, drivingOnly: true);
        return result;
    }

    /// <summary>
    /// A link drives its successor when the date it imposes is the successor's own date: nothing else
    /// (another link, a constraint, the project start) pushed the successor later.
    /// </summary>
    public static bool IsDriving(Project project, ProjectTask predecessor, ProjectTask successor, TaskLink link)
    {
        if (successor.Mode == TaskMode.ManuallyScheduled || successor.ActualStart is not null)
            return false;
        var cal = successor.Calendar ?? project.Calendar;
        long lag = link.LagFormat == LagFormat.PercentOfPredecessor
            ? (long)Math.Round(predecessor.DurationMinutes * link.LagPercent / 100.0)
            : link.LagMinutes;
        var lagCalendar = link.LagFormat == LagFormat.ElapsedTime ? Calendars.WorkingCalendar.Create24Hours() : project.Calendar;
        DateTime Imposed(DateTime moment) => lagCalendar.AddWorkingMinutes(moment, lag);
        static bool Same(DateTime a, DateTime b) => Math.Abs((a - b).TotalMinutes) < 1;
        return link.Type switch
        {
            LinkType.FinishToStart => Same(cal.NextWorkingMoment(Imposed(predecessor.Finish)), successor.Start) || Same(Imposed(predecessor.Finish), successor.Start),
            LinkType.StartToStart => Same(cal.NextWorkingMoment(Imposed(predecessor.Start)), successor.Start) || Same(Imposed(predecessor.Start), successor.Start),
            LinkType.FinishToFinish => Same(cal.PreviousWorkingMoment(Imposed(predecessor.Finish)), successor.Finish) || Same(Imposed(predecessor.Finish), successor.Finish),
            _ => Same(cal.PreviousWorkingMoment(Imposed(predecessor.Start)), successor.Finish) || Same(Imposed(predecessor.Start), successor.Finish),
        };
    }
}
