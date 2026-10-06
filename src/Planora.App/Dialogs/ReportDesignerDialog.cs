using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Fields;
using Planora.Engine.Reports;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>
/// Report designer: a custom report is a list of elements (text, project figures, task tables, task
/// charts, resource tables). Each element's settings are edited on the right.
/// </summary>
public sealed class ReportDesignerDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly string? _originalName;
    private readonly CustomReport _report;
    private readonly TextBox _name = Rows.Box(260);
    private readonly ListBox _elements = new() { Width = 230, Height = 380, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _properties = new() { Width = 380, Margin = new Thickness(14, 0, 0, 0) };
    private bool _loading;

    public ReportDesignerDialog(MainViewModel owner, CustomReport? existing)
    {
        _owner = owner;
        _originalName = existing?.Name;
        _report = existing?.Copy() ?? new CustomReport
        {
            Name = UniqueName(owner),
            Elements =
            {
                new CustomReportElement { Kind = CustomElementKind.ProjectFigures, Title = "Summary" },
                new CustomReportElement { Kind = CustomElementKind.TaskTable, Title = "Tasks" },
            },
        };
        Form.Setup(this, existing is null ? "New Report" : "Edit Report");
        _name.Text = _report.Name;
        _elements.SelectionChanged += (_, _) => ShowProperties();

        var add = new Button { Content = "Add ▾", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 4, 0) };
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<CustomElementKind>())
        {
            var item = new MenuItem { Header = KindName(kind) };
            item.Click += (_, _) => AddElement(kind);
            menu.Items.Add(item);
        }
        add.Click += (_, _) =>
        {
            menu.PlacementTarget = add;
            menu.IsOpen = true;
        };
        var remove = SmallButton("Remove", () =>
        {
            if (_elements.SelectedIndex is int i && i >= 0)
            {
                _report.Elements.RemoveAt(i);
                Refresh(Math.Min(i, _report.Elements.Count - 1));
            }
        });
        var up = SmallButton("Up", () => Move(-1));
        var down = SmallButton("Down", () => Move(1));

        var left = new StackPanel
        {
            Children =
            {
                Rows.Row(Rows.Label("Report name:"), _name),
                new TextBlock { Text = "Elements, in order:", Margin = new Thickness(0, 8, 0, 2) },
                _elements,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { add, remove, up, down } },
            },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { left, _properties } },
                Form.Buttons(this, Apply),
            },
        };
        Refresh(0);
    }

    /// <summary>The report as saved (after OK).</summary>
    public string ReportName => _report.Name;

    private static string UniqueName(MainViewModel owner)
    {
        for (int i = 1; ; i++)
        {
            var name = $"Custom Report {i}";
            if (!ReportCatalog.AllNames(owner.Project).Contains(name))
                return name;
        }
    }

    private static string KindName(CustomElementKind kind) => kind switch
    {
        CustomElementKind.ProjectFigures => "Project figures",
        CustomElementKind.TaskTable => "Task table",
        CustomElementKind.TaskChart => "Task chart",
        CustomElementKind.ResourceTable => "Resource table",
        _ => "Text",
    };

    private static Button SmallButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 4, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddElement(CustomElementKind kind)
    {
        var element = new CustomReportElement { Kind = kind, Title = KindName(kind) };
        if (kind == CustomElementKind.TaskChart)
            element.Title = "";
        int at = _elements.SelectedIndex >= 0 ? _elements.SelectedIndex + 1 : _report.Elements.Count;
        _report.Elements.Insert(at, element);
        Refresh(at);
    }

    private void Move(int step)
    {
        int i = _elements.SelectedIndex;
        int j = i + step;
        if (i < 0 || j < 0 || j >= _report.Elements.Count)
            return;
        (_report.Elements[i], _report.Elements[j]) = (_report.Elements[j], _report.Elements[i]);
        Refresh(j);
    }

    private void Refresh(int select)
    {
        _loading = true;
        _elements.ItemsSource = _report.Elements.Select(e => $"{KindName(e.Kind)}{(e.Title.Length > 0 ? ": " + e.Title : "")}").ToList();
        _loading = false;
        _elements.SelectedIndex = select;
        ShowProperties();
    }

    private void ShowProperties()
    {
        if (_loading)
            return;
        _properties.Children.Clear();
        if (_elements.SelectedIndex < 0 || _elements.SelectedIndex >= _report.Elements.Count)
        {
            _properties.Children.Add(new TextBlock { Text = "Add an element (Add ▾) to build the report.", Foreground = System.Windows.Media.Brushes.Gray });
            return;
        }
        var element = _report.Elements[_elements.SelectedIndex];
        var project = _owner.Project;
        _properties.Children.Add(Rows.Heading(KindName(element.Kind)));

        var title = Rows.Box(260);
        title.Text = element.Title;
        title.TextChanged += (_, _) =>
        {
            element.Title = title.Text;
            int keep = _elements.SelectedIndex;
            _loading = true;
            _elements.ItemsSource = _report.Elements.Select(e => $"{KindName(e.Kind)}{(e.Title.Length > 0 ? ": " + e.Title : "")}").ToList();
            _elements.SelectedIndex = keep;
            _loading = false;
        };
        _properties.Children.Add(Rows.Row(Rows.Label("Title:", 90), title));

        switch (element.Kind)
        {
            case CustomElementKind.Text:
                var text = new TextBox { Width = 360, Height = 160, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = element.Text, Margin = new Thickness(0, 4, 0, 0) };
                text.TextChanged += (_, _) => element.Text = text.Text;
                _properties.Children.Add(text);
                break;

            case CustomElementKind.TaskTable:
            case CustomElementKind.TaskChart:
                var filter = new ComboBox { Width = 220, ItemsSource = ViewCatalog.Filters(project).Where(f => !f.IsInteractive).Select(f => f.Name).ToList(), SelectedItem = element.Filter };
                filter.SelectionChanged += (_, _) => element.Filter = filter.SelectedItem as string ?? "All Tasks";
                _properties.Children.Add(Rows.Row(Rows.Label("Filter:", 90), filter));
                var maxRows = Rows.Box(60);
                maxRows.Text = element.MaxRows == 0 ? "" : element.MaxRows.ToString(CultureInfo.CurrentCulture);
                maxRows.TextChanged += (_, _) => element.MaxRows = int.TryParse(maxRows.Text, out var n) && n > 0 ? n : 0;
                _properties.Children.Add(Rows.Row(Rows.Label("Show at most:", 90), maxRows, Rows.Label("rows (empty = all)")));
                if (element.Kind == CustomElementKind.TaskTable)
                    AddFieldPicker(element);
                else
                    AddChartSettings(element);
                break;

            case CustomElementKind.ProjectFigures:
                _properties.Children.Add(new TextBlock { Text = "Shows the project's start, finish, % complete, work, cost and task count.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                break;

            case CustomElementKind.ResourceTable:
                _properties.Children.Add(new TextBlock { Text = "Lists every resource with its type, work, cost and peak units; over-allocated resources are highlighted.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                break;
        }
    }

    private void AddFieldPicker(CustomReportElement element)
    {
        var project = _owner.Project;
        var all = TaskFields.All(project).ToList();
        var list = new ListBox { Height = 230, Width = 360, Margin = new Thickness(0, 6, 0, 0) };
        // Chosen fields first, in their order, then the rest.
        var ordered = element.Fields.Select(k => all.FirstOrDefault(f => f.Key == k)).OfType<FieldDefinition>()
            .Concat(all.Where(f => !element.Fields.Contains(f.Key))).ToList();
        foreach (var field in ordered)
        {
            var box = new CheckBox { Content = field.Name, IsChecked = element.Fields.Contains(field.Key), Tag = field.Key };
            box.Checked += (_, _) =>
            {
                if (!element.Fields.Contains(field.Key))
                    element.Fields.Add(field.Key);
            };
            box.Unchecked += (_, _) => element.Fields.Remove(field.Key);
            list.Items.Add(box);
        }
        _properties.Children.Add(new TextBlock { Text = "Columns (ticked fields, in the order ticked):", Margin = new Thickness(0, 8, 0, 0) });
        _properties.Children.Add(list);
    }

    private void AddChartSettings(CustomReportElement element)
    {
        var project = _owner.Project;
        var numeric = TaskFields.All(project)
            .Where(f => f.Type is FieldType.Number or FieldType.Cost or FieldType.Percent or FieldType.Work or FieldType.Duration)
            .Select(f => new KeyChoice(f.Key, f.Name)).ToList();
        var field = new ComboBox { Width = 220, ItemsSource = numeric, DisplayMemberPath = nameof(KeyChoice.Name), SelectedValuePath = nameof(KeyChoice.Key), SelectedValue = element.ChartField };
        field.SelectionChanged += (_, _) => element.ChartField = field.SelectedValue as string ?? "Cost";
        _properties.Children.Add(Rows.Row(Rows.Label("Chart field:", 90), field));
        var seconds = new List<KeyChoice> { new("", "(none)") };
        seconds.AddRange(numeric);
        var second = new ComboBox { Width = 220, ItemsSource = seconds, DisplayMemberPath = nameof(KeyChoice.Name), SelectedValuePath = nameof(KeyChoice.Key), SelectedValue = element.SecondChartField ?? "" };
        second.SelectionChanged += (_, _) => element.SecondChartField = second.SelectedValue is string { Length: > 0 } s ? s : null;
        _properties.Children.Add(Rows.Row(Rows.Label("Compare with:", 90), second));
        var top = new CheckBox { Content = "Top-level tasks (phases) only; otherwise the filtered subtasks", IsChecked = element.TopLevelOnly, Margin = new Thickness(0, 6, 0, 0) };
        top.Checked += (_, _) => element.TopLevelOnly = true;
        top.Unchecked += (_, _) => element.TopLevelOnly = false;
        _properties.Children.Add(top);
    }

    private void Apply()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            Form.Warn(this, "Enter a name for the report.");
            return;
        }
        if (name != _originalName && ReportCatalog.AllNames(_owner.Project).Contains(name))
        {
            Form.Warn(this, $"There is already a report called '{name}'.");
            return;
        }
        _report.Name = name;
        var saved = _report.Copy();
        var original = _originalName;
        if (_owner.TryEdit(original is null ? "New report" : "Edit report", p =>
            {
                int index = original is null ? -1 : p.CustomReports.FindIndex(r => r.Name == original);
                if (index >= 0)
                    p.CustomReports[index] = saved;
                else
                    p.CustomReports.Add(saved);
            }))
            DialogResult = true;
    }
}
