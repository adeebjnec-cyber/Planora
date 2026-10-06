using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

public sealed record SortKey(string FieldKey, bool Descending = false);

/// <summary>
/// Sort by up to three fields. With <see cref="KeepOutlineStructure"/>, tasks are sorted among their
/// siblings and stay under their summary tasks; otherwise the list is sorted flat.
/// </summary>
public sealed class SortDefinition
{
    public List<SortKey> Keys { get; set; } = new();
    public bool KeepOutlineStructure { get; set; } = true;

    public bool IsDefault => Keys.Count == 0;

    public List<ProjectTask> Apply(Project project, IEnumerable<ProjectTask> tasks)
    {
        var list = tasks.ToList();
        if (Keys.Count == 0)
            return list;

        var fields = Keys.Select(k => (Field: TaskFields.Get(project, k.FieldKey), k.Descending)).ToList();
        int Compare(ProjectTask a, ProjectTask b)
        {
            foreach (var (field, descending) in fields)
            {
                int order = FieldValue.Compare(field.Get(project, a), field.Get(project, b));
                if (order != 0)
                    return descending ? -order : order;
            }
            return project.IndexOf(a).CompareTo(project.IndexOf(b));
        }

        if (!KeepOutlineStructure)
        {
            list.Sort(Compare);
            return list;
        }

        // Sort siblings, then walk the tree so children follow their (sorted) parent.
        var included = list.ToHashSet();
        var result = new List<ProjectTask>(list.Count);
        void Visit(IEnumerable<ProjectTask> siblings)
        {
            var sorted = siblings.ToList();
            sorted.Sort(Compare);
            foreach (var task in sorted)
            {
                if (included.Contains(task))
                    result.Add(task);
                Visit(task.Children);
            }
        }
        Visit(project.Tasks.Where(t => t.Parent is null));
        return result;
    }
}
