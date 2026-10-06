using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Fields;

namespace Planora.App.Dialogs;

/// <summary>Custom Fields: give a Text/Number/Date/Flag/Cost/Duration slot an alias and an optional pick list.</summary>
public sealed class CustomFieldsDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly Dictionary<string, (string Alias, List<string> Values)> _edits = new();
    private readonly ComboBox _type;
    private readonly ListBox _slots;
    private readonly TextBox _alias;
    private readonly TextBox _values;
    private string? _current;

    public CustomFieldsDialog(MainViewModel owner)
    {
        _owner = owner;
        Title = "Custom Fields";
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var field in owner.Project.CustomFields)
            _edits[field.Key] = (field.Alias, field.ValueList.ToList());

        _type = new ComboBox { ItemsSource = CustomField.SlotCounts.Keys.ToList(), SelectedIndex = 0, Width = 160, Margin = new Thickness(0, 0, 0, 6) };
        _slots = new ListBox { Width = 220, Height = 300 };
        _alias = new TextBox { Width = 240, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(3, 2, 3, 2) };
        _values = new TextBox { Width = 240, Height = 200, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(3, 2, 3, 2) };

        _type.SelectionChanged += (_, _) => FillSlots();
        _slots.SelectionChanged += (_, _) => SelectSlot();

        var left = new StackPanel { Children = { new TextBlock { Text = "Type:" }, _type, _slots } };
        var right = new StackPanel
        {
            Margin = new Thickness(14, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = "Alias (display name):" }, _alias,
                new TextBlock { Text = "Allowed values, one per line (Text fields; leave empty for any value):", TextWrapping = TextWrapping.Wrap, Width = 240 },
                _values,
            },
        };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        ok.Click += (_, _) => Apply();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { left, right } },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { ok, cancel } },
            },
        };
        FillSlots();
    }

    private void FillSlots()
    {
        StoreCurrent();
        var type = (FieldType)_type.SelectedItem;
        _slots.ItemsSource = Enumerable.Range(1, CustomField.SlotCounts[type])
            .Select(i => $"{type}{i}")
            .Select(key => new FieldChoice(key, _edits.TryGetValue(key, out var e) && e.Alias.Length > 0 ? $"{key} ({e.Alias})" : key))
            .ToList();
        _slots.SelectedIndex = 0;
    }

    private void SelectSlot()
    {
        StoreCurrent();
        _current = (_slots.SelectedItem as FieldChoice)?.Key;
        var edit = _current is not null && _edits.TryGetValue(_current, out var e) ? e : ("", new List<string>());
        _alias.Text = edit.Item1;
        _values.Text = string.Join(Environment.NewLine, edit.Item2);
        _values.IsEnabled = _current is not null && CustomField.TypeOfKey(_current) == FieldType.Text;
    }

    private void StoreCurrent()
    {
        if (_current is null)
            return;
        var values = _values.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _edits[_current] = (_alias.Text.Trim(), values);
    }

    private void Apply()
    {
        StoreCurrent();
        var used = _edits.Where(e => e.Value.Alias.Length > 0 || e.Value.Values.Count > 0).ToList();
        if (_owner.TryEdit("Custom fields", p =>
            {
                p.CustomFields.Clear();
                foreach (var (key, (alias, values)) in used)
                    p.CustomFields.Add(new CustomField(key, alias) { ValueList = values });
            }))
        {
            _owner.DefinitionsChanged();
            DialogResult = true;
        }
    }
}
