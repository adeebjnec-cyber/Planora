using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Filter definition: tests joined with And/Or, e.g. "% Complete is less than 100".</summary>
public sealed class FilterEditorDialog : DefinitionEditor<FilterEditorDialog.Row>
{
    public sealed class Row
    {
        public string Join { get; set; } = "And";
        public FieldChoice? Field { get; set; }
        public string Test { get; set; } = "equals";
        public string? Value { get; set; }
    }

    private static readonly (string Text, FilterOperator Op)[] Tests =
    {
        ("equals", FilterOperator.Equals),
        ("does not equal", FilterOperator.NotEquals),
        ("is greater than", FilterOperator.GreaterThan),
        ("is greater than or equal to", FilterOperator.GreaterThanOrEqual),
        ("is less than", FilterOperator.LessThan),
        ("is less than or equal to", FilterOperator.LessThanOrEqual),
        ("contains", FilterOperator.Contains),
        ("does not contain", FilterOperator.DoesNotContain),
    };

    private readonly CheckBox _showSummaries = new() { Content = "Show related summary rows" };

    public FilterEditorDialog(MainViewModel owner)
        : base(owner, "Filter Definition", owner.FilterNames, owner.CurrentFilterName)
    {
        AddChoiceColumn("And/Or", nameof(Row.Join), new[] { "And", "Or" }, 70);
        AddFieldColumn("Field Name", nameof(Row.Field), 230);
        AddChoiceColumn("Test", nameof(Row.Test), Tests.Select(t => t.Text), 170);
        AddTextColumn("Value", nameof(Row.Value), 140);
        AddOption(_showSummaries);
        AddOption(new TextBlock
        {
            Text = "Values use the field's format: dates (e.g. today's date), durations (2d), flags (Yes/No), percentages (50).",
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
        var filter = ViewCatalog.Filter(ViewModel.Project, name);
        _showSummaries.IsChecked = filter.ShowRelatedSummaryRows;
        foreach (var c in filter.Conditions)
        {
            Rows.Add(new Row
            {
                Join = c.Or ? "Or" : "And",
                Field = Choice(c.FieldKey),
                Test = Tests.First(t => t.Op == c.Operator).Text,
                Value = c.Value,
            });
        }
    }

    protected override void Store(Project project)
    {
        var conditions = Rows.Where(r => r.Field is not null).Select((r, i) => new FilterCondition(
            r.Field!.Key,
            Tests.FirstOrDefault(t => t.Text == r.Test).Op,
            r.Value?.Trim() ?? "",
            Or: i > 0 && r.Join == "Or")).ToArray();
        project.Filters.RemoveAll(f => f.Name == DefinitionName);
        project.Filters.Add(new FilterDefinition(DefinitionName, conditions) { ShowRelatedSummaryRows = _showSummaries.IsChecked == true });
    }

    protected override bool Remove(Project project, string name) => project.Filters.RemoveAll(f => f.Name == name) > 0;

    protected override void Apply(string name) => ViewModel.CurrentFilterName = name;
}
