using System.Globalization;
using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

/// <summary>
/// One level of grouping. Numbers, durations and work can be grouped in intervals (e.g. durations in
/// 5-day bands); dates can be grouped by day, week or month.
/// </summary>
public sealed record GroupLevel(string FieldKey, bool Descending = false, double Interval = 0, DateGrouping DateGrouping = DateGrouping.Day);

public enum DateGrouping
{
    Day,
    Week,
    Month,
}

/// <summary>A group header in grouped output, with Microsoft Project-style roll-ups of its tasks.</summary>
public sealed class TaskGroup
{
    public required string Label { get; init; }
    public required int Level { get; init; }
    public required string FieldName { get; init; }
    public List<TaskGroup> Subgroups { get; } = new();
    public List<ProjectTask> Tasks { get; } = new();

    /// <summary>Every task in this group and its subgroups.</summary>
    public IEnumerable<ProjectTask> AllTasks => Tasks.Concat(Subgroups.SelectMany(g => g.AllTasks));

    public DateTime Start => AllTasks.Select(t => t.Start).DefaultIfEmpty().Min();
    public DateTime Finish => AllTasks.Select(t => t.Finish).DefaultIfEmpty().Max();
    public long WorkMinutes => AllTasks.Where(t => !t.IsSummary).Sum(t => t.WorkMinutes);
    public decimal Cost => AllTasks.Where(t => !t.IsSummary).Sum(t => t.Cost);

    public double PercentComplete
    {
        get
        {
            var leaves = AllTasks.Where(t => !t.IsSummary).ToList();
            long total = leaves.Sum(t => t.DurationMinutes);
            return total > 0 ? leaves.Sum(t => t.DurationMinutes * t.PercentComplete) / total : leaves.Select(t => t.PercentComplete).DefaultIfEmpty().Average();
        }
    }
}

/// <summary>A named grouping: tasks are placed under group headers by field value (up to several levels).</summary>
public sealed class GroupDefinition
{
    public GroupDefinition(string name, params GroupLevel[] levels)
    {
        Name = name;
        Levels = levels.ToList();
    }

    public string Name { get; set; }
    public List<GroupLevel> Levels { get; set; }

    /// <summary>Summary tasks are hidden when grouping, as in Microsoft Project, unless this is set.</summary>
    public bool ShowSummaryTasks { get; set; }

    public bool IsNoGroup => Levels.Count == 0;

    public static readonly IReadOnlyList<GroupDefinition> Builtin = new[]
    {
        new GroupDefinition("No Group"),
        new GroupDefinition("Status", new GroupLevel("Status")),
        new GroupDefinition("Critical", new GroupLevel("Critical", Descending: true)),
        new GroupDefinition("Milestones", new GroupLevel("Milestone", Descending: true)),
        new GroupDefinition("Duration", new GroupLevel("Duration")),
        new GroupDefinition("Duration then Priority", new GroupLevel("Duration"), new GroupLevel("Priority", Descending: true)),
        new GroupDefinition("Priority", new GroupLevel("Priority", Descending: true)),
        new GroupDefinition("Constraint Type", new GroupLevel("ConstraintType")),
        new GroupDefinition("Task Mode", new GroupLevel("Mode")),
        new GroupDefinition("Resource", new GroupLevel("ResourceNames")),
        new GroupDefinition("Start Month", new GroupLevel("Start", DateGrouping: DateGrouping.Month)),
    };

    public List<TaskGroup> Apply(Project project, IEnumerable<ProjectTask> tasks)
    {
        var list = tasks.Where(t => ShowSummaryTasks || !t.IsSummary).ToList();
        return Build(project, list, 0);
    }

    private List<TaskGroup> Build(Project project, List<ProjectTask> tasks, int level)
    {
        var groupLevel = Levels[level];
        var field = TaskFields.Get(project, groupLevel.FieldKey);
        var buckets = tasks
            .Select(t => (Task: t, Key: BucketKey(field.Get(project, t), groupLevel)))
            .GroupBy(x => x.Key.Sort, new SortKeyComparer())
            .ToList();
        buckets.Sort((a, b) => FieldValue.Compare(a.Key, b.Key) * (groupLevel.Descending ? -1 : 1));

        var groups = new List<TaskGroup>();
        foreach (var bucket in buckets)
        {
            var label = bucket.First().Key.Label;
            var group = new TaskGroup
            {
                Label = string.IsNullOrEmpty(label) ? $"No {field.Name}" : label,
                Level = level,
                FieldName = field.Name,
            };
            var members = bucket.Select(x => x.Task).ToList();
            if (level + 1 < Levels.Count)
                group.Subgroups.AddRange(Build(project, members, level + 1));
            else
                group.Tasks.AddRange(members);
            groups.Add(group);
        }
        return groups;

        (object? Sort, string Label) BucketKey(object? value, GroupLevel g)
        {
            switch (value)
            {
                case null:
                    return (null, "");
                case bool b:
                    return (b, $"{field.Name}: {(b ? "Yes" : "No")}");
                case DateTime d:
                    var day = g.DateGrouping switch
                    {
                        DateGrouping.Month => new DateTime(d.Year, d.Month, 1),
                        DateGrouping.Week => d.Date.AddDays(-(((int)d.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7)),
                        _ => d.Date,
                    };
                    var label = g.DateGrouping switch
                    {
                        DateGrouping.Month => day.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                        DateGrouping.Week => "Week of " + day.ToString("d", CultureInfo.CurrentCulture),
                        _ => day.ToString("ddd d", CultureInfo.CurrentCulture) + " " + day.ToString("MMM yyyy", CultureInfo.CurrentCulture),
                    };
                    return (day, $"{field.Name}: {label}");
                case long minutes when g.Interval > 0:
                    long size = (long)g.Interval;
                    long lower = minutes / size * size;
                    return (lower, $"{field.Name}: {FieldValue.Format(lower, field.Type, project.Options)} - {FieldValue.Format(lower + size, field.Type, project.Options)}");
                case double n when g.Interval > 0:
                    double from = Math.Floor(n / g.Interval) * g.Interval;
                    return (from, $"{field.Name}: {from.ToString(CultureInfo.CurrentCulture)} - {(from + g.Interval).ToString(CultureInfo.CurrentCulture)}");
                default:
                    var text = FieldValue.Format(value, field.Type, project.Options);
                    return (value is string ? text : value, field.Type == FieldType.Text ? text : $"{field.Name}: {text}");
            }
        }
    }

    private sealed class SortKeyComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y) => FieldValue.Compare(x, y) == 0;
        public int GetHashCode(object? obj) => obj switch
        {
            null => 0,
            string s => StringComparer.CurrentCultureIgnoreCase.GetHashCode(s),
            _ => obj.GetHashCode(),
        };
    }
}
