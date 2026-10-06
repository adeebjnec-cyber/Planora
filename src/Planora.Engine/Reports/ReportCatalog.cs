using System.Globalization;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Reports;

/// <summary>The built-in reports, like Microsoft Project's dashboards and visual reports.</summary>
public static class ReportCatalog
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "Project Overview",
        "Critical Tasks",
        "Cost Overview",
        "Earned Value",
        "Resource Overview",
        "Task Status",
        "Milestones",
        "Who Does What",
    };

    public static Report Build(Project project, string name)
    {
        ProjectScheduler.Schedule(project);
        var context = new Context(project);
        var blocks = name switch
        {
            "Critical Tasks" => context.CriticalTasks(),
            "Cost Overview" => context.CostOverview(),
            "Earned Value" => context.EarnedValueReport(),
            "Resource Overview" => context.ResourceOverview(),
            "Task Status" => context.TaskStatus(),
            "Milestones" => context.Milestones(),
            "Who Does What" => context.WhoDoesWhat(),
            _ => context.ProjectOverview(),
        };
        var subtitle = $"{project.Name}  ·  {DateTime.Now.ToString("D", CultureInfo.CurrentCulture)}" +
                       (project.StatusDate is { } s ? $"  ·  Status date {Date(s)}" : "");
        return new Report(Names.Contains(name) ? name : Names[0], subtitle, blocks);
    }

    private static string Date(DateTime? d) => d is { } v ? v.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) : "NA";

    private sealed class Context
    {
        private readonly Project _p;
        private readonly ScheduleOptions _o;
        private readonly DateTime _statusDate;
        private readonly List<ProjectTask> _leaves;
        private readonly List<ProjectTask> _topLevel;

        public Context(Project project)
        {
            _p = project;
            _o = project.Options;
            _statusDate = project.StatusDate ?? DateTime.Now;
            _leaves = project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
            _topLevel = project.Tasks.Where(t => t.Parent is null && t.IsActive).ToList();
        }

        private string Dur(long minutes) => DurationText.Format(minutes, _o);
        private string Work(long minutes) => DurationText.Format(minutes, _o, DurationUnit.Hours);
        private static string Money(decimal value) => value.ToString("C", CultureInfo.CurrentCulture);
        private static string Percent(double value) => value.ToString("0", CultureInfo.CurrentCulture) + "%";
        private string Status(ProjectTask t) => TaskFields.Get(_p, "Status").Format(_p, t);
        private bool IsLate(ProjectTask t) => Status(t) == "Late";
        private static bool IsMilestone(ProjectTask t) => !t.IsSummary && (t.IsMilestone || t.DurationMinutes == 0);

        private double ProjectPercent()
        {
            long total = _leaves.Sum(t => t.DurationMinutes);
            return total > 0 ? _leaves.Sum(t => t.DurationMinutes * t.PercentComplete) / total : _leaves.Select(t => t.PercentComplete).DefaultIfEmpty().Average();
        }

        private TableBlock TaskTable(IEnumerable<ProjectTask> tasks, bool alertLate = true)
        {
            var list = tasks.ToList();
            return new TableBlock(
                new[] { "Task", "Start", "Finish", "Duration", "% Complete", "Resources" },
                list.Select(t => (IReadOnlyList<string>)new[] { t.Name, Date(t.Start), Date(t.Finish), Dur(t.DurationMinutes), Percent(t.PercentComplete), ResourceNamesText.Format(t) }).ToList(),
                alertLate ? list.Select(IsLate).ToList() : null);
        }

        public List<ReportBlock> ProjectOverview()
        {
            var blocks = new List<ReportBlock>
            {
                new FiguresBlock(new[]
                {
                    ("Start", Date(_p.Start)),
                    ("Finish", Date(_p.Finish)),
                    ("Duration", Dur(_p.Calendar.WorkingMinutesBetween(_p.Start, _p.Finish))),
                    ("% Complete", Percent(ProjectPercent())),
                    ("Work", Work(_p.WorkMinutes)),
                    ("Cost", Money(_p.Cost)),
                    ("Baseline finish", Date(_p.Baselines[0]?.Finish)),
                    ("Late tasks", _leaves.Count(IsLate).ToString(CultureInfo.CurrentCulture)),
                }),
            };
            if (_topLevel.Count > 0)
            {
                blocks.Add(new BarChartBlock("% complete by phase",
                    _topLevel.Select(t => new ChartBar(t.Name, t.PercentComplete, Percent(t.PercentComplete))).ToList(),
                    "% Complete", Maximum: 100));
            }
            var upcoming = _leaves.Where(IsMilestone).Where(t => t.PercentComplete < 100).OrderBy(t => t.Finish).Take(10).ToList();
            blocks.Add(new HeadingBlock("Upcoming milestones"));
            blocks.Add(upcoming.Count == 0 ? new ParagraphBlock("No milestones remaining.")
                : new TableBlock(new[] { "Milestone", "Date", "Status" },
                    upcoming.Select(m => (IReadOnlyList<string>)new[] { m.Name, Date(m.Finish), Status(m) }).ToList(),
                    upcoming.Select(m => IsLate(m) || m.MissesDeadline).ToList()));
            var late = _leaves.Where(IsLate).ToList();
            blocks.Add(new HeadingBlock("Late tasks"));
            blocks.Add(late.Count == 0 ? new ParagraphBlock("No tasks are late.") : TaskTable(late));
            return blocks;
        }

        public List<ReportBlock> CriticalTasks()
        {
            var critical = _leaves.Where(t => t.IsCritical && t.PercentComplete < 100).ToList();
            return new List<ReportBlock>
            {
                new ParagraphBlock("Incomplete tasks on the critical path: delaying any of them delays the project finish."),
                critical.Count == 0 ? new ParagraphBlock("There are no incomplete critical tasks.") : new TableBlock(
                    new[] { "Task", "Start", "Finish", "Remaining", "Total Slack", "Resources" },
                    critical.Select(t => (IReadOnlyList<string>)new[]
                    {
                        t.Name, Date(t.Start), Date(t.Finish), Dur(t.RemainingDurationMinutes), Dur(t.TotalFloatMinutes), ResourceNamesText.Format(t),
                    }).ToList(),
                    critical.Select(t => t.TotalFloatMinutes < 0 || IsLate(t)).ToList()),
            };
        }

        public List<ReportBlock> CostOverview()
        {
            decimal baseline = _p.Baselines[0]?.Cost ?? 0;
            var blocks = new List<ReportBlock>
            {
                new FiguresBlock(new[]
                {
                    ("Cost", Money(_p.Cost)),
                    ("Baseline cost", _p.Baselines[0] is null ? "NA" : Money(baseline)),
                    ("Variance", _p.Baselines[0] is null ? "NA" : Money(_p.Cost - baseline)),
                    ("Actual cost", Money(_p.ActualCost)),
                    ("Remaining cost", Money(_p.Cost - _p.ActualCost)),
                    ("Budget", _p.BudgetCosts.Count == 0 ? "NA" : Money(_p.BudgetCosts.Values.Sum())),
                }),
            };
            if (_topLevel.Count > 0)
            {
                blocks.Add(new BarChartBlock("Cost by phase",
                    _topLevel.Select(t => new ChartBar(t.Name, (double)t.Cost, Money(t.Cost), t.Baselines[0] is { } b ? (double)b.Cost : null)).ToList(),
                    "Cost", _p.Baselines[0] is null ? null : "Baseline"));
            }
            var rows = _p.Tasks.Where(t => t.IsActive).ToList();
            blocks.Add(new HeadingBlock("Task costs"));
            blocks.Add(new TableBlock(
                new[] { "Task", "Fixed Cost", "Cost", "Baseline", "Variance", "Actual", "Remaining" },
                rows.Select(t => (IReadOnlyList<string>)new[]
                {
                    new string(' ', (t.OutlineLevel - 1) * 3) + t.Name, Money(t.FixedCost), Money(t.Cost),
                    t.Baselines[0] is { } b ? Money(b.Cost) : "NA", t.Baselines[0] is null ? "" : Money(t.CostVariance),
                    Money(t.ActualCost), Money(t.RemainingCost),
                }).ToList(),
                rows.Select(t => t.CostVariance > 0).ToList()));
            return blocks;
        }

        public List<ReportBlock> EarnedValueReport()
        {
            var ev = _p.EarnedValue;
            string Index(decimal? value) => value is { } v ? v.ToString("0.00", CultureInfo.CurrentCulture) : "NA";
            var blocks = new List<ReportBlock>();
            if (_p.Baselines[0] is null && _leaves.All(t => t.Baselines[0] is null))
                blocks.Add(new ParagraphBlock("Earned value needs a baseline: use Project > Set Baseline first."));
            blocks.Add(new FiguresBlock(new[]
            {
                ("Planned value (BCWS)", Money(ev.Bcws)),
                ("Earned value (BCWP)", Money(ev.Bcwp)),
                ("Actual cost (ACWP)", Money(ev.Acwp)),
                ("Schedule variance (SV)", Money(ev.Sv)),
                ("Cost variance (CV)", Money(ev.Cv)),
                ("SPI", Index(ev.Spi)),
                ("CPI", Index(ev.Cpi)),
                ("Budget at completion (BAC)", Money(ev.Bac)),
                ("Estimate at completion (EAC)", Money(ev.Eac)),
                ("Variance at completion (VAC)", Money(ev.Vac)),
            }));
            blocks.Add(new HeadingBlock("By phase"));
            blocks.Add(new TableBlock(
                new[] { "Task", "BCWS", "BCWP", "ACWP", "SV", "CV", "EAC" },
                _topLevel.Select(t => (IReadOnlyList<string>)new[]
                {
                    t.Name, Money(t.EarnedValue.Bcws), Money(t.EarnedValue.Bcwp), Money(t.EarnedValue.Acwp),
                    Money(t.EarnedValue.Sv), Money(t.EarnedValue.Cv), Money(t.EarnedValue.Eac),
                }).ToList(),
                _topLevel.Select(t => t.EarnedValue.Sv < 0 || t.EarnedValue.Cv < 0).ToList()));
            return blocks;
        }

        public List<ReportBlock> ResourceOverview()
        {
            var resources = _p.Resources.Where(r => !r.IsBudget).ToList();
            var blocks = new List<ReportBlock>();
            var workers = resources.Where(r => r.Type == ResourceType.Work).ToList();
            if (workers.Count > 0)
            {
                blocks.Add(new BarChartBlock("Work by resource",
                    workers.Select(r => new ChartBar(r.Name, r.WorkMinutes / 60.0, Work(r.WorkMinutes),
                        r.ActualCost > 0 ? ResourceAnalysis.ScheduledAssignments(_p, r).Sum(a => a.ActualWorkMinutes) / 60.0 : null)).ToList(),
                    "Work", "Actual work"));
            }
            blocks.Add(new HeadingBlock("Resources"));
            blocks.Add(new TableBlock(
                new[] { "Resource", "Type", "Max Units", "Peak", "Work", "Cost", "Remaining Cost" },
                resources.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Name, r.Type.ToString(), r.Type == ResourceType.Work ? Percent(r.MaxUnits * 100) : "",
                    r.Type == ResourceType.Work ? Percent(r.PeakUnits * 100) : "", r.Type == ResourceType.Work ? Work(r.WorkMinutes) : "",
                    Money(r.Cost), Money(r.Cost - r.ActualCost),
                }).ToList(),
                resources.Select(r => r.IsOverallocated).ToList()));
            var overallocations = ResourceAnalysis.FindOverallocations(_p);
            blocks.Add(new HeadingBlock("Over-allocations"));
            blocks.Add(overallocations.Count == 0 ? new ParagraphBlock("No resource is over-allocated.") : new TableBlock(
                new[] { "Resource", "From", "To", "Peak Units" },
                overallocations.Select(o => (IReadOnlyList<string>)new[] { o.Resource.Name, Date(o.From), Date(o.To), Percent(o.PeakUnits * 100) }).ToList(),
                overallocations.Select(_ => true).ToList()));
            return blocks;
        }

        public List<ReportBlock> TaskStatus()
        {
            var complete = _leaves.Where(t => t.PercentComplete >= 100).ToList();
            var inProgress = _leaves.Where(t => t.PercentComplete is > 0 and < 100).ToList();
            var notStarted = _leaves.Where(t => t.PercentComplete == 0).ToList();
            var late = _leaves.Where(IsLate).ToList();
            var upcoming = notStarted.Where(t => t.Start >= _statusDate.Date && t.Start < _statusDate.Date.AddDays(14)).OrderBy(t => t.Start).ToList();
            return new List<ReportBlock>
            {
                new FiguresBlock(new[]
                {
                    ("Complete", complete.Count.ToString(CultureInfo.CurrentCulture)),
                    ("In progress", inProgress.Count.ToString(CultureInfo.CurrentCulture)),
                    ("Not started", notStarted.Count.ToString(CultureInfo.CurrentCulture)),
                    ("Late", late.Count.ToString(CultureInfo.CurrentCulture)),
                }),
                new BarChartBlock("Tasks by status", new[]
                {
                    new ChartBar("Complete", complete.Count, complete.Count.ToString(CultureInfo.CurrentCulture)),
                    new ChartBar("In progress", inProgress.Count, inProgress.Count.ToString(CultureInfo.CurrentCulture)),
                    new ChartBar("Not started", notStarted.Count, notStarted.Count.ToString(CultureInfo.CurrentCulture)),
                    new ChartBar("Late", late.Count, late.Count.ToString(CultureInfo.CurrentCulture)),
                }, "Tasks"),
                new HeadingBlock("Late tasks"),
                late.Count == 0 ? new ParagraphBlock("No tasks are late.") : TaskTable(late),
                new HeadingBlock("In progress"),
                inProgress.Count == 0 ? new ParagraphBlock("No tasks are in progress.") : TaskTable(inProgress),
                new HeadingBlock("Starting in the next two weeks"),
                upcoming.Count == 0 ? new ParagraphBlock("No tasks start in the next two weeks.") : TaskTable(upcoming, alertLate: false),
            };
        }

        public List<ReportBlock> Milestones()
        {
            var milestones = _leaves.Where(IsMilestone).OrderBy(t => t.Finish).ToList();
            return new List<ReportBlock>
            {
                milestones.Count == 0 ? new ParagraphBlock("The project has no milestones.") : new TableBlock(
                    new[] { "Milestone", "Date", "Baseline", "Variance", "Deadline", "Status" },
                    milestones.Select(m => (IReadOnlyList<string>)new[]
                    {
                        m.Name, Date(m.Finish), Date(m.Baselines[0]?.Finish), m.Baselines[0] is null ? "" : Dur(m.FinishVarianceMinutes),
                        Date(m.Deadline), m.MissesDeadline ? "Missed deadline" : Status(m),
                    }).ToList(),
                    milestones.Select(m => m.MissesDeadline || IsLate(m) || m.FinishVarianceMinutes > 0).ToList()),
            };
        }

        public List<ReportBlock> WhoDoesWhat()
        {
            var blocks = new List<ReportBlock>();
            foreach (var resource in _p.Resources.Where(r => !r.IsBudget))
            {
                var assignments = _p.AssignmentsOf(resource).Where(a => a.Task.IsActive).OrderBy(a => a.Start).ToList();
                blocks.Add(new HeadingBlock(resource.Name + (resource.IsOverallocated ? "  (over-allocated)" : "")));
                blocks.Add(assignments.Count == 0 ? new ParagraphBlock("No assignments.") : new TableBlock(
                    new[] { "Task", "Units", "Work", "Start", "Finish" },
                    assignments.Select(a => (IReadOnlyList<string>)new[]
                    {
                        a.Task.Name,
                        a.Resource.Type == ResourceType.Work ? Percent(a.Units * 100) : a.Units.ToString("0.##", CultureInfo.CurrentCulture) + " " + a.Resource.MaterialLabel,
                        a.IsWork ? Work(a.WorkMinutes) : "", Date(a.Start), Date(a.Finish),
                    }).ToList()));
            }
            if (blocks.Count == 0)
                blocks.Add(new ParagraphBlock("The project has no resources."));
            return blocks;
        }
    }
}
