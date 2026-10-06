using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Planora.App.ViewModels;

namespace Planora.App.Dialogs;

/// <summary>
/// Shared layout for the table, filter and group editors: pick a definition (or start a new one),
/// edit its name and rows in a grid, then save it to the project and apply it.
/// </summary>
public abstract class DefinitionEditor<TRow> : Window where TRow : class, new()
{
    private readonly ComboBox _picker;
    private readonly TextBox _name;
    private readonly DataGrid _grid;
    private readonly StackPanel _options;

    protected DefinitionEditor(MainViewModel owner, string title, IReadOnlyList<string> names, string current)
    {
        Owner = Application.Current.MainWindow;
        ViewModel = owner;
        Title = title;
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Choices = FieldChoice.All(owner.Project);

        _picker = new ComboBox { ItemsSource = names, Width = 240, Margin = new Thickness(0, 0, 8, 0) };
        _name = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 2, 0, 8) };
        var newButton = new Button { Content = "New", Padding = new Thickness(10, 2, 10, 2) };
        newButton.Click += (_, _) =>
        {
            _picker.SelectedItem = null;
            _name.Text = "New " + title.Split(' ').Last();
            Rows.Clear();
            Load(null);
        };
        _grid = new DataGrid
        {
            ItemsSource = Rows,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.All,
            RowHeight = 24,
            Height = 260,
            Width = 620,
        };
        _options = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var save = new Button { Content = "Save and Apply", IsDefault = true, MinWidth = 110, Padding = new Thickness(10, 4, 10, 4) };
        var delete = new Button { Content = "Delete", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        save.Click += (_, _) => Save();
        delete.Click += (_, _) => Delete();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { _picker, newButton } },
                new TextBlock { Text = "Name:" },
                _name,
                _grid,
                _options,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { save, delete, cancel } },
            },
        };

        _picker.SelectionChanged += (_, _) =>
        {
            if (_picker.SelectedItem is string name)
            {
                _name.Text = name;
                Rows.Clear();
                Load(name);
            }
        };
        Loaded += (_, _) => _picker.SelectedItem = names.Contains(current) ? current : names.FirstOrDefault();
    }

    protected MainViewModel ViewModel { get; }
    protected List<FieldChoice> Choices { get; }
    protected ObservableCollection<TRow> Rows { get; } = new();
    protected string DefinitionName => _name.Text.Trim();

    protected void AddFieldColumn(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = header, ItemsSource = Choices, SelectedItemBinding = new Binding(path), Width = width });

    protected void AddChoiceColumn(string header, string path, IEnumerable<string> items, double width) =>
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = header, ItemsSource = items.ToList(), SelectedItemBinding = new Binding(path), Width = width });

    protected void AddTextColumn(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });

    protected void AddOption(UIElement element) => _options.Children.Add(element);

    protected FieldChoice? Choice(string key) => Choices.FirstOrDefault(c => c.Key == key);

    /// <summary>Fills <see cref="Rows"/> and options from the named definition (null for a new one).</summary>
    protected abstract void Load(string? name);

    /// <summary>Validates the rows and stores the definition in the project (inside an undoable edit).</summary>
    protected abstract void Store(Engine.Model.Project project);

    /// <summary>Removes the definition from the project; built-ins cannot be deleted.</summary>
    protected abstract bool Remove(Engine.Model.Project project, string name);

    /// <summary>Makes the saved definition the current one.</summary>
    protected abstract void Apply(string name);

    private void Save()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (DefinitionName.Length == 0)
        {
            MessageBox.Show(this, "Enter a name.", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!ViewModel.TryEdit(Title, Store))
            return;
        ViewModel.DefinitionsChanged();
        Apply(DefinitionName);
        DialogResult = true;
    }

    private void Delete()
    {
        if (_picker.SelectedItem is not string name)
            return;
        bool removed = false;
        if (!ViewModel.TryEdit("Delete " + name, p => removed = Remove(p, name)))
            return;
        if (!removed)
        {
            MessageBox.Show(this, $"'{name}' is built in and cannot be deleted.", "Planora", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ViewModel.DefinitionsChanged();
        DialogResult = true;
    }
}
