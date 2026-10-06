using Planora.Engine.Calendars;

namespace Planora.Engine.Model;

/// <summary>
/// A project: an ordered task list (hierarchy expressed by outline levels, as in Microsoft Project),
/// calendars and scheduling options.
/// </summary>
public sealed class Project
{
    private readonly List<ProjectTask> _tasks = new();
    private readonly List<Resource> _resources = new();
    private int _nextId = 1;
    private int _nextResourceId = 1;

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

    /// <summary>Custom field definitions (aliases and pick lists) for the Text1/Number1/... slots in use.</summary>
    public List<Fields.CustomField> CustomFields { get; } = new();

    /// <summary>User-defined tables, filters, groups and views (they override built-ins of the same name).</summary>
    public List<Views.TableDefinition> Tables { get; } = new();
    public List<Views.FilterDefinition> Filters { get; } = new();
    public List<Views.GroupDefinition> Groups { get; } = new();
    public List<Views.SavedView> SavedViews { get; } = new();
    public ScheduleOptions Options { get; } = new();

    /// <summary>Tasks in display (outline) order.</summary>
    public IReadOnlyList<ProjectTask> Tasks => _tasks;

    /// <summary>Earliest start of any task; set by the scheduler.</summary>
    public DateTime Start { get; internal set; }

    /// <summary>Latest finish of any task; set by the scheduler.</summary>
    public DateTime Finish { get; internal set; }

    public IReadOnlyList<Resource> Resources => _resources;

    /// <summary>Every assignment in the project, in task order.</summary>
    public IEnumerable<Assignment> Assignments => _tasks.SelectMany(t => t.Assignments);

    /// <summary>Budget cost per cost-type budget resource (Microsoft Project's "Budget Cost" on the project summary).</summary>
    public Dictionary<Resource, decimal> BudgetCosts { get; } = new();

    /// <summary>Budget work or quantity per work/material budget resource ("Budget Work").</summary>
    public Dictionary<Resource, double> BudgetWork { get; } = new();

    /// <summary>Project-level baselines (index 0 = Baseline, 1-10 = Baseline1-10).</summary>
    public BaselineValues?[] Baselines { get; } = new BaselineValues?[Baseline.Count];

    // ---- Totals computed by the scheduler ----

    public long WorkMinutes { get; internal set; }
    public decimal Cost { get; internal set; }
    public decimal ActualCost { get; internal set; }
    public EarnedValue EarnedValue { get; internal set; } = EarnedValue.Empty;

    public ProjectTask? GetTask(int id) => _tasks.FirstOrDefault(t => t.Id == id);

    public ProjectTask GetRequiredTask(int id) =>
        GetTask(id) ?? throw new KeyNotFoundException($"Task {id} does not exist.");

    public int IndexOf(ProjectTask task) => _tasks.IndexOf(task);

    /// <summary>A new task with the project's defaults for task type, effort driving and schedule mode.</summary>
    private ProjectTask NewTask(string name, long durationMinutes) => new(_nextId++, name)
    {
        DurationMinutes = durationMinutes,
        Type = Options.DefaultTaskType,
        IsEffortDriven = Options.NewTasksEffortDriven,
        Mode = Options.NewTasksManuallyScheduled ? TaskMode.ManuallyScheduled : TaskMode.AutoScheduled,
    };
    /// <summary>Appends a top-level task.</summary>
    public ProjectTask AddTask(string name, long durationMinutes = 0)
    {
        var task = NewTask(name, durationMinutes);
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

        var task = NewTask(name, durationMinutes);
        if (index < _tasks.Count)
            task.OutlineLevel = _tasks[index].OutlineLevel;
        else if (index > 0)
            task.OutlineLevel = _tasks[index - 1].OutlineLevel;
        _tasks.Insert(index, task);
        RebuildHierarchy();
        return task;
    }

