using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>A field choice for combo boxes: shows the field's name (or custom alias), stores its key.</summary>
public sealed record FieldChoice(string Key, string Name)
{
    public override string ToString() => Name;

    /// <summary>All task fields: built-in, then custom slots (aliased ones first).</summary>
    public static List<FieldChoice> All(Project project)
    {
        var fields = TaskFields.All(project);
        var builtin = fields.Where(f => !f.IsCustom).Select(f => new FieldChoice(f.Key, f.Name)).OrderBy(f => f.Name);
        var custom = fields.Where(f => f.IsCustom)
            .OrderBy(f => f.Name == f.Key ? 1 : 0)
            .Select(f => new FieldChoice(f.Key, f.Name == f.Key ? f.Key : $"{f.Name} ({f.Key})"));
        return builtin.Concat(custom).ToList();
    }
}
