using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Planora.App.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Views;

/// <summary>
/// Hosts a timeline-based view (Resource Graph, Team Planner) with a horizontal scroll bar, and for the
/// Resource Graph a resource picker.
/// </summary>
public sealed class TimelineView : UserControl
{
    private readonly TimelineElement _element;
    private readonly ScrollBar _scroll = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox? _picker;
    private MainViewModel? _viewModel;

    public TimelineView(TimelineElement element)
    {
        _element = element;
        var root = new DockPanel { Background = Drawing.Background };
        DockPanel.SetDock(_scroll, Dock.Bottom);
        root.Children.Add(_scroll);

        if (element is ResourceGraph graph)
        {
            _picker = new ComboBox { Width = 220, DisplayMemberPath = nameof(Resource.Name), Margin = new Thickness(6, 4, 0, 4) };
            _picker.SelectionChanged += (_, _) => graph.Resource = _picker.SelectedItem as Resource;
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Background = Drawing.HeaderBackground };
            bar.Children.Add(new TextBlock { Text = "Resource:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            bar.Children.Add(_picker);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
        }

        if (element is TeamPlanner)
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = element });
        else
            root.Children.Add(element);
        Content = root;

        _scroll.ValueChanged += (_, e) =>
        {
            if (Math.Abs(_element.HorizontalOffset - e.NewValue) > 0.5)
                _element.HorizontalOffset = e.NewValue;
        };
        _element.ExtentChanged += (_, _) => UpdateScrollBar();
        _element.SizeChanged += (_, _) => UpdateScrollBar();

        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Loaded += (_, _) =>
        {
            FillPicker();
            if (_viewModel is { } vm)
                Dispatcher.BeginInvoke(() =>
                {
                    _element.ScrollTo(vm.Project.Tasks.Count > 0 ? vm.Project.Start : vm.Project.StartDate);
                    // Keep the first bars clear of the Team Planner's resource-name column.
                    if (_element is TeamPlanner planner)
                        _element.HorizontalOffset -= planner.NameWidth;
                    UpdateScrollBar();
                });
        };
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.Refreshed -= OnRefreshed;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.Refreshed += OnRefreshed;
        switch (_element)
        {
            case ResourceGraph graph:
                graph.ViewModel = viewModel;
                break;
            case TeamPlanner planner:
                planner.ViewModel = viewModel;
                planner.TaskClicked += (_, t) => viewModel?.SetSelection(new[] { t });
                planner.TaskDoubleClicked += (_, _) => viewModel?.TaskInformationCommand.Execute(null);
                break;
        }
        FillPicker();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        FillPicker();
        _element.InvalidateMeasure();
    }

    /// <summary>Keeps the resource list current, preserving the choice (by ID, since undo replaces objects).</summary>
    private void FillPicker()
    {
        if (_picker is null || _viewModel is null)
            return;
        int? current = (_picker.SelectedItem as Resource)?.Id ?? _viewModel.SelectedResources.FirstOrDefault()?.Id;
        var resources = _viewModel.Project.Resources.Where(r => r.Type == ResourceType.Work && !r.IsBudget).ToList();
        _picker.ItemsSource = resources;
        _picker.SelectedItem = resources.FirstOrDefault(r => r.Id == current) ?? resources.FirstOrDefault();
    }

    private void UpdateScrollBar()
    {
        _scroll.Minimum = 0;
        _scroll.Maximum = Math.Max(0, _element.ExtentWidth - _element.ActualWidth);
        _scroll.ViewportSize = _element.ActualWidth;
        _scroll.SmallChange = 20;
        _scroll.LargeChange = Math.Max(20, _element.ActualWidth * 0.9);
        _scroll.Value = Math.Min(_element.HorizontalOffset, _scroll.Maximum);
    }
}
