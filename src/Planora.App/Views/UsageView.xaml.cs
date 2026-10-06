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
        Sheet.MouseDoubleClick += (_, _) =>
        {
            if (Sheet.SelectedItem is UsageRow { Assignment: { } assignment })
                _viewModel?.ShowAssignmentInformation(assignment);
        };

        TimeGrid.CellEditRequested += (_, e) => BeginCellEdit(e.Row, e.From, e.To, e.Cell);
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
        var rows = WithDetails(_byResource ? ResourceRows(_viewModel.Project) : TaskRows(_viewModel.Project), _viewModel.Project, _viewModel.UsageDetails);
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
            Assignment = a,
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

    /// <summary>Adds the chosen detail rows (actual, remaining and baseline work; cost, actual and baseline cost) under each row.</summary>
    private static List<UsageRow> WithDetails(List<UsageRow> rows, Project project, UsageDetail details)
    {
        if (details == UsageDetail.None)
            return rows;
        var o = project.Options;
        var result = new List<UsageRow>();
        foreach (var row in rows)
        {
            result.Add(row);
            // The assignments behind the row: one assignment, or all of a task's or resource's.
            var assignments = row.Assignment is { } single ? new List<Assignment> { single }
                : row.Task is { } task && row.Resource is null ? project.Subtree(task).Where(t => !t.IsSummary).SelectMany(t => t.Assignments).ToList()
                : row.Resource is { } resource ? ResourceAnalysis.ScheduledAssignments(project, resource).ToList()
                : new List<Assignment>();
            // Task rows also carry the fixed costs of the tasks under them.
            var fixedCostTasks = row.Assignment is null && row.Task is { } t2 && row.Resource is null
                ? project.Subtree(t2).Where(t => !t.IsSummary && t.FixedCost != 0).ToList()
                : new List<ProjectTask>();

            UsageRow Detail(string label, string total, Func<DateTime, DateTime, double> between, Func<double, string> format) => new()
            {
                Name = "",
                Level = row.Level,
                Task = row.Task,
                Resource = row.Resource,
                Details = label,
                TotalWork = total,
                WorkBetween = between,
                FormatValue = format,
            };

            string Hours(double minutes) => DurationText.Format((long)Math.Round(minutes), o, DurationUnit.Hours);
            if (details.HasFlag(UsageDetail.ActualWork))
                result.Add(Detail("Act. Work", Hours(assignments.Sum(a => (double)a.ActualWorkMinutes)), (f, t) => assignments.Sum(a => Timephased.ActualWorkBetween(a, f, t)), UsageGrid.FormatHours));
            if (details.HasFlag(UsageDetail.RemainingWork))
                result.Add(Detail("Rem. Work", Hours(assignments.Sum(a => (double)a.RemainingWorkMinutes)),
                    (f, t) => assignments.Sum(a => Timephased.WorkBetween(a, f, t) - Timephased.ActualWorkBetween(a, f, t)), UsageGrid.FormatHours));
            if (details.HasFlag(UsageDetail.BaselineWork))
                result.Add(Detail("Base. Work", Hours(assignments.Sum(a => (double)(a.Baselines[0]?.WorkMinutes ?? 0))), (f, t) => assignments.Sum(a => Timephased.BaselineWorkBetween(a, 0, f, t)), UsageGrid.FormatHours));
            if (details.HasFlag(UsageDetail.Cost))
            {
                decimal total = assignments.Sum(a => a.Cost) + fixedCostTasks.Sum(t => t.FixedCost);
                result.Add(Detail("Cost", total.ToString("C", CultureInfo.CurrentCulture),
                    (f, t) => (double)(assignments.Sum(a => Timephased.CostBetween(a, f, t)) + fixedCostTasks.Sum(x => Timephased.FixedCostBetween(x, x.Calendar ?? project.Calendar, f, t))), FormatMoney));
            }
            if (details.HasFlag(UsageDetail.ActualCost))
                result.Add(Detail("Act. Cost", assignments.Sum(a => a.ActualCost).ToString("C", CultureInfo.CurrentCulture),
                    (f, t) => (double)assignments.Sum(a => Timephased.ActualCostBetween(a, f, t)), FormatMoney));
            if (details.HasFlag(UsageDetail.BaselineCost))
                result.Add(Detail("Base. Cost", assignments.Sum(a => a.Baselines[0]?.Cost ?? 0).ToString("C", CultureInfo.CurrentCulture),
                    (f, t) => (double)assignments.Sum(a => Timephased.BaselineCostBetween(a, 0, f, t)), FormatMoney));
        }
        return result;
    }

    private static string FormatMoney(double amount) => Math.Abs(amount) < 0.005 ? "" : amount.ToString(Math.Abs(amount) >= 100 ? "C0" : "C", CultureInfo.CurrentCulture);

    private static string FormatQuantity(double quantity) => quantity.ToString("0.#", CultureInfo.CurrentCulture);

    // ---- Editing work in a period ----

    private (Assignment Assignment, DateTime From, DateTime To)? _editing;

    private void BeginCellEdit(UsageRow row, DateTime from, DateTime to, Rect cell)
    {
        if (row.Assignment is not { IsWork: true } assignment || row.Details != "Work")
        {
            if (_viewModel is not null)
                _viewModel.ShowError("Edit work on an assignment row (the rows under each task or resource).");
            return;
        }
        _editing = (assignment, from, to);
        CellEditor.Text = (row.WorkBetween(from, to) / 60).ToString("0.##", CultureInfo.CurrentCulture);
        Canvas.SetLeft(CellEditor, cell.X);
        Canvas.SetTop(CellEditor, cell.Y);
        CellEditor.Width = Math.Max(48, cell.Width);
        CellEditor.Height = cell.Height;
        CellEditor.Visibility = Visibility.Visible;
        CellEditor.Focus();
        CellEditor.SelectAll();
    }

    private void OnCellEditorKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitCellEdit();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            _editing = null;
            CellEditor.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void OnCellEditorLostFocus(object sender, RoutedEventArgs e) => CommitCellEdit();

    private void CommitCellEdit()
    {
        if (_editing is not { } edit || _viewModel is null)
            return;
        _editing = null;
        CellEditor.Visibility = Visibility.Collapsed;
        if (!DurationText.TryParse(CellEditor.Text, _viewModel.Project.Options, out var work, DurationUnit.Hours))
        {
            _viewModel.ShowError($"'{CellEditor.Text}' is not an amount of work. Type hours, e.g. 6 or 4.5h.");
            return;
        }
        _viewModel.SetTimephasedWork(edit.Assignment, edit.From, edit.To, work.Minutes);
    }
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
