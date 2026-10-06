using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

/// <summary>
/// Critical Path Method scheduler.
/// <para>
/// Links into a summary task apply to every leaf task beneath it. Links out of a summary task use the
/// summary's rolled-up dates in the forward pass, and constrain every leaf beneath it in the backward pass.
/// </para>
/// <para>
/// Constraints are always honoured (Microsoft Project's default), so a hard constraint that conflicts
/// with a link produces negative total float rather than moving the task.
/// </para>
/// </summary>
public static class ProjectScheduler
{
    public static void Schedule(Project project) => new Run(project).Execute();

    private readonly record struct Edge(ProjectTask Predecessor, ProjectTask Successor, LinkType Type, long LagMinutes);

    private sealed class Run
    {
        private readonly Project _project;
        private readonly ScheduleOptions _options;
        private readonly List<ProjectTask> _active;
        private readonly HashSet<ProjectTask> _activeSet;
        private readonly Dictionary<ProjectTask, List<ProjectTask>> _activeChildren = new();
        private readonly Dictionary<ProjectTask, List<Edge>> _incoming = new();     // leaf -> effective predecessors
        private readonly Dictionary<ProjectTask, List<Edge>> _outgoingDeclared = new(); // task -> links where it is predecessor (successor expanded to leaves)
        private DateTime _projectStart;
        private DateTime _projectFinish;

        public Run(Project project)
        {
            _project = project;
            _options = project.Options;
            project.RebuildHierarchy();
            _active = project.Tasks.Where(IsEffectivelyActive).ToList();
            _activeSet = _active.ToHashSet();
            foreach (var task in _active)
            {
                _activeChildren[task] = task.Children.Where(_activeSet.Contains).ToList();
                _incoming[task] = new List<Edge>();
                _outgoingDeclared[task] = new List<Edge>();
            }
        }

        public void Execute()
        {
            if (_active.Count == 0)
            {
                _project.Finish = _project.StartDate;
                return;
            }

            _projectStart = _project.Calendar.NextWorkingMoment(_project.StartDate);
            BuildEdges();
            var order = TopologicalOrder();

            foreach (var task in order)
            {
                if (IsLeaf(task))
                    ForwardLeaf(task);
                else
                    RollupEarly(task);
            }

            _projectFinish = _active.Where(IsLeaf).Max(t => t.EarlyFinish);

            for (int i = order.Count - 1; i >= 0; i--)
            {
                if (IsLeaf(order[i]))
                    BackwardLeaf(order[i]);
            }

            foreach (var task in _active.Where(IsLeaf))
                ComputeFloat(task, CalendarFor(task), OutgoingFor(task));

            for (int i = order.Count - 1; i >= 0; i--)
            {
                var task = order[i];
                if (IsLeaf(task) && task.ConstraintType == ConstraintType.AsLateAsPossible && IsMovable(task))
                    PlaceAsLateAsPossible(task);
            }

            // Children come after their parent in outline order, so walking backwards rolls up bottom-up.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (!IsLeaf(_active[i]))
                    RollupFinal(_active[i]);
            }

            foreach (var task in _active)
                task.MissesDeadline = task.Deadline is { } deadline && task.Finish > deadline;

            _project.Finish = _active.Max(t => t.Finish);
        }

        // ---------------------------------------------------------------- graph

        private bool IsEffectivelyActive(ProjectTask task)
        {
            for (var t = task; t is not null; t = t.Parent)
            {
                if (!t.IsActive)
                    return false;
            }
            return true;
        }

        private bool IsLeaf(ProjectTask task) => _activeChildren[task].Count == 0;

        private IEnumerable<ProjectTask> Leaves(ProjectTask task)
        {
            if (IsLeaf(task))
            {
                yield return task;
                yield break;
            }
            foreach (var child in _activeChildren[task])
            {
                foreach (var leaf in Leaves(child))
                    yield return leaf;
            }
        }

