using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    DoesNotContain,
}

/// <summary>One test in a filter, e.g. "% Complete is less than 100". <see cref="Or"/> joins it to the previous test with Or instead of And.</summary>
public sealed record FilterCondition(string FieldKey, FilterOperator Operator, string Value, bool Or = false);

/// <summary>
/// A named filter: tests combined with And/Or (And binds tighter, as in Microsoft Project's filter
/// definitions). With <see cref="ShowRelatedSummaryRows"/>, summary tasks above matching tasks are kept.
/// </summary>
public sealed class FilterDefinition
{
    public FilterDefinition(string name, params FilterCondition[] conditions)
    {
        Name = name;
        Conditions = conditions.ToList();
    }

    public string Name { get; set; }
    public List<FilterCondition> Conditions { get; set; }
    public bool ShowRelatedSummaryRows { get; set; }

    public bool IsAllTasks => Conditions.Count == 0;

    public static readonly IReadOnlyList<FilterDefinition> Builtin = new[]
    {
        new FilterDefinition("All Tasks"),
        new FilterDefinition("Active Tasks", new FilterCondition("Active", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Completed Tasks", new FilterCondition("PercentComplete", FilterOperator.Equals, "100")),
        new FilterDefinition("Incomplete Tasks", new FilterCondition("PercentComplete", FilterOperator.LessThan, "100")),
        new FilterDefinition("Critical", new FilterCondition("Critical", FilterOperator.Equals, "Yes")) { ShowRelatedSummaryRows = true },
        new FilterDefinition("Late Tasks", new FilterCondition("Status", FilterOperator.Equals, "Late")),
        new FilterDefinition("Slipping Tasks",
            new FilterCondition("PercentComplete", FilterOperator.LessThan, "100"),
            new FilterCondition("FinishVariance", FilterOperator.GreaterThan, "0d")),
        new FilterDefinition("Milestones", new FilterCondition("Milestone", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Marked Tasks", new FilterCondition("Marked", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Summary Tasks", new FilterCondition("Summary", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Manually Scheduled Tasks", new FilterCondition("Mode", FilterOperator.Equals, "Manually Scheduled")),
        new FilterDefinition("Tasks With Deadlines", new FilterCondition("Deadline", FilterOperator.NotEquals, "NA")),
        new FilterDefinition("Tasks With Missed Deadlines", new FilterCondition("DeadlineMissed", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Tasks With Estimated Durations", new FilterCondition("Estimated", FilterOperator.Equals, "Yes")),
        new FilterDefinition("Tasks With Fixed Dates", new FilterCondition("ConstraintType", FilterOperator.NotEquals, "As Soon As Possible")),
        new FilterDefinition("Cost Overbudget", new FilterCondition("CostVariance", FilterOperator.GreaterThan, "0")),
        new FilterDefinition("Work Overbudget", new FilterCondition("WorkVariance", FilterOperator.GreaterThan, "0h")),
    };

    /// <summary>Tests one task. Every condition's value is parsed for its field's type; unparsable values never match.</summary>
    public bool Matches(Project project, ProjectTask task)
    {
        if (Conditions.Count == 0)
            return true;

        // Evaluate as OR of AND-groups.
        bool anyGroup = false, group = true;
        for (int i = 0; i < Conditions.Count; i++)
        {
            if (i > 0 && Conditions[i].Or)
            {
                anyGroup |= group;
                group = true;
            }
            group &= Test(project, task, Conditions[i]);
        }
        return anyGroup | group;
    }

    /// <summary>Tasks matching the filter, in outline order, plus their summaries when requested.</summary>
    public List<ProjectTask> Apply(Project project, IEnumerable<ProjectTask> tasks)
    {
        var list = tasks.ToList();
        var matching = list.Where(t => Matches(project, t)).ToHashSet();
        if (ShowRelatedSummaryRows)
        {
            foreach (var task in matching.ToList())
            {
                for (var p = task.Parent; p is not null; p = p.Parent)
                    matching.Add(p);
            }
        }
        return list.Where(matching.Contains).ToList();
    }

    private static bool Test(Project project, ProjectTask task, FilterCondition condition)
    {
        var field = TaskFields.Find(project, condition.FieldKey);
        if (field is null)
            return false;
        var actual = field.Get(project, task);

        if (field.Type == FieldType.Text || condition.Operator is FilterOperator.Contains or FilterOperator.DoesNotContain)
        {
            var text = actual as string ?? field.Format(project, task);
            return condition.Operator switch
            {
                FilterOperator.Contains => text.Contains(condition.Value, StringComparison.CurrentCultureIgnoreCase),
                FilterOperator.DoesNotContain => !text.Contains(condition.Value, StringComparison.CurrentCultureIgnoreCase),
                _ => Compare(string.Compare(text, condition.Value, StringComparison.CurrentCultureIgnoreCase), condition.Operator),
            };
        }

        if (!FieldValue.TryParse(condition.Value, field.Type, project.Options, out var expected))
            return false;
        if (field.Type == FieldType.Date && actual is DateTime a && expected is DateTime e && e.TimeOfDay == TimeSpan.Zero)
        {
            // A date without a time compares by day.
            return Compare(a.Date.CompareTo(e.Date), condition.Operator);
        }
        return Compare(FieldValue.Compare(actual, expected), condition.Operator);
    }

    private static bool Compare(int order, FilterOperator op) => op switch
    {
        FilterOperator.Equals => order == 0,
        FilterOperator.NotEquals => order != 0,
        FilterOperator.GreaterThan => order > 0,
        FilterOperator.GreaterThanOrEqual => order >= 0,
        FilterOperator.LessThan => order < 0,
        FilterOperator.LessThanOrEqual => order <= 0,
        _ => false,
    };
}
