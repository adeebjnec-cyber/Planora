using System.Globalization;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Table definition: which columns the task sheet shows, in order, with widths and optional titles.</summary>
public sealed class TableEditorDialog : DefinitionEditor<TableEditorDialog.Row>
{
    public sealed class Row
    {
        public FieldChoice? Field { get; set; }
        public string Width { get; set; } = "90";
        public string? Title { get; set; }
    }

    public TableEditorDialog(MainViewModel owner)
        : base(owner, "Table Definition", owner.TableNames, owner.CurrentTableName)
    {
        AddFieldColumn("Field Name", nameof(Row.Field), 280);
        AddTextColumn("Width", nameof(Row.Width), 70);
        AddTextColumn("Title (optional)", nameof(Row.Title), 220);
    }

    protected override void Load(string? name)
    {
        if (name is null)
        {
            Rows.Add(new Row { Field = Choice("Name"), Width = "220" });
            return;
        }
        foreach (var column in ViewCatalog.Table(ViewModel.Project, name).Columns)
            Rows.Add(new Row { Field = Choice(column.FieldKey), Width = column.Width.ToString(CultureInfo.CurrentCulture), Title = column.Title });
    }

    protected override void Store(Project project)
    {
        var columns = new List<TableColumn>();
        foreach (var row in Rows.Where(r => r.Field is not null))
        {
            if (!double.TryParse(row.Width, NumberStyles.Float, CultureInfo.CurrentCulture, out var width) || width < 20)
                throw new FormatException($"Column '{row.Field!.Name}' needs a width of at least 20.");
            columns.Add(new TableColumn(row.Field!.Key, width, string.IsNullOrWhiteSpace(row.Title) ? null : row.Title.Trim()));
        }
        if (columns.Count == 0)
            throw new FormatException("A table needs at least one column.");
        project.Tables.RemoveAll(t => t.Name == DefinitionName);
        project.Tables.Add(new TableDefinition(DefinitionName, columns.ToArray()));
    }

    protected override bool Remove(Project project, string name) => project.Tables.RemoveAll(t => t.Name == name) > 0;

    protected override void Apply(string name) => ViewModel.CurrentTableName = name;
}
