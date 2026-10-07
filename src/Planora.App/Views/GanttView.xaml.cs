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

    /// <summary>False for the Task Sheet view: the task table alone, without the chart.</summary>
    public bool ShowChart
    {
        get => ChartArea.Visibility == Visibility.Visible;
        set
        {
            ChartArea.Visibility = Splitter.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Layout.ColumnDefinitions[1].Width = new GridLength(value ? 4 : 0);
            Layout.ColumnDefinitions[2].Width = value ? new GridLength(6, GridUnitType.Star) : new GridLength(0);
            Layout.ColumnDefinitions[2].MinWidth = value ? 200 : 0;
        }
    }

    public GanttView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        TaskSheet.PreviewMouseRightButtonUp += OnHeaderRightClick;
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
            _viewModel.SelectTaskRequested -= OnSelectTaskRequested;
            _viewModel.FillDownRequested -= OnFillDown;
            _viewModel.TableChanged -= OnTableChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = viewModel;
        Gantt.ViewModel = viewModel;
        RebuildColumns();
        if (viewModel is not null)
        {
            viewModel.ScrollToTaskRequested += OnScrollToTask;
            viewModel.SelectTaskRequested += OnSelectTaskRequested;
            viewModel.FillDownRequested += OnFillDown;
            viewModel.TableChanged += OnTableChanged;
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

    private void OnSelectTaskRequested(object? sender, ProjectTask task)
    {
        var row = _viewModel?.Rows.FirstOrDefault(r => r.Task == task);
        if (row is null)
            return;
        TaskSheet.SelectedItems.Clear();
        TaskSheet.SelectedItems.Add(row);
        TaskSheet.ScrollIntoView(row);
        OnScrollToTask(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fill Down: copies the top selected row's value in the current column to the other selected rows,
    /// through the same cell bindings typing would use.
    /// </summary>
    private void OnFillDown(object? sender, EventArgs e)
    {
        TaskSheet.CommitEdit(DataGridEditingUnit.Row, true);
        if (TaskSheet.CurrentColumn is not DataGridBoundColumn { IsReadOnly: false, Binding: System.Windows.Data.Binding binding })
        {
            _viewModel?.ShowError("Click an editable cell in the column to fill, then select the rows below it.");
            return;
        }
        var rows = TaskSheet.SelectedItems.OfType<TaskRowViewModel>().Where(r => r.Task is not null)
            .OrderBy(r => _viewModel!.Rows.IndexOf(r)).ToList();
        if (rows.Count < 2)
            return;
        string path = binding.Path.Path;
        var value = CellValue(rows[0], path);
        var ids = rows.Skip(1).Select(r => r.Task!.Id).ToList();
        foreach (var id in ids)
        {
            // Rows are rebuilt after each edit, so look each one up again.
            if (_viewModel!.Rows.FirstOrDefault(r => r.Task?.Id == id) is { } row)
                SetCellValue(row, path, value);
        }
    }

    private static string? CellValue(TaskRowViewModel row, string path) =>
        path.StartsWith('[') ? row[path.Trim('[', ']')] : typeof(TaskRowViewModel).GetProperty(path)?.GetValue(row)?.ToString();

    private static void SetCellValue(TaskRowViewModel row, string path, string? value)
    {
        if (path.StartsWith('['))
            row[path.Trim('[', ']')] = value ?? "";
        else if (typeof(TaskRowViewModel).GetProperty(path) is { CanWrite: true, PropertyType: var type } property && type == typeof(string))
            property.SetValue(row, value);
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
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.C or Key.X or Key.V)
        {
            var command = e.Key switch { Key.C => _viewModel.CopyCommand, Key.X => _viewModel.CutCommand, _ => _viewModel.PasteCommand };
            if (command.CanExecute(null))
                command.Execute(null);
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

    /// <summary>The blank row only accepts a task name; group header rows are read-only.</summary>
    private void OnSheetBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is TaskRowViewModel { IsGroupHeader: true } ||
            e.Row.Item is TaskRowViewModel { IsPlaceholder: true } && e.Column != NameColumn)
            e.Cancel = true;
    }

    // ---- Table columns ----

    /// <summary>Fields edited through the row's dedicated properties (with Microsoft Project's editing rules).</summary>
    private static readonly Dictionary<string, string> EditableProperties = new()
    {
        ["Duration"] = nameof(TaskRowViewModel.Duration),
        ["Start"] = nameof(TaskRowViewModel.Start),
        ["Finish"] = nameof(TaskRowViewModel.Finish),
        ["Predecessors"] = nameof(TaskRowViewModel.Predecessors),
        ["ResourceNames"] = nameof(TaskRowViewModel.ResourceNames),
        ["PercentComplete"] = nameof(TaskRowViewModel.PercentComplete),
    };

    private void OnTableChanged(object? sender, EventArgs e) => RebuildColumns();

    /// <summary>Replaces the table columns after the fixed ones (row number, indicators, mode) with the current table's.</summary>
    private void RebuildColumns()
    {
        if (_viewModel is null)
            return;
        TaskSheet.CommitEdit(DataGridEditingUnit.Row, true);
        while (TaskSheet.Columns.Count > FixedColumns)
            TaskSheet.Columns.RemoveAt(FixedColumns);

        var project = _viewModel.Project;
        var editorStyle = (Style)FindResource("CellEditor");
        _columnKeys.Clear();
        _columnKeys[NameColumn] = "Name";
        foreach (var column in _viewModel.CurrentTable.Columns)
        {
            var field = Engine.Fields.TaskFields.Find(project, column.FieldKey);
            if (field is null)
                continue;
            var header = column.Title ?? field.Name;
            if (field.Key == "Name")
            {
                NameColumn.Header = header;
                NameColumn.Width = Wider(column.Width);
                TaskSheet.Columns.Add(NameColumn);
                continue;
            }

            bool editable = EditableProperties.TryGetValue(field.Key, out var property) || field.IsCustom && !field.IsCalculated;
            var binding = new System.Windows.Data.Binding(property ?? $"[{field.Key}]");
            var gridColumn = new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = Wider(column.Width),
                IsReadOnly = !editable,
                EditingElementStyle = editorStyle,
            };
            _columnKeys[gridColumn] = field.Key;
            TaskSheet.Columns.Add(gridColumn);
        }
    }

    private readonly Dictionary<DataGridColumn, string> _columnKeys = new();

    /// <summary>Table widths were set for a smaller font; the sheet's type is a little wider.</summary>
    private static double Wider(double width) => Math.Round(width * 1.15);

    /// <summary>Row number, status, indicators and mode come before the table's own columns.</summary>
    private const int FixedColumns = 4;

    /// <summary>
    /// AutoFilter: right-click a column header to show only tasks with chosen values in that column
    /// (summary tasks above them stay visible).
    /// </summary>
    private void OnHeaderRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is not { } vm)
            return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is not System.Windows.Controls.Primitives.DataGridColumnHeader { Column: { } column } || !_columnKeys.TryGetValue(column, out var key))
            return;
        e.Handled = true;

        var project = vm.Project;
        var values = project.Tasks.Where(t => !t.IsSummary).Select(t => vm.AutoFilterText(t, key)).Distinct()
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
        var chosen = vm.AutoFilterValues(key);
        var menu = new ContextMenu();
        var all = new MenuItem { Header = "(All)", IsCheckable = true, IsChecked = chosen is null };
        all.Click += (_, _) => vm.SetAutoFilter(key, null);
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var value in values.Take(50))
        {
            var item = new MenuItem { Header = value.Length == 0 ? "(Blank)" : value, IsCheckable = true, IsChecked = chosen?.Contains(value) == true, StaysOpenOnClick = false };
            item.Click += (_, _) =>
            {
                var set = vm.AutoFilterValues(key)?.ToHashSet() ?? new HashSet<string>();
                if (!set.Add(value))
                    set.Remove(value);
                vm.SetAutoFilter(key, set.Count == 0 ? null : set);
            };
            menu.Items.Add(item);
        }
        if (values.Count > 50)
            menu.Items.Add(new MenuItem { Header = $"({values.Count - 50} more values; use Filter for these)", IsEnabled = false });
        if (vm.HasAutoFilter)
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear All AutoFilters" };
            clear.Click += (_, _) => vm.ClearAutoFilters();
            menu.Items.Add(clear);
        }
        menu.PlacementTarget = (UIElement)node;
        menu.IsOpen = true;
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
