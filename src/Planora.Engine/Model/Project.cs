using Planora.Engine.Calendars;

namespace Planora.Engine.Model;

/// <summary>
/// A project: an ordered task list (hierarchy expressed by outline levels, as in Microsoft Project),
/// calendars and scheduling options.
/// </summary>
public sealed class Project
{
    private readonly List<ProjectTask> _tasks = new();
    private int _nextId = 1;

    public Project(string name, DateTime startDate)
    {
        Name = name;
        StartDate = startDate;
        FinishDate = startDate;
        Calendar = WorkingCalendar.CreateStandard();
        Calendars.Add(Calendar);
    }

    public string Name { get; set; }

    /// <summary>Project start, used when scheduling forward (the default).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Project finish, used when <see cref="ScheduleFromFinish"/> is true.</summary>
    public DateTime FinishDate { get; set; }

    /// <summary>Schedule backward from <see cref="FinishDate"/> instead of forward from <see cref="StartDate"/>.</summary>
    public bool ScheduleFromFinish { get; set; }

    public DateTime? StatusDate { get; set; }

    /// <summary>The project calendar, used by tasks that have no calendar of their own.</summary>
    public WorkingCalendar Calendar { get; set; }

    public List<WorkingCalendar> Calendars { get; } = new();
    public ScheduleOptions Options { get; } = new();

    /// <summary>Tasks in display (outline) order.</summary>
    public IReadOnlyList<ProjectTask> Tasks => _tasks;

    /// <summary>Earliest start of any task; set by the scheduler.</summary>
    public DateTime Start { get; internal set; }

    /// <summary>Latest finish of any task; set by the scheduler.</summary>
    public DateTime Finish { get; internal set; }

    public ProjectTask? GetTask(int id) => _tasks.FirstOrDefault(t => t.Id == id);

    public ProjectTask GetRequiredTask(int id) =>
        GetTask(id) ?? throw new KeyNotFoundException($"Task {id} does not exist.");

    public int IndexOf(ProjectTask task) => _tasks.IndexOf(task);

    /// <summary>Appends a top-level task.</summary>
    public ProjectTask AddTask(string name, long durationMinutes = 0)
    {
        var task = new ProjectTask(_nextId++, name) { DurationMinutes = durationMinutes };
        _tasks.Add(task);
        RebuildHierarchy();
        return task;
    }

