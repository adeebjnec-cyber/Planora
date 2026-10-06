using Planora.Engine.Calendars;
using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.Engine.Interop;

public enum OrganizerItemKind
{
    Calendar,
    Table,
    Filter,
    Group,
    View,
    CustomField,
    Report,
    Macro,
}

/// <summary>
/// The Organizer: copies calendars, tables, filters, groups, saved views, custom field definitions,
/// reports and macro modules from one project to another (an item with the same name is replaced).
/// </summary>
public static class Organizer
{
    public static IReadOnlyList<string> Names(Project project, OrganizerItemKind kind) => kind switch
    {
        OrganizerItemKind.Calendar => project.Calendars.Select(c => c.Name).ToList(),
        OrganizerItemKind.Table => project.Tables.Select(t => t.Name).ToList(),
        OrganizerItemKind.Filter => project.Filters.Select(f => f.Name).ToList(),
        OrganizerItemKind.Group => project.Groups.Select(g => g.Name).ToList(),
        OrganizerItemKind.View => project.SavedViews.Select(v => v.Name).ToList(),
        OrganizerItemKind.CustomField => project.CustomFields.Select(f => f.Alias.Length > 0 ? $"{f.Key} ({f.Alias})" : f.Key).ToList(),
        OrganizerItemKind.Report => project.CustomReports.Select(r => r.Name).ToList(),
        _ => project.Macros.Select(m => m.Name).ToList(),
    };

    /// <summary>Copies one item; the copy shares nothing with the source project.</summary>
    public static void Copy(Project from, Project to, OrganizerItemKind kind, string name)
    {
        // A serialised round trip gives independent copies of every definition.
        var clone = ProjectFile.Deserialize(ProjectFile.Serialize(from));
        switch (kind)
        {
            case OrganizerItemKind.Calendar:
            {
                var source = clone.Calendars.FirstOrDefault(c => c.Name == name) ?? throw Missing(name);
                // Keep the base calendar when the target has one of the same name; otherwise flatten.
                var newBase = source.BaseCalendar is { } b ? to.Calendars.FirstOrDefault(c => c.Name == b.Name) : null;
                var copy = source.Rebase(newBase, name);
                int index = to.Calendars.FindIndex(c => c.Name == name);
                if (index >= 0)
                {
                    var old = to.Calendars[index];
                    to.Calendars[index] = copy;
                    // Everything that used the replaced calendar uses the copy.
                    if (to.Calendar == old)
                        to.Calendar = copy;
                    foreach (var t in to.Tasks.Where(t => t.Calendar == old))
                        t.Calendar = copy;
                }
                else
                {
                    to.Calendars.Add(copy);
                }
                break;
            }
            case OrganizerItemKind.Table:
                Replace(to.Tables, clone.Tables.FirstOrDefault(t => t.Name == name) ?? throw Missing(name), t => t.Name == name);
                break;
            case OrganizerItemKind.Filter:
                Replace(to.Filters, clone.Filters.FirstOrDefault(f => f.Name == name) ?? throw Missing(name), f => f.Name == name);
                break;
            case OrganizerItemKind.Group:
                Replace(to.Groups, clone.Groups.FirstOrDefault(g => g.Name == name) ?? throw Missing(name), g => g.Name == name);
                break;
            case OrganizerItemKind.View:
                Replace(to.SavedViews, clone.SavedViews.FirstOrDefault(v => v.Name == name) ?? throw Missing(name), v => v.Name == name);
                break;
            case OrganizerItemKind.CustomField:
            {
                var field = clone.CustomFields.FirstOrDefault(f => Names(clone, kind)[clone.CustomFields.IndexOf(f)] == name) ?? throw Missing(name);
                Replace(to.CustomFields, field, f => f.Key == field.Key);
                break;
            }
            case OrganizerItemKind.Report:
                Replace(to.CustomReports, clone.CustomReports.FirstOrDefault(r => r.Name == name) ?? throw Missing(name), r => r.Name == name);
                break;
            default:
                Replace(to.Macros, clone.Macros.FirstOrDefault(m => m.Name == name) ?? throw Missing(name), m => m.Name == name);
                break;
        }
    }

    /// <summary>Deletes an item. Calendars in use (by the project, tasks or resources) are kept.</summary>
    public static bool Delete(Project project, OrganizerItemKind kind, string name)
    {
        switch (kind)
        {
            case OrganizerItemKind.Calendar:
                var calendar = project.Calendars.FirstOrDefault(c => c.Name == name);
                if (calendar is null || project.Calendar == calendar || project.Tasks.Any(t => t.Calendar == calendar)
                    || project.Resources.Any(r => r.Calendar?.BaseCalendar == calendar) || project.Calendars.Any(c => c.BaseCalendar == calendar))
                    return false;
                return project.Calendars.Remove(calendar);
            case OrganizerItemKind.Table:
                return project.Tables.RemoveAll(t => t.Name == name) > 0;
            case OrganizerItemKind.Filter:
                return project.Filters.RemoveAll(f => f.Name == name) > 0;
            case OrganizerItemKind.Group:
                return project.Groups.RemoveAll(g => g.Name == name) > 0;
            case OrganizerItemKind.View:
                return project.SavedViews.RemoveAll(v => v.Name == name) > 0;
            case OrganizerItemKind.CustomField:
                var names = Names(project, kind);
                int index = names.ToList().IndexOf(name);
                if (index < 0)
                    return false;
                project.CustomFields.RemoveAt(index);
                return true;
            case OrganizerItemKind.Report:
                return project.CustomReports.RemoveAll(r => r.Name == name) > 0;
            default:
                return project.Macros.RemoveAll(m => m.Name == name) > 0;
        }
    }

    private static void Replace<T>(List<T> list, T item, Predicate<T> same)
    {
        int index = list.FindIndex(same);
        if (index >= 0)
            list[index] = item;
        else
            list.Add(item);
    }

    private static InvalidOperationException Missing(string name) => new($"'{name}' was not found.");
}