    /// <summary>Adds a recurring task (at the end, or before row <paramref name="index"/>): a summary task whose subtasks are the generated occurrences.</summary>
    public ProjectTask AddRecurringTask(string name, RecurrencePattern pattern, int? index = null)
    {
        var summary = index is { } at ? InsertTask(Math.Clamp(at, 0, _tasks.Count), name) : AddTask(name);
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

    /// <summary>Removes a single task, moving its subtasks up one outline level (used when copying part of an outline).</summary>
    internal void RemoveTaskKeepChildren(ProjectTask task)
    {
        foreach (var descendant in Subtree(task).Skip(1).ToList())
            descendant.OutlineLevel--;
        _tasks.Remove(task);
        foreach (var t in _tasks)
            t.Predecessors.RemoveAll(l => l.PredecessorId == task.Id);
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

    // ------------------------------------------------------------------ loading (used by the file reader)

    /// <summary>Appends a task with a known ID and outline level, as read from a file.</summary>
    internal ProjectTask RestoreTask(int id, string name, int outlineLevel)
    {
        if (GetTask(id) is not null)
            throw new InvalidDataException($"Duplicate task ID {id}.");
        var task = new ProjectTask(id, name) { OutlineLevel = outlineLevel };
        _tasks.Add(task);
        _nextId = Math.Max(_nextId, id + 1);
        return task;
    }

    internal Resource RestoreResource(int id, string name, ResourceType type, WorkingCalendar? calendar)
    {
        if (GetResource(id) is not null)
            throw new InvalidDataException($"Duplicate resource ID {id}.");
        var resource = new Resource(id, name, type, calendar);
        _resources.Add(resource);
        _nextResourceId = Math.Max(_nextResourceId, id + 1);
        return resource;
    }

    internal static Assignment RestoreAssignment(ProjectTask task, Resource resource, double units)
    {
        var assignment = new Assignment(task, resource, units);
        task.AssignmentList.Add(assignment);
        return assignment;
    }

    // ------------------------------------------------------------------ resources

    /// <summary>Adds a resource. Work resources get their own calendar based on the project calendar.</summary>
    public Resource AddResource(string name, ResourceType type = ResourceType.Work)
    {
        var calendar = type == ResourceType.Work ? new WorkingCalendar(name, Calendar) : null;
        var resource = new Resource(_nextResourceId++, name, type, calendar);
        _resources.Add(resource);
        return resource;
    }

    public Resource? GetResource(int id) => _resources.FirstOrDefault(r => r.Id == id);

    /// <summary>Removes a resource and all of its assignments.</summary>
    public void RemoveResource(Resource resource)
    {
        foreach (var task in _tasks)
            task.AssignmentList.RemoveAll(a => a.Resource == resource);
        BudgetCosts.Remove(resource);
        BudgetWork.Remove(resource);
        _resources.Remove(resource);
    }

    public IEnumerable<Assignment> AssignmentsOf(Resource resource) =>
        Assignments.Where(a => a.Resource == resource);

    /// <summary>
    /// Assigns a resource to a task. For work resources, <paramref name="units"/> is the share of the
    /// resource's time (1.0 = 100%); for material resources it is the quantity used. On effort-driven
    /// tasks the existing total work is shared among all resources, shortening the task (or, for Fixed
    /// Duration tasks, lowering everyone's units).
    /// </summary>
    public Assignment Assign(ProjectTask task, Resource resource, double units = 1.0)
    {
        RequireIndex(task);
        if (!_resources.Contains(resource))
            throw new ArgumentException($"Resource '{resource.Name}' is not in this project.");
        if (task.IsSummary)
            throw new InvalidOperationException("Resources are assigned to subtasks, not summary tasks.");
        if (resource.IsBudget)
            throw new InvalidOperationException("Budget resources hold project budgets; set them with BudgetCosts or BudgetWork.");
        if (task.Assignments.Any(a => a.Resource == resource))
            throw new InvalidOperationException($"'{resource.Name}' is already assigned to '{task.Name}'.");

        var assignment = new Assignment(task, resource, units);
        if (resource.Type != ResourceType.Work)
        {
            task.AssignmentList.Add(assignment);
            return assignment;
        }

        var existing = WorkAssignments(task).ToList();
        task.AssignmentList.Add(assignment);

        if (existing.Count == 0 || !task.IsEffortDriven)
            assignment.WorkMinutes = WorkFor(assignment, task.DurationMinutes);
        else
            RedistributeWork(task, existing.Sum(a => a.WorkMinutes));
        return assignment;
    }

    /// <summary>Removes an assignment. On effort-driven tasks the remaining resources take over its work.</summary>
    public void Unassign(ProjectTask task, Resource resource)
    {
        var assignment = task.Assignments.FirstOrDefault(a => a.Resource == resource)
            ?? throw new InvalidOperationException($"'{resource.Name}' is not assigned to '{task.Name}'.");
        long totalWork = WorkAssignments(task).Sum(a => a.WorkMinutes);
        task.AssignmentList.Remove(assignment);
        if (assignment.IsWork && task.IsEffortDriven && WorkAssignments(task).Any())
            RedistributeWork(task, totalWork);
    }

    /// <summary>Changes a task's duration; work or units follow according to the task type.</summary>
    public void SetDuration(ProjectTask task, long durationMinutes)
    {
        if (task.IsSummary)
            throw new InvalidOperationException("A summary task's duration comes from its subtasks.");
        task.DurationMinutes = durationMinutes;
        foreach (var a in WorkAssignments(task))
        {
            if (task.Type == TaskType.FixedWork)
            {
                if (durationMinutes > 0)
                    a.Units = a.WorkMinutes / (durationMinutes * WorkContours.AverageFactor(a.Contour));
            }
            else
            {
                a.WorkMinutes = WorkFor(a, durationMinutes);
            }
        }
    }

    /// <summary>Changes an assignment's units; duration or work follow according to the task type.</summary>
    public void SetUnits(Assignment assignment, double units)
    {
        assignment.Units = units;
        if (!assignment.IsWork)
            return;
        if (assignment.Task.Type == TaskType.FixedDuration)
            assignment.WorkMinutes = WorkFor(assignment, assignment.Task.DurationMinutes);
        else
            UpdateDurationFromAssignments(assignment.Task);
    }

    /// <summary>Changes an assignment's work; duration or units follow according to the task type.</summary>
    public void SetWork(Assignment assignment, long workMinutes)
    {
        assignment.WorkMinutes = workMinutes;
        var task = assignment.Task;
        if (task.Type == TaskType.FixedDuration && task.DurationMinutes > 0)
            assignment.Units = workMinutes / (task.DurationMinutes * WorkContours.AverageFactor(assignment.Contour));
        else
            UpdateDurationFromAssignments(task);
    }

    private static IEnumerable<Assignment> WorkAssignments(ProjectTask task) =>
        task.Assignments.Where(a => a.IsWork);

    private static long WorkFor(Assignment assignment, long durationMinutes) =>
        (long)Math.Round(assignment.Units * durationMinutes * WorkContours.AverageFactor(assignment.Contour));

    private static void RedistributeWork(ProjectTask task, long totalWork)
    {
        var assignments = WorkAssignments(task).ToList();
        double totalUnits = assignments.Sum(a => a.Units * WorkContours.AverageFactor(a.Contour));
        if (totalUnits <= 0)
            return;

        if (task.Type == TaskType.FixedDuration)
        {
            // Duration stays; each resource's share of the work sets its units.
            foreach (var a in assignments)
            {
                double share = a.Units * WorkContours.AverageFactor(a.Contour) / totalUnits;
                a.WorkMinutes = (long)Math.Round(totalWork * share);
                if (task.DurationMinutes > 0)
                    a.Units = a.WorkMinutes / (task.DurationMinutes * WorkContours.AverageFactor(a.Contour));
            }
        }
        else
        {
            task.DurationMinutes = (long)Math.Round(totalWork / totalUnits);
            foreach (var a in assignments)
                a.WorkMinutes = WorkFor(a, task.DurationMinutes);
        }
    }

    private static void UpdateDurationFromAssignments(ProjectTask task)
    {
        var spans = WorkAssignments(task).Where(a => a.Units > 0).Select(a => a.SpanMinutes + a.DelayMinutes).ToList();
        if (spans.Count > 0)
            task.DurationMinutes = spans.Max();
    }

    // ------------------------------------------------------------------ baselines

    /// <summary>
    /// Saves the current schedule into a baseline (0 = Baseline, 1-10 = Baseline1-10) for the given tasks,
    /// or all tasks. With <paramref name="rollUpToSummaries"/>, summary tasks above the selected tasks get
    /// their baseline recalculated from their subtasks. Schedule the project first.
    /// </summary>
    public void SaveBaseline(int index = 0, IEnumerable<ProjectTask>? tasks = null, bool rollUpToSummaries = false)
    {
        RequireBaselineIndex(index);
        var savedOn = DateTime.Now;
        var selected = tasks?.ToList() ?? _tasks.ToList();
        foreach (var task in selected)
        {
            task.Baselines[index] = new BaselineValues(task.Start, task.Finish, task.DurationMinutes, task.WorkMinutes, task.Cost, savedOn);
            foreach (var a in task.Assignments)
                a.Baselines[index] = new BaselineValues(a.Start, a.Finish, 0, a.WorkMinutes, a.Cost, savedOn);
        }

        if (tasks is null)
        {
            Baselines[index] = new BaselineValues(Start, Finish, Calendar.WorkingMinutesBetween(Start, Finish), WorkMinutes, Cost, savedOn);
        }
        else if (rollUpToSummaries)
        {
            var ancestors = selected.SelectMany(Ancestors).Distinct().OrderByDescending(t => t.OutlineLevel);
            foreach (var summary in ancestors)
            {
                var children = summary.Children.Select(c => c.Baselines[index]).OfType<BaselineValues>().ToList();
                if (children.Count == 0)
                    continue;
                var start = children.Min(c => c.Start);
                var finish = children.Max(c => c.Finish);
                summary.Baselines[index] = new BaselineValues(
                    start, finish, Calendar.WorkingMinutesBetween(start, finish),
                    children.Sum(c => c.WorkMinutes), children.Sum(c => c.Cost) + summary.FixedCost, savedOn);
            }
        }
    }

    public void ClearBaseline(int index = 0, IEnumerable<ProjectTask>? tasks = null)
    {
        RequireBaselineIndex(index);
        foreach (var task in tasks ?? _tasks)
        {
            task.Baselines[index] = null;
            foreach (var a in task.Assignments)
                a.Baselines[index] = null;
        }
        if (tasks is null)
            Baselines[index] = null;
    }

    /// <summary>
    /// Saves current start/finish dates (or those of a baseline) into an interim plan (0-9 = Start1/Finish1 to Start10/Finish10).
    /// </summary>
    public void SaveInterimPlan(int index, IEnumerable<ProjectTask>? tasks = null, int? fromBaseline = null)
    {
        if (index < 0 || index >= Baseline.InterimPlanCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        foreach (var task in tasks ?? _tasks)
        {
            if (fromBaseline is { } b)
            {
                var values = task.Baselines[b];
                task.InterimPlans[index] = values is null ? null : new InterimPlan(values.Start, values.Finish);
            }
            else
            {
                task.InterimPlans[index] = new InterimPlan(task.Start, task.Finish);
            }
        }
    }

    private static IEnumerable<ProjectTask> Ancestors(ProjectTask task)
    {
        for (var p = task.Parent; p is not null; p = p.Parent)
            yield return p;
    }

    private static void RequireBaselineIndex(int index)
    {
        if (index < 0 || index >= Baseline.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Baseline index must be 0-10.");
    }

    // ------------------------------------------------------------------ hierarchy helpers

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
