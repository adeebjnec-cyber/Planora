using System.Globalization;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;
using Planora.Engine.Views;

namespace Planora.Engine.Reports;

/// <summary>One series of a time-series chart: drawn as a line or as columns.</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, bool AsColumns = false);

/// <summary>
/// A chart over time (e.g. burndown, cash flow): one value per period for each series, with period labels.
/// <see cref="ValueFormat"/> formats axis and value labels ("hours", "money" or "count").
/// </summary>
public sealed record TimeChartBlock(string Title, IReadOnlyList<DateTime> Periods, IReadOnlyList<string> PeriodLabels, IReadOnlyList<ChartSeries> Series, string ValueFormat, DateTime? StatusDate = null) : ReportBlock;

/// <summary>
/// The reports added to match Microsoft Project's built-in set: burndown, cash flow, cost overruns,
/// late/upcoming/slipping tasks, over-allocated resources and work overview.
/// </summary>
internal sealed class MoreReports
{
    private readonly Project _p;
    private readonly ScheduleOptions _o;
    private readonly DateTime _statusDate;
    private readonly List<ProjectTask> _leaves;
    private readonly List<ProjectTask> _topLevel;

    public MoreReports(Project project)
    {
        _p = project;
        _o = project.Options;
        _statusDate = project.StatusDate ?? DateTime.Now;
        _leaves = project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        _topLevel = project.Tasks.Where(t => t.Parent is null && t.IsActive).ToList();
    }

    public static readonly IReadOnlyList<string> Names = new[]
    {
        "Burndown",
        "Cash Flow",
        "Cost Overruns",
        "Late Tasks",
        "Upcoming Tasks",
        "Slipping Tasks",
        "Overallocated Resources",
        "Work Overview",
    };

    public List<ReportBlock> Build(string name) => name switch
    {
        "Burndown" => Burndown(),
        "Cash Flow" => CashFlow(),
        "Cost Overruns" => CostOverruns(),
        "Late Tasks" => LateTasks(),
        "Upcoming Tasks" => UpcomingTasks(),
        "Slipping Tasks" => SlippingTasks(),
        "Overallocated Resources" => OverallocatedResources(),
        _ => WorkOverview(),
    };

