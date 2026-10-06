using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App;

public partial class MainWindow : Window
{
    private ScrollViewer? _sheetScroller;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        Gantt.ViewModel = ViewModel;
        Gantt.ExtentChanged += (_, _) => UpdateGanttScrollBar();
        Gantt.SizeChanged += (_, _) => UpdateGanttScrollBar();
        Gantt.TaskClicked += (_, task) => SelectTask(task);
        Gantt.WheelScrolled += (_, delta) => _sheetScroller?.ScrollToVerticalOffset(_sheetScroller.VerticalOffset - Math.Sign(delta) * 3);
        ViewModel.ScrollToTaskRequested += (_, _) =>
        {
            if (ViewModel.SelectedTasks.FirstOrDefault() is { } task)
                Gantt.ScrollTo(task.Start);
            UpdateGanttScrollBar();
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Document))
                ScrollGanttToProjectStart();
        };

        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _sheetScroller = FindChild<ScrollViewer>(TaskSheet);
        if (_sheetScroller is not null)
            _sheetScroller.ScrollChanged += (_, _) => Gantt.VerticalOffset = _sheetScroller.VerticalOffset * Gantt.RowHeight;
        ScrollGanttToProjectStart();
    }

    private void ScrollGanttToProjectStart()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Gantt.ScrollTo(ViewModel.Project.Tasks.Count > 0 ? ViewModel.Project.Start : ViewModel.Project.StartDate);
            UpdateGanttScrollBar();
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!ViewModel.ConfirmDiscard())
            e.Cancel = true;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ---- Task sheet ----

    private void OnSheetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SetSelection(TaskSheet.SelectedItems.OfType<TaskRowViewModel>().Select(r => r.Task).OfType<ProjectTask>());
    }

    private void SelectTask(ProjectTask task)
    {
        var row = ViewModel.Rows.FirstOrDefault(r => r.Task == task);
        if (row is null)
            return;
        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!additive)
            TaskSheet.SelectedItems.Clear();
        TaskSheet.SelectedItems.Add(row);
        TaskSheet.ScrollIntoView(row);
    }

    private void OnSheetPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool editing = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Delete && !editing && ViewModel.DeleteTaskCommand.CanExecute(null))
        {
            ViewModel.DeleteTaskCommand.Execute(null);
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

    /// <summary>Renders the window's content to a PNG (used for documentation and UI checks).</summary>
    public void SaveScreenshot(string path)
    {
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }
}
