using Planora.Engine.Model;

namespace Planora.Engine.Views;

/// <summary>Built-in plus project-defined tables, filters and groups; project definitions win by name.</summary>
public static class ViewCatalog
{
    public static IReadOnlyList<TableDefinition> Tables(Project project) => Merge(project.Tables, TableDefinition.Builtin, t => t.Name);
    public static IReadOnlyList<FilterDefinition> Filters(Project project) => Merge(project.Filters, FilterDefinition.Builtin, f => f.Name);
    public static IReadOnlyList<GroupDefinition> Groups(Project project) => Merge(project.Groups, GroupDefinition.Builtin, g => g.Name);

    public static TableDefinition Table(Project project, string? name) =>
        Tables(project).FirstOrDefault(t => t.Name == name) ?? TableDefinition.Builtin[0];

    public static FilterDefinition Filter(Project project, string? name) =>
        Filters(project).FirstOrDefault(f => f.Name == name) ?? FilterDefinition.Builtin[0];

    public static GroupDefinition Group(Project project, string? name) =>
        Groups(project).FirstOrDefault(g => g.Name == name) ?? GroupDefinition.Builtin[0];

    private static IReadOnlyList<T> Merge<T>(IEnumerable<T> custom, IEnumerable<T> builtin, Func<T, string> name)
    {
        var result = builtin.ToList();
        foreach (var item in custom)
        {
            int index = result.FindIndex(b => name(b) == name(item));
            if (index >= 0)
                result[index] = item;
            else
                result.Add(item);
        }
        return result;
    }
}