    private static string Date(DateTime? d) => d is { } v ? v.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) : "NA";
    private string Dur(long minutes) => DurationText.Format(minutes, _o);
    private string Work(long minutes) => DurationText.Format(minutes, _o, DurationUnit.Hours);
    private static string Money(decimal value) => value.ToString("C", CultureInfo.CurrentCulture);
    private static string Percent(double value) => value.ToString("0", CultureInfo.CurrentCulture) + "%";
    private string Status(ProjectTask t) => TaskFields.Get(_p, "Status").Format(_p, t);

    private List<Assignment> WorkAssignments() => _leaves.SelectMany(t => t.Assignments).Where(a => a.IsWork).ToList();

    /// <summary>Period starts covering the project (and its baseline): weeks, or months for long projects.</summary>
    private (List<DateTime> Starts, List<string> Labels, DateTime End) Periods()
    {
        var start = _p.Tasks.Count > 0 ? _p.Start : _p.StartDate;
        var end = _p.Tasks.Count > 0 ? _p.Finish : _p.StartDate.AddDays(7);
        foreach (var t in _leaves)
        {
            if (t.Baselines[0] is { } b)
            {
                if (b.Start < start) start = b.Start;
                if (b.Finish > end) end = b.Finish;
            }
        }
        bool monthly = (end - start).TotalDays > 180;
        var starts = new List<DateTime>();
        var labels = new List<string>();
        var first = monthly ? new DateTime(start.Year, start.Month, 1) : start.Date.AddDays(-(((int)start.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7));
        for (var p = first; p <= end; p = monthly ? p.AddMonths(1) : p.AddDays(7))
        {
            starts.Add(p);
            labels.Add(p.ToString(monthly ? "MMM yy" : "d MMM", CultureInfo.CurrentCulture));
        }
        return (starts, labels, monthly ? starts[^1].AddMonths(1) : starts[^1].AddDays(7));
    }

    private static DateTime Next(List<DateTime> starts, int i, DateTime end) => i + 1 < starts.Count ? starts[i + 1] : end;

    public List<ReportBlock> Burndown()
    {
        var (starts, labels, end) = Periods();
        var assignments = WorkAssignments();
        double totalWork = assignments.Sum(a => (double)a.WorkMinutes);
        double baselineTotal = assignments.Sum(a => (double)(a.Baselines[0]?.WorkMinutes ?? 0));
        var remaining = new List<double>();
        var baselineRemaining = new List<double>();
        var actualRemaining = new List<double>();
        double scheduled = 0, baseline = 0, actual = 0;
        for (int i = 0; i < starts.Count; i++)
        {
            var from = starts[i];
            var to = Next(starts, i, end);
            scheduled += assignments.Sum(a => Timephased.WorkBetween(a, from, to));
            baseline += assignments.Sum(a => Timephased.BaselineWorkBetween(a, 0, from, to));
            actual += assignments.Sum(a => Timephased.ActualWorkBetween(a, from, to));
            remaining.Add(Math.Max(0, (totalWork - scheduled) / 60));
            baselineRemaining.Add(Math.Max(0, (baselineTotal - baseline) / 60));
            // Actual remaining work is only known up to the status date.
            actualRemaining.Add(from <= _statusDate ? Math.Max(0, (totalWork - actual) / 60) : double.NaN);
        }
        var series = new List<ChartSeries> { new("Remaining cumulative work", remaining) };
        if (baselineTotal > 0)
            series.Add(new ChartSeries("Baseline remaining work", baselineRemaining));
        series.Add(new ChartSeries("Remaining actual work", actualRemaining));

        // Task burndown: tasks not yet finished by each period, scheduled and (to the status date) actual.
        var plannedTasks = starts.Select((s, i) => (double)_leaves.Count(t => t.Finish > Next(starts, i, end))).ToList();
        // Actual: tasks not actually finished by the end of each period (known up to the status date).
        var openTasks = starts.Select((s, i) =>
        {
            var periodEnd = Next(starts, i, end);
            var asOf = periodEnd < _statusDate ? periodEnd : _statusDate;
            return s <= _statusDate ? (double)_leaves.Count(t => t.ActualFinish is not { } f || f > asOf) : double.NaN;
        }).ToList();

        return new List<ReportBlock>
        {
            new FiguresBlock(new[]
            {
                ("Work", Work((long)totalWork)),
                ("Remaining work", Work(assignments.Sum(a => a.RemainingWorkMinutes))),
                ("% Work complete", Percent(totalWork > 0 ? 100 * assignments.Sum(a => (double)a.ActualWorkMinutes) / totalWork : 0)),
                ("Tasks remaining", _leaves.Count(t => t.PercentComplete < 100).ToString(CultureInfo.CurrentCulture)),
            }),
            new TimeChartBlock("Work burndown (hours)", starts, labels, series, "hours", _statusDate),
            new TimeChartBlock("Task burndown", starts, labels, new[]
            {
                new ChartSeries("Remaining tasks (scheduled)", plannedTasks),
                new ChartSeries("Remaining tasks (actual)", openTasks),
            }, "count", _statusDate),
            new ParagraphBlock("Remaining cumulative work follows the current schedule; the baseline line shows the plan saved with Set Baseline; the actual line is the work not yet done, up to the status date."),
        };
    }

    public List<ReportBlock> CashFlow()
    {
        var (starts, labels, end) = Periods();
        var assignments = _leaves.SelectMany(t => t.Assignments).ToList();
        var costs = new List<double>();
        var cumulative = new List<double>();
        var baselineCumulative = new List<double>();
        double running = 0, baselineRunning = 0;
        for (int i = 0; i < starts.Count; i++)
        {
            var from = starts[i];
            var to = Next(starts, i, end);
            double cost = (double)(assignments.Sum(a => Timephased.CostBetween(a, from, to))
                + _leaves.Sum(t => Timephased.FixedCostBetween(t, t.Calendar ?? _p.Calendar, from, to)));
            running += cost;
            baselineRunning += (double)(assignments.Sum(a => Timephased.BaselineCostBetween(a, 0, from, to))
                + _leaves.Where(t => t.Assignments.Count == 0).Sum(t => Timephased.BaselineCostBetween(t, t.Calendar ?? _p.Calendar, 0, from, to)));
            costs.Add(cost);
            cumulative.Add(running);
            baselineCumulative.Add(baselineRunning);
        }
        var series = new List<ChartSeries> { new("Cost per period", costs, AsColumns: true), new("Cumulative cost", cumulative) };
        if (baselineCumulative.Any(v => v > 0))
            series.Add(new ChartSeries("Baseline cumulative cost", baselineCumulative));
        var rows = starts.Select((s, i) => (IReadOnlyList<string>)new[] { labels[i], Money((decimal)costs[i]), Money((decimal)cumulative[i]) }).ToList();
        return new List<ReportBlock>
        {
            new FiguresBlock(new[]
            {
                ("Cost", Money(_p.Cost)),
                ("Actual cost", Money(_p.ActualCost)),
                ("Remaining cost", Money(_p.Cost - _p.ActualCost)),
                ("Baseline cost", _p.Baselines[0] is { } b ? Money(b.Cost) : "NA"),
            }),
            new TimeChartBlock("Cash flow", starts, labels, series, "money", _statusDate),
            new HeadingBlock("By period"),
            new TableBlock(new[] { "Period", "Cost", "Cumulative Cost" }, rows),
        };
    }

    public List<ReportBlock> CostOverruns()
    {
        var tasks = _p.Tasks.Where(t => t.IsActive && t.Baselines[0] is not null && t.CostVariance > 0).ToList();
        var resources = _p.Resources.Where(r => !r.IsBudget).Select(r =>
        {
            var assignments = _p.AssignmentsOf(r).ToList();
            decimal baseline = assignments.Sum(a => a.Baselines[0]?.Cost ?? 0);
            return (Resource: r, Baseline: baseline, Variance: r.Cost - baseline, HasBaseline: assignments.Any(a => a.Baselines[0] is not null));
        }).Where(x => x.HasBaseline && x.Variance > 0).ToList();
        var blocks = new List<ReportBlock>();
        if (_p.Tasks.All(t => t.Baselines[0] is null))
            blocks.Add(new ParagraphBlock("Cost overruns compare with the baseline: use Project > Set Baseline first."));
        blocks.Add(new FiguresBlock(new[]
        {
            ("Cost", Money(_p.Cost)),
            ("Baseline cost", _p.Baselines[0] is { } b ? Money(b.Cost) : "NA"),
            ("Cost variance", _p.Baselines[0] is { } b2 ? Money(_p.Cost - b2.Cost) : "NA"),
            ("Tasks over budget", tasks.Count(t => !t.IsSummary).ToString(CultureInfo.CurrentCulture)),
        }));
        if (tasks.Count > 0)
        {
            blocks.Add(new BarChartBlock("Cost variance by task",
                tasks.Where(t => !t.IsSummary).OrderByDescending(t => t.CostVariance).Take(15)
                    .Select(t => new ChartBar(t.Name, (double)t.CostVariance, Money(t.CostVariance))).ToList(), "Cost variance"));
        }
        blocks.Add(new HeadingBlock("Tasks over budget"));
        blocks.Add(tasks.Count == 0 ? new ParagraphBlock("No task costs more than its baseline.") : new TableBlock(
            new[] { "Task", "Cost", "Baseline Cost", "Variance", "Actual Cost", "Remaining" },
            tasks.Select(t => (IReadOnlyList<string>)new[]
            {
                new string(' ', (t.OutlineLevel - 1) * 3) + t.Name, Money(t.Cost), Money(t.Baselines[0]!.Cost), Money(t.CostVariance), Money(t.ActualCost), Money(t.RemainingCost),
            }).ToList(),
            tasks.Select(_ => true).ToList()));
        blocks.Add(new HeadingBlock("Resources over budget"));
        blocks.Add(resources.Count == 0 ? new ParagraphBlock("No resource costs more than its baseline.") : new TableBlock(
            new[] { "Resource", "Cost", "Baseline Cost", "Variance" },
            resources.Select(x => (IReadOnlyList<string>)new[] { x.Resource.Name, Money(x.Resource.Cost), Money(x.Baseline), Money(x.Variance) }).ToList(),
            resources.Select(_ => true).ToList()));
        return blocks;
    }

    private TableBlock TaskTable(IReadOnlyList<ProjectTask> tasks, bool alert)
    {
        return new TableBlock(
            new[] { "Task", "Start", "Finish", "Duration", "% Complete", "Status", "Resources" },
            tasks.Select(t => (IReadOnlyList<string>)new[] { t.Name, Date(t.Start), Date(t.Finish), Dur(t.DurationMinutes), Percent(t.PercentComplete), Status(t), ResourceNamesText.Format(t) }).ToList(),
            alert ? tasks.Select(_ => true).ToList() : null);
    }

    public List<ReportBlock> LateTasks()
    {
        var late = _leaves.Where(t => Status(t) == "Late").OrderBy(t => t.Finish).ToList();
        return new List<ReportBlock>
        {
            new ParagraphBlock($"Tasks that should have made more progress by the status date ({Date(_statusDate)})."),
            late.Count == 0 ? new ParagraphBlock("No tasks are late.") : TaskTable(late, alert: true),
        };
    }

    public List<ReportBlock> UpcomingTasks()
    {
        var weekStart = _statusDate.Date.AddDays(-(((int)_statusDate.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7));
        var thisWeek = _leaves.Where(t => t.PercentComplete < 100 && t.Start < weekStart.AddDays(7) && t.Finish >= weekStart).OrderBy(t => t.Start).ToList();
        var nextWeek = _leaves.Where(t => t.PercentComplete < 100 && t.Start >= weekStart.AddDays(7) && t.Start < weekStart.AddDays(14)).OrderBy(t => t.Start).ToList();
        return new List<ReportBlock>
        {
            new HeadingBlock($"This week (from {Date(weekStart)})"),
            thisWeek.Count == 0 ? new ParagraphBlock("No unfinished tasks are scheduled this week.") : TaskTable(thisWeek, alert: false),
            new HeadingBlock("Starting next week"),
            nextWeek.Count == 0 ? new ParagraphBlock("No tasks start next week.") : TaskTable(nextWeek, alert: false),
        };
    }

    public List<ReportBlock> SlippingTasks()
    {
        var slipping = _leaves.Where(t => t.PercentComplete < 100 && t.Baselines[0] is not null && t.FinishVarianceMinutes > 0)
            .OrderByDescending(t => t.FinishVarianceMinutes).ToList();
        var blocks = new List<ReportBlock>();
        if (_leaves.All(t => t.Baselines[0] is null))
            blocks.Add(new ParagraphBlock("Slipping tasks are compared with the baseline: use Project > Set Baseline first."));
        blocks.Add(new ParagraphBlock("Unfinished tasks now scheduled to finish later than their baseline finish."));
        blocks.Add(slipping.Count == 0 ? new ParagraphBlock("No tasks are slipping.") : new TableBlock(
            new[] { "Task", "Finish", "Baseline Finish", "Finish Variance", "% Complete", "Resources" },
            slipping.Select(t => (IReadOnlyList<string>)new[] { t.Name, Date(t.Finish), Date(t.Baselines[0]!.Finish), Dur(t.FinishVarianceMinutes), Percent(t.PercentComplete), ResourceNamesText.Format(t) }).ToList(),
            slipping.Select(_ => true).ToList()));
        return blocks;
    }

    public List<ReportBlock> OverallocatedResources()
    {
        var overallocations = ResourceAnalysis.FindOverallocations(_p, OverallocationBasis.DayByDay);
        var resources = overallocations.Select(o => o.Resource).Distinct().ToList();
        var blocks = new List<ReportBlock>
        {
            new FiguresBlock(new[]
            {
                ("Over-allocated resources", resources.Count.ToString(CultureInfo.CurrentCulture)),
                ("Over-allocated periods", overallocations.Count.ToString(CultureInfo.CurrentCulture)),
            }),
        };
        if (resources.Count == 0)
        {
            blocks.Add(new ParagraphBlock("No resource is over-allocated (checked day by day)."));
            return blocks;
        }
        blocks.Add(new BarChartBlock("Work of over-allocated resources",
            resources.Select(r => new ChartBar(r.Name, r.WorkMinutes / 60.0, Work(r.WorkMinutes),
                ResourceAnalysis.ScheduledAssignments(_p, r).Sum(a => a.ActualWorkMinutes) / 60.0)).ToList(), "Work", "Actual work"));
        foreach (var resource in resources)
        {
            var periods = overallocations.Where(o => o.Resource == resource).ToList();
            blocks.Add(new HeadingBlock($"{resource.Name} (max units {Percent(resource.MaxUnits * 100)})"));
            blocks.Add(new TableBlock(new[] { "From", "To", "Peak Units", "Tasks" },
                periods.Select(o => (IReadOnlyList<string>)new[]
                {
                    Date(o.From), Date(o.To), Percent(o.PeakUnits * 100),
                    string.Join(", ", ResourceAnalysis.ScheduledAssignments(_p, resource).Where(a => a.Start < o.To && a.Finish > o.From).Select(a => a.Task.Name).Distinct()),
                }).ToList(),
                periods.Select(_ => true).ToList()));
        }
        return blocks;
    }

    public List<ReportBlock> WorkOverview()
    {
        var (starts, labels, end) = Periods();
        var assignments = WorkAssignments();
        var perPeriod = new List<double>();
        var actualPerPeriod = new List<double>();
        for (int i = 0; i < starts.Count; i++)
        {
            perPeriod.Add(assignments.Sum(a => Timephased.WorkBetween(a, starts[i], Next(starts, i, end))) / 60);
            actualPerPeriod.Add(starts[i] <= _statusDate ? assignments.Sum(a => Timephased.ActualWorkBetween(a, starts[i], Next(starts, i, end))) / 60 : double.NaN);
        }
        var blocks = new List<ReportBlock>
        {
            new FiguresBlock(new[]
            {
                ("Work", Work(_p.WorkMinutes)),
                ("Actual work", Work(assignments.Sum(a => a.ActualWorkMinutes))),
                ("Remaining work", Work(assignments.Sum(a => a.RemainingWorkMinutes))),
                ("Baseline work", _p.Baselines[0] is { } b ? Work(b.WorkMinutes) : "NA"),
            }),
            new TimeChartBlock("Work by period (hours)", starts, labels, new[]
            {
                new ChartSeries("Work", perPeriod, AsColumns: true),
                new ChartSeries("Actual work", actualPerPeriod),
            }, "hours", _statusDate),
        };
        if (_topLevel.Count > 0)
        {
            blocks.Add(new BarChartBlock("Work by phase: total, and actual done",
                _topLevel.Select(t => new ChartBar(t.Name, t.WorkMinutes / 60.0, Work(t.WorkMinutes), t.ActualWorkMinutes / 60.0)).ToList(), "Work", "Actual work"));
        }
        var workers = _p.Resources.Where(r => r.Type == ResourceType.Work && !r.IsBudget).ToList();
        blocks.Add(new HeadingBlock("Resource work"));
        blocks.Add(new TableBlock(new[] { "Resource", "Work", "Actual Work", "Remaining Work", "% Work Complete" },
            workers.Select(r =>
            {
                var a = ResourceAnalysis.ScheduledAssignments(_p, r).ToList();
                long actual = a.Sum(x => x.ActualWorkMinutes);
                return (IReadOnlyList<string>)new[] { r.Name, Work(r.WorkMinutes), Work(actual), Work(r.WorkMinutes - actual), Percent(r.WorkMinutes > 0 ? 100.0 * actual / r.WorkMinutes : 0) };
            }).ToList(),
            workers.Select(r => r.IsOverallocated).ToList()));
        return blocks;
    }
}
