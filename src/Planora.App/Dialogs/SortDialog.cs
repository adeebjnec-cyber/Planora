using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Sort: up to three fields, each ascending or descending, with "keep outline structure".</summary>
public sealed class SortDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly List<(ComboBox Field, ComboBox Order)> _rows = new();
    private readonly CheckBox _keepOutline;

    public SortDialog(MainViewModel owner)
    {
        _owner = owner;
        Title = "Sort";
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var choices = new List<FieldChoice> { new("", "(none)") };
        choices.AddRange(FieldChoice.All(owner.Project));
        var panel = new StackPanel { Margin = new Thickness(14), Width = 420 };
        string[] labels = { "Sort by", "Then by", "Then by" };
        for (int i = 0; i < 3; i++)
        {
            var key = i < owner.CurrentSort.Keys.Count ? owner.CurrentSort.Keys[i] : null;
            var field = new ComboBox { ItemsSource = choices, Width = 260, Margin = new Thickness(0, 2, 8, 2) };
            field.SelectedItem = choices.FirstOrDefault(c => c.Key == (key?.FieldKey ?? (i == 0 ? "ID" : ""))) ?? choices[0];
            var order = new ComboBox { ItemsSource = new[] { "Ascending", "Descending" }, Width = 110, SelectedIndex = key?.Descending == true ? 1 : 0, Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(new TextBlock { Text = labels[i], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 2) });
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { field, order } });
            _rows.Add((field, order));
        }
        _keepOutline = new CheckBox { Content = "Keep outline structure", IsChecked = owner.CurrentSort.KeepOutlineStructure, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(_keepOutline);

        var sort = new Button { Content = "Sort", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        var reset = new Button { Content = "Reset", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        sort.Click += (_, _) => Apply();
        reset.Click += (_, _) =>
        {
            _owner.CurrentSort = new SortDefinition();
            DialogResult = true;
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { sort, reset, cancel } });
        Content = panel;
    }

    private void Apply()
    {
        var keys = _rows
            .Where(r => r.Field.SelectedItem is FieldChoice { Key.Length: > 0 })
            .Select(r => new SortKey(((FieldChoice)r.Field.SelectedItem).Key, r.Order.SelectedIndex == 1))
            .ToList();
        // Sorting by ID ascending alone is the natural order.
        if (keys.Count == 1 && keys[0] == new SortKey("ID"))
            keys.Clear();
        _owner.CurrentSort = new SortDefinition { Keys = keys, KeepOutlineStructure = _keepOutline.IsChecked == true };
        DialogResult = true;
    }
}