        private void BuildEdges()
        {
            foreach (var successor in _active)
            {
                foreach (var link in successor.Predecessors)
                {
                    var predecessor = _project.GetTask(link.PredecessorId);
                    if (predecessor is null || !_activeSet.Contains(predecessor))
                        continue;
                    if (predecessor == successor || Project.IsAncestor(predecessor, successor) || Project.IsAncestor(successor, predecessor))
                        throw new SchedulingException($"Task '{successor.Name}' is linked to its own summary or subtask '{predecessor.Name}'.", new[] { predecessor, successor });
                    if (!IsLeaf(successor) && link.Type is LinkType.FinishToFinish or LinkType.StartToFinish)
                        throw new SchedulingException($"Summary task '{successor.Name}' cannot be the successor of a {link.Type} link.", new[] { predecessor, successor });

                    foreach (var leaf in Leaves(successor))
                    {
                        var edge = new Edge(predecessor, leaf, link.Type, link.LagMinutes);
                        _incoming[leaf].Add(edge);
                        _outgoingDeclared[predecessor].Add(edge);
                    }
                }
            }
        }

        /// <summary>Effective successors of a leaf: its own links plus those of every summary above it.</summary>
        private List<Edge> OutgoingFor(ProjectTask leaf)
        {
            var edges = new List<Edge>(_outgoingDeclared[leaf]);
            for (var p = leaf.Parent; p is not null; p = p.Parent)
                edges.AddRange(_outgoingDeclared[p]);
            return edges;
        }

        /// <summary>
        /// Orders tasks so that every predecessor precedes its successors and every child precedes its
        /// summary. Ties keep outline order, so results are deterministic.
        /// </summary>
        private List<ProjectTask> TopologicalOrder()
        {
            var index = new Dictionary<ProjectTask, int>();
            for (int i = 0; i < _active.Count; i++)
                index[_active[i]] = i;

            var next = _active.ToDictionary(t => t, _ => new List<ProjectTask>());
            var inDegree = _active.ToDictionary(t => t, _ => 0);
            void AddEdge(ProjectTask from, ProjectTask to)
            {
                next[from].Add(to);
                inDegree[to]++;
            }

            foreach (var task in _active)
            {
                foreach (var edge in _incoming[task])
                    AddEdge(edge.Predecessor, task);
                if (task.Parent is { } parent && _activeSet.Contains(parent))
                    AddEdge(task, parent);
            }

            var ready = new SortedSet<int>(_active.Where(t => inDegree[t] == 0).Select(t => index[t]));
            var order = new List<ProjectTask>(_active.Count);
            while (ready.Count > 0)
            {
                var task = _active[ready.Min];
                ready.Remove(ready.Min);
                order.Add(task);
                foreach (var n in next[task])
                {
                    if (--inDegree[n] == 0)
                        ready.Add(index[n]);
                }
            }

            if (order.Count != _active.Count)
            {
                var loop = _active.Where(t => inDegree[t] > 0).ToList();
                throw new SchedulingException(
                    "The project contains a circular dependency involving: " + string.Join(", ", loop.Select(t => $"'{t.Name}'")),
                    loop);
            }
            return order;
        }

        // ---------------------------------------------------------------- forward pass

        private WorkingCalendar CalendarFor(ProjectTask task) => task.Calendar ?? _project.Calendar;

        private static bool IsMovable(ProjectTask task) =>
            task.Mode == TaskMode.AutoScheduled && task.ActualStart is null;

        private void ForwardLeaf(ProjectTask task)
        {
            var cal = CalendarFor(task);
            long duration = task.DurationMinutes;
            DateTime start, finish;

            if (task.Mode == TaskMode.ManuallyScheduled)
            {
                start = task.ManualStart ?? _projectStart;
                finish = task.ManualFinish ?? cal.AddWorkingMinutes(start, duration);
            }
            else if (task.ActualStart is { } actualStart)
            {
                start = actualStart;
                finish = task.ActualFinish ?? cal.AddWorkingMinutes(cal.NextWorkingMoment(actualStart), duration);
            }
            else
            {
                start = _projectStart;
                DateTime? finishNoEarlierThan = null;

                foreach (var edge in _incoming[task])
                {
                    var p = edge.Predecessor;
                    switch (edge.Type)
                    {
                        case LinkType.FinishToStart:
                            start = Max(start, cal.AddWorkingMinutes(p.EarlyFinish, edge.LagMinutes));
                            break;
                        case LinkType.StartToStart:
                            start = Max(start, cal.AddWorkingMinutes(p.EarlyStart, edge.LagMinutes));
                            break;
                        case LinkType.FinishToFinish:
                            finishNoEarlierThan = Max(finishNoEarlierThan, cal.AddWorkingMinutes(p.EarlyFinish, edge.LagMinutes));
                            break;
                        case LinkType.StartToFinish:
                            finishNoEarlierThan = Max(finishNoEarlierThan, cal.AddWorkingMinutes(p.EarlyStart, edge.LagMinutes));
                            break;
                    }
                }

                if (task.ConstraintDate is { } date)
                {
                    switch (task.ConstraintType)
                    {
                        case ConstraintType.StartNoEarlierThan:
                            start = Max(start, date);
                            break;
                        case ConstraintType.FinishNoEarlierThan:
                            finishNoEarlierThan = Max(finishNoEarlierThan, date);
                            break;
                        case ConstraintType.MustStartOn:
                            start = date;
                            finishNoEarlierThan = null;
                            break;
                        case ConstraintType.MustFinishOn:
                            start = StartFromFinish(cal, date, duration);
                            finishNoEarlierThan = null;
                            break;
                    }
                }

                if (finishNoEarlierThan is { } minFinish)
                    start = Max(start, StartFromFinish(cal, minFinish, duration));

                if (duration > 0)
                    start = cal.NextWorkingMoment(start);
                finish = cal.AddWorkingMinutes(start, duration);
            }

            task.EarlyStart = task.Start = start;
            task.EarlyFinish = task.Finish = finish;
        }

