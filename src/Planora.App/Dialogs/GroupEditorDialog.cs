using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Group definition: fields to group by (outermost first), order, and optional intervals.</summary>
public sealed class GroupEditorDialog : DefinitionEditor<GroupEditorDialog.Row>
{
    public sealed class Row
    {
        public FieldChoice? Field { get; set; }
        public string Order { get; set; } = "Ascending";
        public string? Interval { get; set; }
        public string Dates { get; set; } = "Day";
    }

    private readonly CheckBox _showSummaries = new() { Content = "Show summary tasks" };

    public GroupEditorDialog(MainViewModel owner)
        : base(owner, "Group Definition", owner.GroupNames, owner.CurrentGroupName)
    {
        AddFieldColumn("Group By", nameof(Row.Field), 250);
        AddChoiceColumn("Order", nameof(Row.Order), new[] { "Ascending", "Descending" }, 100);
        AddTextColumn("Interval", nameof(Row.Interval), 110);
        AddChoiceColumn("Dates By", nameof(Row.Dates), Enum.GetNames<DateGrouping>(), 90);
        AddOption(_showSummaries);
        AddOption(new TextBlock
        {
            Text = "Interval groups numbers, durations or work into bands, e.g. 5d or 10. Dates By applies to date fields.",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 6, 0, 0),
        });
    }

    protected override void Load(string? name)
    {
        if (name is null)
        {
            _showSummaries.IsChecked = false;
            return;
        }
        var group = ViewCatalog.Group(ViewModel.Project, name);
        _showSummaries.IsChecked = group.ShowSummaryTasks;
        var options = ViewModel.Project.Options;
        foreach (var level in group.Levels)
        {
            var field = TaskFields.Find(ViewModel.Project, level.FieldKey);
            string? interval = level.Interval <= 0 ? null
                : field?.Type is FieldType.Duration or FieldType.Work ? FieldValue.Format((long)level.Interval, field.Type, options)
                : level.Interval.ToString(CultureInfo.CurrentCulture);
            Rows.Add(new Row
            {
                Field = Choice(level.FieldKey),
                Order = level.Descending ? "Descending" : "Ascending",
                Interval = interval,
                Dates = level.DateGrouping.ToString(),
            });
        }
    }

    protected override void Store(Project project)
    {
        var levels = new List<GroupLevel>();
        foreach (var row in Rows.Where(r => r.Field is not null))
        {
            var field = TaskFields.Get(project, row.Field!.Key);
            double interval = 0;
            if (!string.IsNullOrWhiteSpace(row.Interval))
            {
                var type = field.Type is FieldType.Duration or FieldType.Work ? field.Type : FieldType.Number;
                if (!FieldValue.TryParse(row.Interval, type, project.Options, out var value) || value is null)
                    throw new FormatException($"'{row.Interval}' is not a valid interval for {field.Name}.");
                interval = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (interval <= 0)
                    throw new FormatException("Intervals must be greater than zero.");
            }
            levels.Add(new GroupLevel(field.Key, row.Order == "Descending", interval, Enum.Parse<DateGrouping>(row.Dates)));
        }
        if (levels.Count == 0)
            throw new FormatException("Choose at least one field to group by.");
        project.Groups.RemoveAll(g => g.Name == DefinitionName);
        project.Groups.Add(new GroupDefinition(DefinitionName, levels.ToArray()) { ShowSummaryTasks = _showSummaries.IsChecked == true });
    }

    protected override bool Remove(Project project, string name) => project.Groups.RemoveAll(g => g.Name == name) > 0;

    protected override void Apply(string name) => ViewModel.CurrentGroupName = name;
}