    /// <summary>
    /// Inserts a task before the row at <paramref name="index"/>, taking that row's outline level
    /// (or the level of the last row when appending), like Microsoft Project's Insert Task.
    /// </summary>
    public ProjectTask InsertTask(int index, string name, long durationMinutes = 0)
    {
        if (index < 0 || index > _tasks.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var task = new ProjectTask(_nextId++, name) { DurationMinutes = durationMinutes };
        if (index < _tasks.Count)
            task.OutlineLevel = _tasks[index].OutlineLevel;
        else if (index > 0)
            task.OutlineLevel = _tasks[index - 1].OutlineLevel;
        _tasks.Insert(index, task);
        RebuildHierarchy();
        return task;
    }

    /// <summary>Appends a recurring task: a summary task whose subtasks are the generated occurrences.</summary>
    public ProjectTask AddRecurringTask(string name, RecurrencePattern pattern)
    {
        var summary = AddTask(name);
        try
        {
            SetRecurrence(summary, pattern);
        }
        catch
        {
            RemoveTask(summary);
            throw;
        }
        return summary;
    }

    /// <summary>
    /// (Re)generates the occurrences of a recurring task. Each occurrence gets a Start No Earlier Than
    /// constraint on its date. Existing occurrences are replaced.
    /// </summary>
    public void SetRecurrence(ProjectTask summary, RecurrencePattern pattern)
    {
        RequireIndex(summary);
        var dates = pattern.Dates()
            .Where(d => !pattern.SkipNonWorkingDays || Calendar.IsWorkingDay(d))
            .ToList();
        if (dates.Count == 0)
            throw new InvalidOperationException("The recurrence pattern produces no occurrences on working days.");

        foreach (var child in summary.Children.ToList())
            RemoveTask(child);

        summary.Recurrence = pattern;
        int index = _tasks.IndexOf(summary) + 1;
        int number = 1;
        foreach (var date in dates)
        {
            var occurrence = new ProjectTask(_nextId++, $"{summary.Name} {number++}")
            {
                DurationMinutes = pattern.OccurrenceDurationMinutes,
                OutlineLevel = summary.OutlineLevel + 1,
                ConstraintType = ConstraintType.StartNoEarlierThan,
                ConstraintDate = Calendar.NextWorkingMoment(date.ToDateTime(TimeOnly.MinValue)),
            };
            _tasks.Insert(index++, occurrence);
        }
        RebuildHierarchy();
    }

    /// <summary>Removes a task, its subtasks, and every link pointing at them.</summary>
    public void RemoveTask(ProjectTask task)
    {
        var removed = Subtree(task).ToList();
        var ids = removed.Select(t => t.Id).ToHashSet();
        foreach (var t in removed)
            _tasks.Remove(t);
        foreach (var t in _tasks)
            t.Predecessors.RemoveAll(l => ids.Contains(l.PredecessorId));
        RebuildHierarchy();
    }

    /// <summary>Makes the task (and its subtasks) a subtask of the task above it.</summary>
    public void Indent(ProjectTask task)
    {
        int index = RequireIndex(task);
        if (index == 0 || _tasks[index - 1].OutlineLevel < task.OutlineLevel)
            throw new InvalidOperationException($"Task '{task.Name}' cannot be indented further.");
        foreach (var t in Subtree(task).ToList())
            t.OutlineLevel++;
        RemoveLinksBetweenRelatives();
        RebuildHierarchy();
    }

    /// <summary>Moves the task (and its subtasks) up one outline level.</summary>
    public void Outdent(ProjectTask task)
    {
        RequireIndex(task);
        if (task.OutlineLevel == 1)
            throw new InvalidOperationException($"Task '{task.Name}' is already at the top level.");
        foreach (var t in Subtree(task).ToList())
            t.OutlineLevel--;
        RemoveLinksBetweenRelatives();
        RebuildHierarchy();
    }

    /// <summary>Adds a dependency. Only one link may exist between two tasks.</summary>
    public TaskLink Link(ProjectTask predecessor, ProjectTask successor, LinkType type = LinkType.FinishToStart, long lagMinutes = 0)
    {
        RequireIndex(predecessor);
        RequireIndex(successor);
        if (predecessor == successor)
            throw new InvalidOperationException("A task cannot be linked to itself.");
        if (IsAncestor(predecessor, successor) || IsAncestor(successor, predecessor))
            throw new InvalidOperationException("A summary task cannot be linked to one of its own subtasks.");
        if (successor.Predecessors.Any(l => l.PredecessorId == predecessor.Id) ||
            predecessor.Predecessors.Any(l => l.PredecessorId == successor.Id))
            throw new InvalidOperationException($"Tasks '{predecessor.Name}' and '{successor.Name}' are already linked.");
        if (successor.IsSummary && type is LinkType.FinishToFinish or LinkType.StartToFinish)
            throw new InvalidOperationException("A summary task can only be a successor through Finish-to-Start or Start-to-Start links.");

        var link = new TaskLink(predecessor.Id, type, lagMinutes);
        successor.Predecessors.Add(link);
        return link;
    }

    public bool Unlink(ProjectTask predecessor, ProjectTask successor) =>
        successor.Predecessors.RemoveAll(l => l.PredecessorId == predecessor.Id) > 0;

    /// <summary>The task followed by all of its descendants, in outline order.</summary>
    public IEnumerable<ProjectTask> Subtree(ProjectTask task)
    {
        int index = RequireIndex(task);
        yield return task;
        for (int i = index + 1; i < _tasks.Count && _tasks[i].OutlineLevel > task.OutlineLevel; i++)
            yield return _tasks[i];
    }

    public static bool IsAncestor(ProjectTask ancestor, ProjectTask task)
    {
        for (var p = task.Parent; p is not null; p = p.Parent)
        {
            if (p == ancestor)
                return true;
        }
        return false;
    }

    /// <summary>Recomputes parents, children and WBS codes from outline levels.</summary>
    public void RebuildHierarchy()
    {
        var stack = new List<ProjectTask>();
        var counters = new List<int>();
        foreach (var task in _tasks)
        {
            task.ChildList.Clear();
            int level = Math.Clamp(task.OutlineLevel, 1, stack.Count + 1);
            task.OutlineLevel = level;

            stack.RemoveRange(level - 1, stack.Count - (level - 1));
            if (counters.Count < level)
                counters.Add(0);
            counters.RemoveRange(level, counters.Count - level);
            counters[level - 1]++;

            task.Parent = level > 1 ? stack[level - 2] : null;
            task.Parent?.ChildList.Add(task);
            task.WbsCode = string.Join('.', counters);
            stack.Add(task);
        }
    }

    private void RemoveLinksBetweenRelatives()
    {
        RebuildHierarchy();
        foreach (var task in _tasks)
        {
            task.Predecessors.RemoveAll(l =>
            {
                var pred = GetTask(l.PredecessorId);
                return pred is not null && (IsAncestor(pred, task) || IsAncestor(task, pred));
            });
        }
    }

    private int RequireIndex(ProjectTask task)
    {
        int index = _tasks.IndexOf(task);
        return index >= 0 ? index : throw new ArgumentException($"Task '{task.Name}' is not in this project.");
    }
}