        private void RollupEarly(ProjectTask summary)
        {
            var children = _activeChildren[summary];
            summary.EarlyStart = children.Min(c => c.EarlyStart);
            summary.EarlyFinish = children.Max(c => c.EarlyFinish);
        }

        // ---------------------------------------------------------------- backward pass

        private void BackwardLeaf(ProjectTask task)
        {
            var cal = CalendarFor(task);
            long duration = task.DurationMinutes;
            var outgoing = OutgoingFor(task);

            DateTime finish = _options.CalculateMultipleCriticalPaths && outgoing.Count == 0
                ? task.EarlyFinish
                : _projectFinish;
            DateTime? startNoLaterThan = null;

            foreach (var edge in outgoing)
            {
                var s = edge.Successor;
                switch (edge.Type)
                {
                    case LinkType.FinishToStart:
                        finish = Min(finish, cal.AddWorkingMinutes(s.LateStart, -edge.LagMinutes));
                        break;
                    case LinkType.FinishToFinish:
                        finish = Min(finish, cal.AddWorkingMinutes(s.LateFinish, -edge.LagMinutes));
                        break;
                    case LinkType.StartToStart:
                        startNoLaterThan = Min(startNoLaterThan, cal.AddWorkingMinutes(s.LateStart, -edge.LagMinutes));
                        break;
                    case LinkType.StartToFinish:
                        startNoLaterThan = Min(startNoLaterThan, cal.AddWorkingMinutes(s.LateFinish, -edge.LagMinutes));
                        break;
                }
            }

            ApplyLateLimits(task, ref finish, ref startNoLaterThan);
            (task.LateStart, task.LateFinish) = LateDates(task, cal, duration, finish, startNoLaterThan);
        }

        private static void ApplyLateLimits(ProjectTask task, ref DateTime finish, ref DateTime? startNoLaterThan)
        {
            if (task.Deadline is { } deadline)
                finish = Min(finish, deadline);

            if (task.ConstraintDate is { } date)
            {
                if (task.ConstraintType == ConstraintType.StartNoLaterThan)
                    startNoLaterThan = Min(startNoLaterThan, date);
                else if (task.ConstraintType == ConstraintType.FinishNoLaterThan)
                    finish = Min(finish, date);
            }
        }

        private static (DateTime Start, DateTime Finish) LateDates(ProjectTask task, WorkingCalendar cal, long duration, DateTime finish, DateTime? startNoLaterThan)
        {
            if (task.ConstraintDate is { } date)
            {
                if (task.ConstraintType == ConstraintType.MustStartOn)
                {
                    var s = duration > 0 ? cal.NextWorkingMoment(date) : date;
                    return (s, cal.AddWorkingMinutes(s, duration));
                }
                if (task.ConstraintType == ConstraintType.MustFinishOn)
                {
                    var f = duration > 0 ? cal.PreviousWorkingMoment(date) : date;
                    return (cal.AddWorkingMinutes(f, -duration), f);
                }
            }

            if (startNoLaterThan is { } maxStart)
                finish = Min(finish, duration > 0 ? cal.AddWorkingMinutes(cal.NextWorkingMoment(maxStart), duration) : maxStart);
            if (duration > 0)
                finish = cal.PreviousWorkingMoment(finish);
            return (cal.AddWorkingMinutes(finish, -duration), finish);
        }

        // ---------------------------------------------------------------- float, ALAP, roll-up

