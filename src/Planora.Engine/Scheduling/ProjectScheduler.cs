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
/// <para>
/// Forward-scheduled projects run the forward pass from the project start, then the backward pass from
/// the resulting finish. Projects scheduled from their finish date run the backward pass first and take
/// the project start from it.
/// </para>
/// </summary>
public static class ProjectScheduler
{
    public static void Schedule(Project project) => new Run(project).Execute();

    private readonly record struct Edge(ProjectTask Predecessor, ProjectTask Successor, TaskLink Link);

    /// <summary>An in-progress task whose remaining work was moved to the status date.</summary>
    private readonly record struct Resumption(DateTime CompleteThrough, DateTime Resume, long GapMinutes);

    private sealed class Run
    {
        private static readonly WorkingCalendar Elapsed = WorkingCalendar.Create24Hours("Elapsed");

        private readonly Project _project;
        private readonly ScheduleOptions _options;
        private readonly List<ProjectTask> _active;
        private readonly HashSet<ProjectTask> _activeSet;
        private readonly Dictionary<ProjectTask, List<ProjectTask>> _activeChildren = new();
        private readonly Dictionary<ProjectTask, List<Edge>> _incoming = new();         // leaf -> effective predecessors
        private readonly Dictionary<ProjectTask, List<Edge>> _outgoingDeclared = new(); // task -> links where it is predecessor (successor expanded to leaves)
        private readonly Dictionary<ProjectTask, Resumption> _resumptions = new();
        private DateTime _projectStart;
        private DateTime _projectFinish;

        public Run(Project project)
        {
            _project = project;
            _options = project.Options;
            project.RebuildHierarchy();
            foreach (var a in project.Assignments)
            {
                a.EffectiveCalendar = null;
                a.Segments = Array.Empty<TaskSegment>();
            }
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
                _project.Start = _project.Finish = _project.ScheduleFromFinish ? _project.FinishDate : _project.StartDate;
                return;
            }

            BuildEdges();
            var order = TopologicalOrder();
            var leavesInOrder = order.Where(IsLeaf).ToList();

            if (_project.ScheduleFromFinish)
            {
                _projectFinish = _project.Calendar.PreviousWorkingMoment(_project.FinishDate);
                BackwardPass(leavesInOrder);
                _projectStart = leavesInOrder.Min(t => t.LateStart);
                ForwardPass(order);
            }
            else
            {
                _projectStart = _project.Calendar.NextWorkingMoment(_project.StartDate);
                ForwardPass(order);
                _projectFinish = leavesInOrder.Max(t => t.EarlyFinish);
                BackwardPass(leavesInOrder);
            }

            foreach (var task in leavesInOrder)
                ComputeFloat(task, CalendarFor(task), OutgoingFor(task));

            for (int i = leavesInOrder.Count - 1; i >= 0; i--)
            {
                var task = leavesInOrder[i];
                if (task.ConstraintType == ConstraintType.AsLateAsPossible && IsMovable(task))
                    PlaceAsLateAsPossible(task);
            }

            foreach (var task in leavesInOrder)
            {
                ComputeSegments(task);
                ComputeAssignments(task);
            }

            // Children come after their parent in outline order, so walking backwards rolls up bottom-up.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (!IsLeaf(_active[i]))
                    RollupFinal(_active[i]);
            }

            foreach (var task in _active)
                task.MissesDeadline = task.Deadline is { } deadline && task.Finish > deadline;

            _project.Start = _active.Min(t => t.Start);
            _project.Finish = _active.Max(t => t.Finish);

