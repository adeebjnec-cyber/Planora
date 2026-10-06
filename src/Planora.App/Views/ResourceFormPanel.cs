using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Views;

/// <summary>
/// Resource Form (View > Details in a resource view): the selected resource's name, units and rates with
/// its assignments, under the Resource Sheet, Resource Usage or Resource Graph.
/// </summary>
public sealed class ResourceFormPanel : UserControl
{
    private readonly TextBox _name = Box(220), _initials = Box(60), _maxUnits = Box(70), _rate = Box(90), _overtime = Box(90), _perUse = Box(80), _group = Box(120);
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Brushes.DimGray };
    private readonly DataGrid _assignments = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = 22,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        Background = Brushes.White,
        Margin = new Thickness(8, 2, 8, 8),
    };
    private MainViewModel? _viewModel;
    private int? _resourceId;

    public ResourceFormPanel()
    {
        Background = Controls.Drawing.HeaderBackground;
        BorderBrush = Brushes.LightGray;
        BorderThickness = new Thickness(0, 1, 0, 0);
        foreach (var (header, path, width) in new[] { ("Task", "Task", 240.0), ("Units", "Units", 70.0), ("Work", "Work", 80.0), ("Start", "Start", 120.0), ("Finish", "Finish", 120.0) })
            _assignments.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width });

        var ok = new Button { Content = "OK", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) => Apply();
        var info = new Button { Content = "Resource Information...", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        info.Click += (_, _) => _viewModel?.ResourceInformationCommand.Execute(null);

        var top = new WrapPanel { Margin = new Thickness(8, 6, 8, 2) };
        foreach (var (label, box) in new (string, TextBox)[] { ("Name:", _name), ("Initials:", _initials), ("Max units:", _maxUnits), ("Group:", _group) })
        {
            top.Children.Add(Label(label));
            top.Children.Add(box);
        }
        top.Children.Add(ok);
        top.Children.Add(info);
        var rates = new WrapPanel { Margin = new Thickness(8, 0, 8, 4) };
        foreach (var (label, box) in new (string, TextBox)[] { ("Std. rate:", _rate), ("Ovt. rate:", _overtime), ("Cost/use:", _perUse) })
        {
            rates.Children.Add(Label(label));
            rates.Children.Add(box);
        }
        rates.Children.Add(_summary);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(rates, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(rates);
        root.Children.Add(_assignments);
        Content = root;

        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        IsVisibleChanged += (_, _) => Load();
    }

    private static TextBox Box(double width) => new() { Width = width, Margin = new Thickness(0, 1, 12, 1), Padding = new Thickness(2, 1, 2, 1) };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    private void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.Refreshed -= OnChanged;
            _viewModel.ResourceSelectionChanged -= OnChanged;
        }
        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.Refreshed += OnChanged;
            viewModel.ResourceSelectionChanged += OnChanged;
        }
        Load();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (IsVisible)
            Load();
    }

    private void Load()
    {
        var project = _viewModel?.Project;
        var resource = _viewModel?.SelectedResources.FirstOrDefault() is { } r ? project?.GetResource(r.Id) : null;
        IsEnabled = resource is not null;
        _resourceId = resource?.Id;
        if (project is null || resource is null)
        {
            foreach (var box in new[] { _name, _initials, _maxUnits, _rate, _overtime, _perUse, _group })
                box.Text = "";
            _summary.Text = "Select a resource in the Resource Sheet to see its details.";
            _assignments.ItemsSource = null;
            return;
        }
        var c = CultureInfo.CurrentCulture;
        _name.Text = resource.Name;
        _initials.Text = resource.Initials;
        _group.Text = resource.Group;
        _maxUnits.Text = resource.Type == ResourceType.Work ? $"{resource.MaxUnits * 100:0.#}%" : "";
        _maxUnits.IsEnabled = resource.Type == ResourceType.Work;
        _rate.Text = resource.StandardRate.ToString("0.##", c);
        _overtime.Text = resource.OvertimeRate.ToString("0.##", c);
        _perUse.Text = resource.CostPerUse.ToString("0.##", c);
        _summary.Text = $"{resource.Type}  ·  Work {DurationText.Format(resource.WorkMinutes, project.Options, DurationUnit.Hours)}  ·  Cost {resource.Cost.ToString("C", c)}"
                        + (resource.IsOverallocated ? "  ·  over-allocated" : "");
        _assignments.ItemsSource = project.AssignmentsOf(resource).OrderBy(a => a.Start).Select(a => new
        {
            Task = a.Task.Name,
            Units = resource.Type == ResourceType.Work ? $"{a.Units * 100:0}%" : a.Units.ToString("0.##", c),
            Work = a.IsWork ? DurationText.Format(a.WorkMinutes, project.Options, DurationUnit.Hours) : "",
            Start = DateText.Format(a.Start),
            Finish = DateText.Format(a.Finish),
        }).ToList();
    }

    private void Apply()
    {
        if (_viewModel is null || _resourceId is not { } id)
            return;
        var c = CultureInfo.CurrentCulture;
        bool ParseMoney(TextBox box, out decimal value) => decimal.TryParse(box.Text, NumberStyles.Currency, c, out value) && value >= 0;
        if (!ParseMoney(_rate, out var rate) || !ParseMoney(_overtime, out var overtime) || !ParseMoney(_perUse, out var perUse))
        {
            _viewModel.ShowError("Rates must be amounts of 0 or more.");
            return;
        }
        double? maxUnits = null;
        if (_maxUnits.IsEnabled)
        {
            if (!double.TryParse(_maxUnits.Text.Trim().TrimEnd('%'), NumberStyles.Float, c, out var percent) || percent <= 0)
            {
                _viewModel.ShowError("Max units must be a percentage above 0, e.g. 100%.");
                return;
            }
            maxUnits = percent / 100;
        }
        string name = _name.Text.Trim(), initials = _initials.Text.Trim(), group = _group.Text.Trim();
        _viewModel.TryEdit("Resource form", p =>
        {
            var r = p.GetResource(id) ?? throw new InvalidOperationException("The resource no longer exists.");
            if (name.Length > 0)
                r.Name = name;
            r.Initials = initials;
            r.Group = group;
            r.StandardRate = rate;
            r.OvertimeRate = overtime;
            r.CostPerUse = perUse;
            if (maxUnits is { } units)
                r.MaxUnits = units;
        });
    }
}