        private void ComputeFloat(ProjectTask task, WorkingCalendar cal, List<Edge> outgoing)
        {
            long total = Math.Min(
                cal.WorkingMinutesBetween(task.EarlyStart, task.LateStart),
                cal.WorkingMinutesBetween(task.EarlyFinish, task.LateFinish));

            long free;
            if (outgoing.Count == 0)
            {
                free = _options.CalculateMultipleCriticalPaths ? 0 : cal.WorkingMinutesBetween(task.EarlyFinish, _projectFinish);
            }
            else
            {
                free = long.MaxValue;
                foreach (var edge in outgoing)
                {
                    var s = edge.Successor;
                    long slack = edge.Type switch
                    {
                        LinkType.FinishToStart => cal.WorkingMinutesBetween(cal.AddWorkingMinutes(task.EarlyFinish, edge.LagMinutes), s.EarlyStart),
                        LinkType.StartToStart => cal.WorkingMinutesBetween(cal.AddWorkingMinutes(task.EarlyStart, edge.LagMinutes), s.EarlyStart),
                        LinkType.FinishToFinish => cal.WorkingMinutesBetween(cal.AddWorkingMinutes(task.EarlyFinish, edge.LagMinutes), s.EarlyFinish),
                        _ => cal.WorkingMinutesBetween(cal.AddWorkingMinutes(task.EarlyStart, edge.LagMinutes), s.EarlyFinish),
                    };
                    free = Math.Min(free, slack);
                }
            }

            task.TotalFloatMinutes = total;
            task.FreeFloatMinutes = Math.Max(0, Math.Min(free, total));
            task.IsCritical = total <= _options.CriticalSlackLimitMinutes && task.PercentComplete < 100;
        }

        /// <summary>Delays an ALAP task as far as its successors' scheduled dates allow.</summary>
        private void PlaceAsLateAsPossible(ProjectTask task)
        {
            var cal = CalendarFor(task);
            long duration = task.DurationMinutes;
            DateTime finish = _projectFinish;
            DateTime? startNoLaterThan = null;

            foreach (var edge in OutgoingFor(task))
            {
                var s = edge.Successor;
                switch (edge.Type)
                {
                    case LinkType.FinishToStart:
                        finish = Min(finish, cal.AddWorkingMinutes(s.Start, -edge.LagMinutes));
                        break;
                    case LinkType.FinishToFinish:
                        finish = Min(finish, cal.AddWorkingMinutes(s.Finish, -edge.LagMinutes));
                        break;
                    case LinkType.StartToStart:
                        startNoLaterThan = Min(startNoLaterThan, cal.AddWorkingMinutes(s.Start, -edge.LagMinutes));
                        break;
                    case LinkType.StartToFinish:
                        startNoLaterThan = Min(startNoLaterThan, cal.AddWorkingMinutes(s.Finish, -edge.LagMinutes));
                        break;
                }
            }

            ApplyLateLimits(task, ref finish, ref startNoLaterThan);
            var (start, lateFinish) = LateDates(task, cal, duration, finish, startNoLaterThan);
            if (start > task.EarlyStart)
            {
                task.Start = start;
                task.Finish = lateFinish;
            }
        }

        private void RollupFinal(ProjectTask summary)
        {
            var children = _activeChildren[summary];
            var cal = _project.Calendar;

            summary.Start = children.Min(c => c.Start);
            summary.Finish = children.Max(c => c.Finish);
            summary.LateStart = children.Min(c => c.LateStart);
            summary.LateFinish = children.Max(c => c.LateFinish);
            summary.DurationMinutes = Math.Max(0, cal.WorkingMinutesBetween(summary.Start, summary.Finish));

            long totalDuration = children.Sum(c => c.DurationMinutes);
            summary.PercentComplete = totalDuration > 0
                ? children.Sum(c => c.DurationMinutes * c.PercentComplete) / totalDuration
                : children.Average(c => c.PercentComplete);

            ComputeFloat(summary, cal, _outgoingDeclared[summary]);
        }

        private static DateTime StartFromFinish(WorkingCalendar cal, DateTime finish, long duration) =>
            duration > 0 ? cal.AddWorkingMinutes(cal.PreviousWorkingMoment(finish), -duration) : finish;

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime? Max(DateTime? a, DateTime b) => a is null || b > a ? b : a;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime? Min(DateTime? a, DateTime b) => a is null || b < a ? b : a;
    }
}