            ComputeCostsAndTracking();
            ResourceAnalysis.UpdateResourceTotals(_project);
        }

        private void ForwardPass(List<ProjectTask> order)
        {
            foreach (var task in order)
            {
                if (IsLeaf(task))
                    ForwardLeaf(task);
                else
                    RollupEarly(task);
            }
        }

        private void BackwardPass(List<ProjectTask> leavesInOrder)
        {
            for (int i = leavesInOrder.Count - 1; i >= 0; i--)
                BackwardLeaf(leavesInOrder[i]);
        }

        // ---------------------------------------------------------------- graph

        private static bool IsEffectivelyActive(ProjectTask task)
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
                        var edge = new Edge(predecessor, leaf, link);
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
            {
                if (_outgoingDeclared.TryGetValue(p, out var inherited))
                    edges.AddRange(inherited);
            }
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

        // ---------------------------------------------------------------- time helpers

        private WorkingCalendar CalendarFor(ProjectTask task) =>
            task.IsElapsedDuration ? Elapsed : task.Calendar ?? _project.Calendar;

        /// <summary>Working time from start to finish, including split gaps and any gap created by progress rescheduling.</summary>
        private long Span(ProjectTask task) =>
            task.SpanMinutes + (_resumptions.TryGetValue(task, out var r) ? r.GapMinutes : 0);

        /// <summary>Moves a moment by a link's lag (<paramref name="direction"/> = -1 to undo it in the backward pass).</summary>
        private DateTime ApplyLag(DateTime moment, Edge edge, int direction)
        {
            var link = edge.Link;
            long lag = link.LagFormat == LagFormat.PercentOfPredecessor
                ? (long)Math.Round(edge.Predecessor.DurationMinutes * link.LagPercent / 100.0)
                : link.LagMinutes;
            var cal = link.LagFormat == LagFormat.ElapsedTime ? Elapsed : _project.Calendar;
            return cal.AddWorkingMinutes(moment, direction * lag);
        }

        private static bool IsMovable(ProjectTask task) =>
            task.Mode == TaskMode.AutoScheduled && task.ActualStart is null;

        // ---------------------------------------------------------------- assignments

        private readonly Dictionary<(WorkingCalendar, WorkingCalendar), WorkingCalendar> _intersections = new();

        /// <summary>A work assignment that determines how long the task takes.</summary>
        private static bool IsDriving(Assignment a) => a.IsWork && a.Units > 0;

        /// <summary>
        /// Auto-scheduled, unstarted tasks with working resources take their dates from those resources.
        /// Split tasks keep their duration-based shape.
        /// </summary>
        private static bool IsResourceDriven(ProjectTask task) =>
            IsMovable(task) && task.Splits.Count == 0 && task.Assignments.Any(IsDriving);

        private WorkingCalendar AssignmentCalendar(Assignment a)
        {
            var task = a.Task;
            if (task.IsElapsedDuration)
                return Elapsed;
            var resourceCalendar = a.Resource.Calendar ?? _project.Calendar;
            if (task.Calendar is null)
                return resourceCalendar;
            if (task.IgnoreResourceCalendar)
                return task.Calendar;
            var key = (task.Calendar, resourceCalendar);
            if (!_intersections.TryGetValue(key, out var combined))
                _intersections[key] = combined = WorkingCalendar.Intersect(task.Calendar, resourceCalendar);
            return combined;
        }

        private (DateTime Start, DateTime Finish) AssignmentDates(Assignment a, DateTime taskStart)
        {
            var cal = AssignmentCalendar(a);
            var start = cal.NextWorkingMoment(cal.AddWorkingMinutes(taskStart, a.DelayMinutes));
            return (start, cal.AddWorkingMinutes(start, a.SpanMinutes));
        }

        private void ComputeAssignments(ProjectTask task)
        {
            bool resourceDriven = IsResourceDriven(task);
            foreach (var a in task.Assignments)
            {
                a.EffectiveCalendar = AssignmentCalendar(a);
                if (resourceDriven && IsDriving(a))
                {
                    (a.Start, a.Finish) = AssignmentDates(a, task.Start);
                    a.Segments = new[] { new TaskSegment(a.Start, a.Finish) };
                }
                else
                {
                    (a.Start, a.Finish) = (task.Start, task.Finish);
                    a.Segments = task.Segments;
                }
            }

            if (resourceDriven)
            {
                var driving = task.Assignments.Where(IsDriving).ToList();
                task.Start = driving.Min(a => a.Start);
                task.Finish = driving.Max(a => a.Finish);
                task.Segments = new[] { new TaskSegment(task.Start, task.Finish) };
            }
        }

        // ---------------------------------------------------------------- cost, variance, earned value

        private void ComputeCostsAndTracking()
        {
            var statusDate = _project.StatusDate ?? DateTime.Now;

            foreach (var task in _active.Where(IsLeaf))
            {
                long work = 0, actualWork = 0;
                decimal cost = task.FixedCost, actualCost = Costing.Accrue(task.FixedCost, task.FixedCostAccrual, task.PercentComplete);
                foreach (var a in task.Assignments)
                {
                    Costing.Calculate(a);
                    if (a.IsWork)
                    {
                        work += a.WorkMinutes;
                        actualWork += a.ActualWorkMinutes;
                    }
                    cost += a.Cost;
                    actualCost += a.ActualCost;
                }
                task.WorkMinutes = work;
                task.ActualWorkMinutes = actualWork;
                task.Cost = cost;
                task.ActualCost = actualCost;
                ComputeVariance(task, CalendarFor(task));
                task.EarnedValue = LeafEarnedValue(task, CalendarFor(task), statusDate);
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var summary = _active[i];
                if (IsLeaf(summary))
                    continue;
                var children = _activeChildren[summary];
                summary.WorkMinutes = children.Sum(c => c.WorkMinutes);
                summary.ActualWorkMinutes = children.Sum(c => c.ActualWorkMinutes);
                summary.Cost = children.Sum(c => c.Cost) + summary.FixedCost;
                summary.ActualCost = children.Sum(c => c.ActualCost) + Costing.Accrue(summary.FixedCost, summary.FixedCostAccrual, summary.PercentComplete);
                summary.EarnedValue = children.Aggregate(EarnedValue.Empty, (sum, c) => sum + c.EarnedValue);
                ComputeVariance(summary, _project.Calendar);
            }

            var topLevel = _active.Where(t => t.Parent is null || !_activeSet.Contains(t.Parent)).ToList();
            _project.WorkMinutes = topLevel.Sum(t => t.WorkMinutes);
            _project.Cost = topLevel.Sum(t => t.Cost);
            _project.ActualCost = topLevel.Sum(t => t.ActualCost);
            _project.EarnedValue = topLevel.Aggregate(EarnedValue.Empty, (sum, t) => sum + t.EarnedValue);
        }

        private static void ComputeVariance(ProjectTask task, WorkingCalendar cal)
        {
            if (task.Baselines[0] is not { } b)
            {
                task.StartVarianceMinutes = task.FinishVarianceMinutes = task.DurationVarianceMinutes = task.WorkVarianceMinutes = 0;
                task.CostVariance = 0;
                return;
            }
            task.StartVarianceMinutes = cal.WorkingMinutesBetween(b.Start, task.Start);
            task.FinishVarianceMinutes = cal.WorkingMinutesBetween(b.Finish, task.Finish);
            task.DurationVarianceMinutes = task.DurationMinutes - b.DurationMinutes;
            task.WorkVarianceMinutes = task.WorkMinutes - b.WorkMinutes;
            task.CostVariance = task.Cost - b.Cost;
        }

        private static EarnedValue LeafEarnedValue(ProjectTask task, WorkingCalendar cal, DateTime statusDate)
        {
            if (task.Baselines[0] is not { } b)
                return new EarnedValue(0, 0, task.ActualCost, 0);

            decimal plannedFraction;
            if (statusDate >= b.Finish)
                plannedFraction = 1;
            else if (statusDate <= b.Start)
                plannedFraction = 0;
            else
            {
                long total = cal.WorkingMinutesBetween(b.Start, b.Finish);
                plannedFraction = total > 0 ? (decimal)cal.WorkingMinutesBetween(b.Start, statusDate) / total : 0;
            }

            return new EarnedValue(
                Bcws: b.Cost * plannedFraction,
                Bcwp: b.Cost * (decimal)task.PercentComplete / 100m,
                Acwp: task.ActualCost,
                Bac: b.Cost);
        }

        // ---------------------------------------------------------------- forward pass

        private void ForwardLeaf(ProjectTask task)
        {
            var cal = CalendarFor(task);
            DateTime start, finish;

            if (task.Mode == TaskMode.ManuallyScheduled)
            {
                start = task.ManualStart ?? _projectStart;
                finish = task.ManualFinish ?? cal.AddWorkingMinutes(start, Span(task));
            }
            else if (task.ActualStart is { } actualStart)
            {
                start = actualStart;
                finish = task.ActualFinish ?? ScheduleInProgress(task, cal, actualStart);
            }
            else
            {
                long span = Span(task);
                start = _projectStart;
                DateTime? finishNoEarlierThan = null;

                foreach (var edge in _incoming[task])
                {
                    var p = edge.Predecessor;
                    switch (edge.Link.Type)
                    {
                        case LinkType.FinishToStart:
                            start = Max(start, ApplyLag(p.EarlyFinish, edge, 1));
                            break;
                        case LinkType.StartToStart:
                            start = Max(start, ApplyLag(p.EarlyStart, edge, 1));
                            break;
                        case LinkType.FinishToFinish:
                            finishNoEarlierThan = Max(finishNoEarlierThan, ApplyLag(p.EarlyFinish, edge, 1));
                            break;
                        case LinkType.StartToFinish:
                            finishNoEarlierThan = Max(finishNoEarlierThan, ApplyLag(p.EarlyStart, edge, 1));
                            break;
                    }
                }

                if (_options.RescheduleUncompletedWorkToStatusDate && _project.StatusDate is { } statusDate && task.PercentComplete == 0)
                    start = Max(start, statusDate);

                if (task.LevelingDelayMinutes > 0)
                    start = Elapsed.AddWorkingMinutes(start, task.LevelingDelayMinutes);

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
                            start = StartFromFinish(cal, date, span);
                            finishNoEarlierThan = null;
                            break;
                    }
                }

                if (finishNoEarlierThan is { } minFinish)
                    start = Max(start, StartFromFinish(cal, minFinish, span));

                if (span > 0)
                    start = cal.NextWorkingMoment(start);
                finish = cal.AddWorkingMinutes(start, span);

                if (IsResourceDriven(task))
                {
                    // Work drives the dates: each resource works its hours on its own calendar.
                    var taskStart = start;
                    (start, finish) = (DateTime.MaxValue, DateTime.MinValue);
                    foreach (var a in task.Assignments.Where(IsDriving))
                    {
                        var (aStart, aFinish) = AssignmentDates(a, taskStart);
                        start = Min(start, aStart);
                        finish = Max(finish, aFinish);
                    }
                    task.DurationMinutes = Math.Max(0, cal.WorkingMinutesBetween(start, finish));
                }
            }

            task.EarlyStart = task.Start = start;
            task.EarlyFinish = task.Finish = finish;
        }

        /// <summary>Finish of a started, unfinished task; may split off its remaining work to the status date.</summary>
        private DateTime ScheduleInProgress(ProjectTask task, WorkingCalendar cal, DateTime actualStart)
        {
            long span = task.SpanMinutes;
            var workStart = span > 0 ? cal.NextWorkingMoment(actualStart) : actualStart;
            var finish = cal.AddWorkingMinutes(workStart, span);

            if (_options.RescheduleUncompletedWorkToStatusDate && _project.StatusDate is { } statusDate && task.RemainingDurationMinutes > 0 && task.Splits.Count == 0)
            {
                var completeThrough = cal.AddWorkingMinutes(workStart, task.ActualDurationMinutes);
                var resume = cal.NextWorkingMoment(statusDate);
                long gap = cal.WorkingMinutesBetween(completeThrough, resume);
                if (gap > 0)
                {
                    _resumptions[task] = new Resumption(completeThrough, resume, gap);
                    finish = cal.AddWorkingMinutes(resume, task.RemainingDurationMinutes);
                }
            }
            return finish;
        }

        private void RollupEarly(ProjectTask summary)
        {
            var children = _activeChildren[summary];
            summary.EarlyStart = children.Min(c => c.EarlyStart);
            summary.EarlyFinish = children.Max(c => c.EarlyFinish);
            summary.DurationMinutes = Math.Max(0, _project.Calendar.WorkingMinutesBetween(summary.EarlyStart, summary.EarlyFinish));
        }

        // ---------------------------------------------------------------- backward pass

        private void BackwardLeaf(ProjectTask task)
        {
            var cal = CalendarFor(task);
            var outgoing = OutgoingFor(task);

            DateTime finish = _options.CalculateMultipleCriticalPaths && outgoing.Count == 0 && !_project.ScheduleFromFinish
                ? task.EarlyFinish
                : _projectFinish;
            DateTime? startNoLaterThan = null;

            foreach (var edge in outgoing)
            {
                var s = edge.Successor;
                switch (edge.Link.Type)
                {
                    case LinkType.FinishToStart:
                        finish = Min(finish, ApplyLag(s.LateStart, edge, -1));
                        break;
                    case LinkType.FinishToFinish:
                        finish = Min(finish, ApplyLag(s.LateFinish, edge, -1));
                        break;
                    case LinkType.StartToStart:
                        startNoLaterThan = Min(startNoLaterThan, ApplyLag(s.LateStart, edge, -1));
                        break;
                    case LinkType.StartToFinish:
                        startNoLaterThan = Min(startNoLaterThan, ApplyLag(s.LateFinish, edge, -1));
                        break;
                }
            }

            ApplyLateLimits(task, ref finish, ref startNoLaterThan);
            (task.LateStart, task.LateFinish) = LateDates(task, cal, Span(task), finish, startNoLaterThan);
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

        private static (DateTime Start, DateTime Finish) LateDates(ProjectTask task, WorkingCalendar cal, long span, DateTime finish, DateTime? startNoLaterThan)
        {
            if (task.ConstraintDate is { } date)
            {
                if (task.ConstraintType == ConstraintType.MustStartOn)
                {
                    var s = span > 0 ? cal.NextWorkingMoment(date) : date;
                    return (s, cal.AddWorkingMinutes(s, span));
                }
                if (task.ConstraintType == ConstraintType.MustFinishOn)
                {
                    var f = span > 0 ? cal.PreviousWorkingMoment(date) : date;
                    return (cal.AddWorkingMinutes(f, -span), f);
                }
            }

            if (startNoLaterThan is { } maxStart)
                finish = Min(finish, span > 0 ? cal.AddWorkingMinutes(cal.NextWorkingMoment(maxStart), span) : maxStart);
            if (span > 0)
                finish = cal.PreviousWorkingMoment(finish);
            return (cal.AddWorkingMinutes(finish, -span), finish);
        }

        // ---------------------------------------------------------------- float, ALAP, segments, roll-up

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
                    long slack = edge.Link.Type switch
                    {
                        LinkType.FinishToStart => cal.WorkingMinutesBetween(ApplyLag(task.EarlyFinish, edge, 1), s.EarlyStart),
                        LinkType.StartToStart => cal.WorkingMinutesBetween(ApplyLag(task.EarlyStart, edge, 1), s.EarlyStart),
                        LinkType.FinishToFinish => cal.WorkingMinutesBetween(ApplyLag(task.EarlyFinish, edge, 1), s.EarlyFinish),
                        _ => cal.WorkingMinutesBetween(ApplyLag(task.EarlyStart, edge, 1), s.EarlyFinish),
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
            DateTime finish = _projectFinish;
            DateTime? startNoLaterThan = null;

            foreach (var edge in OutgoingFor(task))
            {
                var s = edge.Successor;
                switch (edge.Link.Type)
                {
                    case LinkType.FinishToStart:
                        finish = Min(finish, ApplyLag(s.Start, edge, -1));
                        break;
                    case LinkType.FinishToFinish:
                        finish = Min(finish, ApplyLag(s.Finish, edge, -1));
                        break;
                    case LinkType.StartToStart:
                        startNoLaterThan = Min(startNoLaterThan, ApplyLag(s.Start, edge, -1));
                        break;
                    case LinkType.StartToFinish:
                        startNoLaterThan = Min(startNoLaterThan, ApplyLag(s.Finish, edge, -1));
                        break;
                }
            }

            ApplyLateLimits(task, ref finish, ref startNoLaterThan);
            var (start, lateFinish) = LateDates(task, cal, Span(task), finish, startNoLaterThan);
            if (start > task.EarlyStart)
            {
                task.Start = start;
                task.Finish = lateFinish;
            }
        }

        private void ComputeSegments(ProjectTask task)
        {
            var cal = CalendarFor(task);
            var segments = new List<TaskSegment>();

            if (_resumptions.TryGetValue(task, out var r))
            {
                if (r.CompleteThrough > task.Start)
                    segments.Add(new TaskSegment(task.Start, r.CompleteThrough));
                segments.Add(new TaskSegment(r.Resume, task.Finish));
                task.Segments = segments;
                task.CompleteThrough = r.CompleteThrough;
                return;
            }

            long duration = task.DurationMinutes;
            var splits = task.Splits.Where(s => s.AfterWorkMinutes < duration).ToList();
            if (duration == 0 || splits.Count == 0 || task.Mode == TaskMode.ManuallyScheduled && task.ManualFinish is not null)
            {
                segments.Add(new TaskSegment(task.Start, task.Finish));
            }
            else
            {
                var cursor = task.Start;
                long done = 0;
                foreach (var split in splits)
                {
                    var segmentStart = cal.NextWorkingMoment(cursor);
                    var segmentEnd = cal.AddWorkingMinutes(segmentStart, split.AfterWorkMinutes - done);
                    segments.Add(new TaskSegment(segmentStart, segmentEnd));
                    cursor = cal.AddWorkingMinutes(segmentEnd, split.GapMinutes);
                    done = split.AfterWorkMinutes;
                }
                segments.Add(new TaskSegment(cal.NextWorkingMoment(cursor), task.Finish));
            }

            task.Segments = segments;
            task.CompleteThrough = CompleteThrough(task, cal, segments);
        }

        private static DateTime CompleteThrough(ProjectTask task, WorkingCalendar cal, List<TaskSegment> segments)
        {
            if (task.PercentComplete <= 0)
                return task.Start;
            if (task.PercentComplete >= 100)
                return task.Finish;

            long remaining = task.ActualDurationMinutes;
            foreach (var segment in segments)
            {
                long length = cal.WorkingMinutesBetween(segment.Start, segment.Finish);
                if (remaining <= length)
                    return cal.AddWorkingMinutes(cal.NextWorkingMoment(segment.Start), remaining);
                remaining -= length;
            }
            return task.Finish;
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

            // Actual dates roll up: started when any subtask started, finished when all have finished.
            var started = children.Where(c => c.ActualStart is not null).ToList();
            summary.ActualStart = started.Count > 0 ? started.Min(c => c.ActualStart) : null;
            summary.ActualFinish = children.All(c => c.ActualFinish is not null) ? children.Max(c => c.ActualFinish) : null;

            summary.Segments = new[] { new TaskSegment(summary.Start, summary.Finish) };
            summary.CompleteThrough = summary.PercentComplete switch
            {
                <= 0 => summary.Start,
                >= 100 => summary.Finish,
                _ => cal.AddWorkingMinutes(cal.NextWorkingMoment(summary.Start), summary.ActualDurationMinutes),
            };

            ComputeFloat(summary, cal, _outgoingDeclared[summary]);
        }

        private static DateTime StartFromFinish(WorkingCalendar cal, DateTime finish, long span) =>
            span > 0 ? cal.AddWorkingMinutes(cal.PreviousWorkingMoment(finish), -span) : finish;

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime? Max(DateTime? a, DateTime b) => a is null || b > a ? b : a;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime? Min(DateTime? a, DateTime b) => a is null || b < a ? b : a;
    }
}
