using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Views;

/// <summary>The Gantt Chart view: the task sheet on the left and the chart on the right, scrolled together.</summary>
public partial class GanttView : UserControl
{
    private ScrollViewer? _sheetScroller;
    private MainViewModel? _viewModel;

    public GanttView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Loaded += OnLoaded;

        Gantt.ExtentChanged += (_, _) => UpdateGanttScrollBar();
        Gantt.SizeChanged += (_, _) => UpdateGanttScrollBar();
        Gantt.TaskClicked += (_, task) => SelectTask(task);
        Gantt.TaskDoubleClicked += (_, _) => _viewModel?.TaskInformationCommand.Execute(null);
        Gantt.WheelScrolled += (_, delta) => _sheetScroller?.ScrollToVerticalOffset(_sheetScroller.VerticalOffset - Math.Sign(delta) * 3);
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToTaskRequested -= OnScrollToTask;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = viewModel;
        Gantt.ViewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.ScrollToTaskRequested += OnScrollToTask;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Document))
            ScrollGanttToProjectStart();
    }

    private void OnScrollToTask(object? sender, EventArgs e)
    {
        if (_viewModel?.SelectedTasks.FirstOrDefault() is { } task)
            Gantt.ScrollTo(task.Start);
        UpdateGanttScrollBar();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_sheetScroller is null)
        {
            _sheetScroller = VisualTreeSearch.FindChild<ScrollViewer>(TaskSheet);
            if (_sheetScroller is not null)
                _sheetScroller.ScrollChanged += (_, _) => Gantt.VerticalOffset = _sheetScroller.VerticalOffset * Gantt.RowHeight;
            ScrollGanttToProjectStart();
        }
        RestoreSelection();
    }

    private void ScrollGanttToProjectStart()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel is null)
                return;
            var project = _viewModel.Project;
            Gantt.ScrollTo(project.Tasks.Count > 0 ? project.Start : project.StartDate);
            UpdateGanttScrollBar();
        });
    }

    // ---- Task sheet ----

    private void OnSheetSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel?.SetSelection(TaskSheet.SelectedItems.OfType<TaskRowViewModel>().Select(r => r.Task).OfType<ProjectTask>());

    /// <summary>Shows the view model's selection when returning to this view.</summary>
    private void RestoreSelection()
    {
        if (_viewModel is null)
            return;
        var selected = _viewModel.SelectedTasks.ToHashSet();
        TaskSheet.SelectedItems.Clear();
        foreach (var row in _viewModel.Rows.Where(r => r.Task is not null && selected.Contains(r.Task)))
            TaskSheet.SelectedItems.Add(row);
    }

    private void SelectTask(ProjectTask task)
    {
        var row = _viewModel?.Rows.FirstOrDefault(r => r.Task == task);
        if (row is null)
            return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            TaskSheet.SelectedItems.Clear();
        TaskSheet.SelectedItems.Add(row);
        TaskSheet.ScrollIntoView(row);
    }

    private void OnSheetPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || Keyboard.FocusedElement is TextBox)
            return;
        if (e.Key == Key.Delete && _viewModel.DeleteTaskCommand.CanExecute(null))
        {
            _viewModel.DeleteTaskCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            _viewModel.TaskInformationCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSheetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking the row number opens Task Information, as in Microsoft Project.
        if (e.OriginalSource is FrameworkElement { DataContext: TaskRowViewModel { IsPlaceholder: false } } element &&
            VisualTreeSearch.FindParent<DataGridCell>(element) is { Column.DisplayIndex: 0 })
        {
            _viewModel?.TaskInformationCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The blank row only accepts a task name.</summary>
    private void OnSheetBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is TaskRowViewModel { IsPlaceholder: true } && e.Column.Header as string != "Task Name")
            e.Cancel = true;
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    // ---- Gantt scrolling ----

    private void OnGanttScroll(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Math.Abs(Gantt.HorizontalOffset - e.NewValue) > 0.5)
            Gantt.HorizontalOffset = e.NewValue;
    }

    private void UpdateGanttScrollBar()
    {
        GanttScroll.Minimum = 0;
        GanttScroll.Maximum = Math.Max(0, Gantt.ExtentWidth - Gantt.ActualWidth);
        GanttScroll.ViewportSize = Gantt.ActualWidth;
        GanttScroll.SmallChange = 20;
        GanttScroll.LargeChange = Math.Max(20, Gantt.ActualWidth * 0.9);
        GanttScroll.Value = Math.Min(Gantt.HorizontalOffset, GanttScroll.Maximum);
    }
}
