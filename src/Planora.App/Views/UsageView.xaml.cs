using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Views;

/// <summary>
/// Task Usage (tasks with their assignments) or Resource Usage (resources with their assignments):
/// a sheet on the left and time-phased hours on the right.
/// </summary>
public partial class UsageView : UserControl
{
    private readonly bool _byResource;
    private ScrollViewer? _sheetScroller;
    private MainViewModel? _viewModel;

    public UsageView(bool byResource)
    {
        _byResource = byResource;
        InitializeComponent();
        NameColumn.Header = byResource ? "Resource Name" : "Task Name";
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Loaded += OnLoaded;

        TimeGrid.ExtentChanged += (_, _) => UpdateScrollBar();
        TimeGrid.SizeChanged += (_, _) => UpdateScrollBar();
        TimeGrid.WheelScrolled += (_, delta) => _sheetScroller?.ScrollToVerticalOffset(_sheetScroller.VerticalOffset - Math.Sign(delta) * 3);
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.Refreshed -= OnRefreshed;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.Refreshed += OnRefreshed;
        TimeGrid.ViewModel = viewModel;
        Rebuild();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        if (IsVisible)
            Rebuild();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rebuild();
        if (_sheetScroller is null)
        {
            _sheetScroller = VisualTreeSearch.FindChild<ScrollViewer>(Sheet);
            if (_sheetScroller is not null)
                _sheetScroller.ScrollChanged += (_, _) => TimeGrid.VerticalOffset = _sheetScroller.VerticalOffset * TimeGrid.RowHeight;
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel is { } vm)
                    TimeGrid.ScrollTo(vm.Project.Tasks.Count > 0 ? vm.Project.Start : vm.Project.StartDate);
            });
        }
    }

    private void Rebuild()
    {
        if (_viewModel is null)
            return;
        var rows = _byResource ? ResourceRows(_viewModel.Project) : TaskRows(_viewModel.Project);
        Sheet.ItemsSource = rows;
        TimeGrid.Rows = rows;
        TimeGrid.InvalidateVisual();
    }

    private static List<UsageRow> TaskRows(Project project)
    {
        var o = project.Options;
        var rows = new List<UsageRow>();
        foreach (var task in project.Tasks)
        {
            var assignments = project.Subtree(task).SelectMany(t => t.IsSummary ? Enumerable.Empty<Assignment>() : t.Assignments)
                .Where(a => a.IsWork).ToList();
            rows.Add(new UsageRow
            {
                Name = task.Name,
                Level = task.OutlineLevel - 1,
                IsGroup = true,
                Task = task,
                TotalWork = DurationText.Format(task.WorkMinutes, o, DurationUnit.Hours),
                WorkBetween = (from, to) => assignments.Sum(a => Timephased.WorkBetween(a, from, to)),
            });
            if (task.IsSummary)
                continue;
            foreach (var a in task.Assignments)
                rows.Add(AssignmentRow(a, a.Resource.Name, task.OutlineLevel, o));
        }
        return rows;
    }

    private static List<UsageRow> ResourceRows(Project project)
    {
        var o = project.Options;
        var rows = new List<UsageRow>();
        foreach (var resource in project.Resources.Where(r => !r.IsBudget))
        {
            var assignments = ResourceAnalysis.ScheduledAssignments(project, resource).ToList();
            var overallocations = ResourceAnalysis.FindOverallocations(project, resource).Select(x => (x.From, x.To)).ToList();
            bool material = resource.Type == ResourceType.Material;
            rows.Add(new UsageRow
            {
                Name = resource.Name,
                IsGroup = true,
                Resource = resource,
                Details = material ? "Units" : resource.Type == ResourceType.Cost ? "Cost" : "Work",
                TotalWork = material
                    ? $"{assignments.Sum(a => a.Units):0.##} {resource.MaterialLabel}".Trim()
                    : DurationText.Format(resource.WorkMinutes, o, DurationUnit.Hours),
                Overallocations = overallocations,
                WorkBetween = material
                    ? (from, to) => assignments.Sum(a => Timephased.MaterialBetween(a, from, to))
                    : (from, to) => assignments.Sum(a => Timephased.WorkBetween(a, from, to)),
                FormatValue = material ? FormatQuantity : UsageGrid.FormatHours,
            });
            foreach (var a in assignments)
                rows.Add(AssignmentRow(a, a.Task.Name, 1, o));
        }
        return rows;
    }

    private static UsageRow AssignmentRow(Assignment a, string name, int level, ScheduleOptions o)
    {
        bool material = a.Resource.Type == ResourceType.Material;
        return new UsageRow
        {
            Name = name,
            Level = level,
            Task = a.Task,
            Resource = a.Resource,
            Details = material ? "Units" : a.Resource.Type == ResourceType.Cost ? "Cost" : "Work",
            TotalWork = material
                ? $"{a.Units:0.##} {a.Resource.MaterialLabel}".Trim()
                : a.Resource.Type == ResourceType.Cost ? a.Cost.ToString("C", CultureInfo.CurrentCulture)
                : DurationText.Format(a.WorkMinutes, o, DurationUnit.Hours),
            WorkBetween = material
                ? (from, to) => Timephased.MaterialBetween(a, from, to)
                : (from, to) => Timephased.WorkBetween(a, from, to),
            FormatValue = material ? FormatQuantity : UsageGrid.FormatHours,
        };
    }

    private static string FormatQuantity(double quantity) => quantity.ToString("0.##", CultureInfo.CurrentCulture);

    private void OnHorizontalScroll(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Math.Abs(TimeGrid.HorizontalOffset - e.NewValue) > 0.5)
            TimeGrid.HorizontalOffset = e.NewValue;
    }

    private void UpdateScrollBar()
    {
        HorizontalScroll.Minimum = 0;
        HorizontalScroll.Maximum = Math.Max(0, TimeGrid.ExtentWidth - TimeGrid.ActualWidth);
        HorizontalScroll.ViewportSize = TimeGrid.ActualWidth;
        HorizontalScroll.SmallChange = 20;
        HorizontalScroll.LargeChange = Math.Max(20, TimeGrid.ActualWidth * 0.9);
        HorizontalScroll.Value = Math.Min(TimeGrid.HorizontalOffset, HorizontalScroll.Maximum);
    }
}
