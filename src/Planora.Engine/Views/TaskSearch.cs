using System.Text.RegularExpressions;
using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

/// <summary>Find and Replace over task fields, as Microsoft Project's Find (Ctrl+F) and Replace (Ctrl+H).</summary>
public static class TaskSearch
{
    /// <summary>Text fields Replace can change: the task name, notes, hyperlink fields and custom text fields.</summary>
    public static bool CanReplace(string fieldKey) =>
        fieldKey is "Name" or "Notes" or "Hyperlink" or "HyperlinkAddress" or "HyperlinkSubAddress"
        || CustomField.TypeOfKey(fieldKey) == FieldType.Text;

    /// <summary>
    /// The next task (in <paramref name="order"/>, wrapping around) after <paramref name="after"/> whose field
    /// passes the test, or null if none does. With <paramref name="forward"/> false the search goes upwards.
    /// </summary>
    public static ProjectTask? FindNext(Project project, IReadOnlyList<ProjectTask> order, ProjectTask? after, FilterCondition test, bool forward = true, bool matchCase = false)
    {
        if (order.Count == 0)
            return null;
        int start = after is null ? (forward ? -1 : order.Count) : IndexOf(order, after);
        if (start < 0 && after is not null)
            start = forward ? -1 : order.Count;
        for (int step = 1; step <= order.Count; step++)
        {
            int i = ((start + (forward ? step : -step)) % order.Count + order.Count) % order.Count;
            if (Matches(project, order[i], test, matchCase))
                return order[i];
        }
        return null;
    }

    public static bool Matches(Project project, ProjectTask task, FilterCondition test, bool matchCase = false)
    {
        if (matchCase && test.Operator is FilterOperator.Contains or FilterOperator.Equals && TaskFields.Find(project, test.FieldKey) is { Type: FieldType.Text } field)
        {
            var text = field.Format(project, task);
            return test.Operator == FilterOperator.Contains ? text.Contains(test.Value, StringComparison.Ordinal) : text == test.Value;
        }
        return new FilterDefinition("Find", test).Matches(project, task);
    }

    /// <summary>
    /// Replaces <paramref name="find"/> with <paramref name="replacement"/> in one text field of a task.
    /// Returns the number of replacements made (0 if the field cannot be replaced or does not contain the text).
    /// </summary>
    public static int Replace(Project project, ProjectTask task, string fieldKey, string find, string replacement, bool matchCase = false)
    {
        if (find.Length == 0 || !CanReplace(fieldKey))
            return 0;
        string current = Get(project, task, fieldKey);
        var pattern = new Regex(Regex.Escape(find), matchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int count = pattern.Matches(current).Count;
        if (count == 0)
            return 0;
        string updated = pattern.Replace(current, replacement.Replace("$", "$$"));
        switch (fieldKey)
        {
            case "Name":
                task.Name = updated;
                break;
            case "Notes":
                task.Notes = updated;
                break;
            case "Hyperlink":
                task.Hyperlink = updated;
                break;
            case "HyperlinkAddress":
                task.HyperlinkAddress = updated;
                break;
            case "HyperlinkSubAddress":
                task.HyperlinkSubAddress = updated;
                break;
            default:
                if (!TaskFields.TrySetCustom(project, task, fieldKey, updated))
                    return 0;
                break;
        }
        return count;
    }

    /// <summary>Replace All over the given tasks. Returns the number of replacements.</summary>
    public static int ReplaceAll(Project project, IEnumerable<ProjectTask> tasks, string fieldKey, string find, string replacement, bool matchCase = false) =>
        tasks.ToList().Sum(t => Replace(project, t, fieldKey, find, replacement, matchCase));

    private static string Get(Project project, ProjectTask task, string fieldKey) => fieldKey switch
    {
        "Name" => task.Name,
        "Notes" => task.Notes,
        "Hyperlink" => task.Hyperlink,
        "HyperlinkAddress" => task.HyperlinkAddress,
        "HyperlinkSubAddress" => task.HyperlinkSubAddress,
        _ => TaskFields.Get(project, fieldKey).Format(project, task),
    };

    private static int IndexOf(IReadOnlyList<ProjectTask> order, ProjectTask task)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == task)
                return i;
        }
        return -1;
    }
}
