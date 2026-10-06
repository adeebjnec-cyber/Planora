using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Find (Ctrl+F) and Replace (Ctrl+H): search any task field and replace text in text fields.</summary>
public sealed class FindReplaceDialog : Window
{
    private static readonly (string Label, FilterOperator Op)[] Tests =
    {
        ("contains", FilterOperator.Contains),
        ("equals", FilterOperator.Equals),
        ("does not contain", FilterOperator.DoesNotContain),
        ("does not equal", FilterOperator.NotEquals),
        ("is greater than", FilterOperator.GreaterThan),
        ("is less than", FilterOperator.LessThan),
    };

    private readonly MainViewModel _owner;
    private readonly TextBox _find = Rows.Box(260), _replace = Rows.Box(260);
    private readonly ComboBox _field = new() { Width = 200, Margin = new Thickness(0, 2, 12, 2) };
    private readonly ComboBox _test = new() { Width = 140, Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _matchCase = new() { Content = "Match case", Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _replaceRow;
    private readonly Button _replaceButton, _replaceAllButton;

    public FindReplaceDialog(MainViewModel owner, bool replace)
    {
        _owner = owner;
        Form.Setup(this, replace ? "Replace" : "Find");
        var fields = TaskFields.All(owner.Project).ToList();
        _field.ItemsSource = fields;
        _field.DisplayMemberPath = nameof(FieldDefinition.Name);
        _field.SelectedItem = fields.First(f => f.Key == "Name");
        _test.ItemsSource = Tests.Select(t => t.Label).ToList();
        _test.SelectedIndex = 0;
        _field.SelectionChanged += (_, _) => UpdateReplaceState();

        var findNext = new Button { Content = "Find Next", IsDefault = true, MinWidth = 100, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 4) };
        var findPrevious = new Button { Content = "Find Previous", MinWidth = 100, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 4) };
        _replaceButton = new Button { Content = "Replace", MinWidth = 100, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 4) };
        _replaceAllButton = new Button { Content = "Replace All", MinWidth = 100, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 4) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, Padding = new Thickness(8, 3, 8, 3) };
        findNext.Click += (_, _) => Find(true);
        findPrevious.Click += (_, _) => Find(false);
        _replaceButton.Click += (_, _) => ReplaceOne();
        _replaceAllButton.Click += (_, _) => ReplaceAll();

        _replaceRow = Rows.Row(Rows.Label("Replace with:", 90), _replace);
        if (!replace)
        {
            _replaceRow.Visibility = Visibility.Collapsed;
            _replaceButton.Visibility = _replaceAllButton.Visibility = Visibility.Collapsed;
        }

        var form = new StackPanel
        {
            Width = 380,
            Children =
            {
                Rows.Row(Rows.Label("Find what:", 90), _find),
                _replaceRow,
                Rows.Row(Rows.Label("Look in field:", 90), _field),
                Rows.Row(Rows.Label("Test:", 90), _test),
                _matchCase,
            },
        };
        var buttons = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { findNext, findPrevious, _replaceButton, _replaceAllButton, close } };
        Content = new DockPanel { Margin = new Thickness(14), Children = { form, buttons } };
        DockPanel.SetDock(buttons, Dock.Right);
        // DockPanel docks children in order: put the buttons first.
        ((DockPanel)Content).Children.Remove(buttons);
        ((DockPanel)Content).Children.Insert(0, buttons);

        Loaded += (_, _) => _find.Focus();
        UpdateReplaceState();
    }

    private FieldDefinition Field => (FieldDefinition)_field.SelectedItem;

    private void UpdateReplaceState()
    {
        bool canReplace = TaskSearch.CanReplace(Field.Key);
        _replaceButton.IsEnabled = _replaceAllButton.IsEnabled = canReplace;
        _replaceButton.ToolTip = canReplace ? null : "Replace works on text fields: Name, Notes, Hyperlink and Text1-30.";
    }

    private FilterCondition Condition => new(Field.Key, Tests[Math.Max(0, _test.SelectedIndex)].Op, _find.Text);

    private List<ProjectTask> VisibleTasks() => _owner.Rows.Select(r => r.Task).OfType<ProjectTask>().ToList();

    private ProjectTask? Find(bool forward)
    {
        if (_find.Text.Length == 0)
            return null;
        var current = _owner.SelectedTasks.Count > 0 ? _owner.SelectedTasks[forward ? ^1 : 0] : null;
        var found = TaskSearch.FindNext(_owner.Project, VisibleTasks(), current, Condition, forward, _matchCase.IsChecked == true);
        if (found is null)
        {
            MessageBox.Show(this, $"Planora cannot find '{_find.Text}' in {Field.Name}.", "Find", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        _owner.SelectTask(found);
        return found;
    }

    private void ReplaceOne()
    {
        var current = _owner.SelectedTasks.Count == 1 ? _owner.SelectedTasks[0] : null;
        if (current is null || !TaskSearch.Matches(_owner.Project, current, new FilterCondition(Field.Key, FilterOperator.Contains, _find.Text), _matchCase.IsChecked == true))
        {
            Find(true);
            return;
        }
        int id = current.Id;
        string key = Field.Key, find = _find.Text, replacement = _replace.Text;
        bool matchCase = _matchCase.IsChecked == true;
        _owner.TryEdit("Replace", p => TaskSearch.Replace(p, p.GetRequiredTask(id), key, find, replacement, matchCase));
        Find(true);
    }

    private void ReplaceAll()
    {
        if (_find.Text.Length == 0)
            return;
        string key = Field.Key, find = _find.Text, replacement = _replace.Text;
        bool matchCase = _matchCase.IsChecked == true;
        var ids = VisibleTasks().Select(t => t.Id).ToList();
        int count = 0;
        _owner.TryEdit("Replace all", p => count = TaskSearch.ReplaceAll(p, ids.Select(p.GetRequiredTask), key, find, replacement, matchCase));
        MessageBox.Show(this, count == 0 ? $"'{find}' was not found." : $"Replaced {count} occurrence(s).", "Replace", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
