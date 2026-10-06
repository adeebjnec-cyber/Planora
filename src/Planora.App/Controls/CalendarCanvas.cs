using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// The Calendar view: a month of weeks with task bars stacked in each week, like Microsoft Project's
/// Calendar. The header has previous/next/today buttons.
/// </summary>
public sealed class CalendarCanvas : FrameworkElement, ITaskCanvas
{
    private const double HeaderHeight = 40;
    private const double WeekdayHeight = 22;
    private const double DayNumberHeight = 18;
    private const double BarHeight = 18;
    private const double BarGap = 2;

    private MainViewModel? _viewModel;
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private readonly List<(Rect Bounds, ProjectTask Task)> _hitAreas = new();
    private readonly List<(Rect Bounds, DateTime Day)> _dayCells = new();
    private ProjectTask? _dragTask;
    private DateTime _dragFromDay;
    private Point _dragStart;
    private Point _dragCurrent;
    private bool _dragging;
    private Rect _previousButton, _nextButton, _todayButton;
    private bool _monthChosen;

    public CalendarCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler<ProjectTask>? TaskClicked;
    public event EventHandler<ProjectTask>? TaskDoubleClicked;

    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
                _viewModel.Refreshed -= OnRefreshed;
            _viewModel = value;
            if (_viewModel is not null)
                _viewModel.Refreshed += OnRefreshed;
            InvalidateVisual();
        }
    }

    private void OnRefreshed(object? sender, EventArgs e) => InvalidateVisual();

    private DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    protected override void OnRender(DrawingContext dc)
    {
        _hitAreas.Clear();
        _dayCells.Clear();
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        if (_viewModel is null)
            return;
        var project = _viewModel.Project;

        // Open on the month the project starts in, until the user navigates.
        if (!_monthChosen && project.Tasks.Count > 0)
            _month = new DateTime(project.Start.Year, project.Start.Month, 1);

        DrawHeader(dc);

        double width = ActualWidth, columnWidth = width / 7;
        var gridStart = _month.AddDays(-(((int)_month.DayOfWeek - (int)FirstDay + 7) % 7));
        int weeks = (int)Math.Ceiling(((_month.AddMonths(1) - gridStart).TotalDays) / 7);
        double weekHeight = Math.Max(60, (ActualHeight - HeaderHeight - WeekdayHeight) / weeks);

        for (int d = 0; d < 7; d++)
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)FirstDay + d) % 7));
            var text = Drawing.Text(this, name, 11, Drawing.MutedText, bold: true);
            dc.DrawText(text, new Point(d * columnWidth + (columnWidth - text.Width) / 2, HeaderHeight + 3));
        }

        var tasks = project.Tasks.Where(t => !t.IsSummary && t.IsActive).OrderBy(t => t.Start).ThenBy(project.IndexOf).ToList();
        var selected = _viewModel.SelectedTasks.ToHashSet();
        var calendar = project.Calendar;

        for (int w = 0; w < weeks; w++)
        {
            var weekStart = gridStart.AddDays(7 * w);
            var weekEnd = weekStart.AddDays(7);
            double top = HeaderHeight + WeekdayHeight + w * weekHeight;

            for (int d = 0; d < 7; d++)
            {
                var day = weekStart.AddDays(d);
                var cell = new Rect(d * columnWidth, top, columnWidth, weekHeight);
                _dayCells.Add((cell, day));
                if (day.Month != _month.Month)
                    dc.DrawRectangle(Drawing.HeaderBackground, null, cell);
                else if (!calendar.IsWorkingDay(DateOnly.FromDateTime(day)))
                    dc.DrawRectangle(Drawing.NonWorking, null, cell);
                if (day == DateTime.Today)
                    dc.DrawRectangle(null, Drawing.TodayPen, new Rect(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2));
                dc.DrawRectangle(null, Drawing.GridPen, cell);
                var number = Drawing.Text(this, day.Day == 1 ? day.ToString("d MMM", CultureInfo.CurrentCulture) : day.Day.ToString(CultureInfo.CurrentCulture),
                    11, day.Month == _month.Month ? Drawing.TextBrush : Drawing.MutedText);
                dc.DrawText(number, new Point(cell.Right - number.Width - 4, cell.Top + 2));
            }

            // Stack this week's tasks into lanes.
            var lanes = new List<DateTime>();
            int maxLanes = (int)((weekHeight - DayNumberHeight - 4) / (BarHeight + BarGap));
            var hidden = new int[7];
            foreach (var task in tasks.Where(t => t.Start < weekEnd && t.Finish >= weekStart))
            {
                int firstColumn = Math.Max(0, (int)(task.Start.Date - weekStart).TotalDays);
                var lastDay = task.Finish.TimeOfDay == TimeSpan.Zero && task.Finish > task.Start ? task.Finish.AddDays(-1) : task.Finish;
                int lastColumn = Math.Min(6, (int)(lastDay.Date - weekStart).TotalDays);
                if (lastColumn < firstColumn)
                    continue;

                int lane = lanes.FindIndex(free => free <= weekStart.AddDays(firstColumn));
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add(default);
                }
                lanes[lane] = weekStart.AddDays(lastColumn + 1);
                if (lane >= maxLanes)
                {
                    for (int c = firstColumn; c <= lastColumn; c++)
                        hidden[c]++;
                    continue;
                }

                var bar = new Rect(firstColumn * columnWidth + 3, top + DayNumberHeight + 2 + lane * (BarHeight + BarGap),
                    (lastColumn - firstColumn + 1) * columnWidth - 6, BarHeight);
                bool critical = _viewModel.ShowCriticalPath && task.IsCritical;
                bool milestone = task.IsMilestone || task.DurationMinutes == 0;
                var fill = task.PercentComplete >= 100 ? Drawing.DoneFill : critical ? Drawing.CriticalFill : Drawing.TaskFill;
                var pen = selected.Contains(task) ? Drawing.SelectedBorder : critical ? Drawing.CriticalBorder : Drawing.TaskBorder;
                if (milestone)
                {
                    Drawing.Diamond(dc, new Point(bar.Left + 7, bar.Top + BarHeight / 2), 6, critical ? Drawing.CriticalBorder.Brush : Drawing.SummaryFill);
                    dc.DrawText(Drawing.Text(this, task.Name, 11, maxWidth: Math.Max(1, bar.Width - 18)), new Point(bar.Left + 16, bar.Top + 1));
                }
                else
                {
                    dc.DrawRoundedRectangle(fill, pen, bar, 3, 3);
                    dc.DrawText(Drawing.Text(this, task.Name, 11, maxWidth: Math.Max(1, bar.Width - 8)), new Point(bar.Left + 4, bar.Top + 1));
                }
                _hitAreas.Add((bar, task));
            }

            for (int c = 0; c < 7; c++)
            {
                if (hidden[c] > 0)
                {
                    var more = Drawing.Text(this, $"+{hidden[c]} more", 10, Drawing.MutedText);
                    dc.DrawText(more, new Point(c * columnWidth + 4, top + weekHeight - more.Height - 2));
                }
            }
        }

        if (_dragging && _dragTask is not null && DayAt(_dragCurrent) is { } target)
        {
            var cell = _dayCells.First(c => c.Day == target).Bounds;
            dc.DrawRectangle(null, Drawing.SelectedBorder, new Rect(cell.X + 2, cell.Y + 2, cell.Width - 4, cell.Height - 4));
            var label = Drawing.Text(this, $"Move {_dragTask.Name} to {target.ToString("ddd d MMM", CultureInfo.CurrentCulture)}", 11);
            var box = new Rect(_dragCurrent.X + 10, _dragCurrent.Y - 22, label.Width + 10, label.Height + 4);
            dc.DrawRectangle(Brushes.LightYellow, Drawing.GridPen, box);
            dc.DrawText(label, new Point(box.X + 5, box.Y + 2));
        }
    }

    private DateTime? DayAt(Point p) => _dayCells.Where(c => c.Bounds.Contains(p)).Select(c => (DateTime?)c.Day).FirstOrDefault();

    private void DrawHeader(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(0, 0, ActualWidth, HeaderHeight));
        var title = Drawing.Text(this, _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture), 16, bold: true);
        dc.DrawText(title, new Point(110, (HeaderHeight - title.Height) / 2));

        _previousButton = new Rect(8, 8, 28, 24);
        _nextButton = new Rect(40, 8, 28, 24);
        _todayButton = new Rect(ActualWidth - 70, 8, 60, 24);
        DrawButton(dc, _previousButton, "◀");
        DrawButton(dc, _nextButton, "▶");
        DrawButton(dc, _todayButton, "Today");
    }

    private void DrawButton(DrawingContext dc, Rect rect, string label)
    {
        dc.DrawRoundedRectangle(Drawing.Background, Drawing.GridPen, rect, 3, 3);
        var text = Drawing.Text(this, label, 11);
        dc.DrawText(text, new Point(rect.Left + (rect.Width - text.Width) / 2, rect.Top + (rect.Height - text.Height) / 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        if (_previousButton.Contains(p) || _nextButton.Contains(p) || _todayButton.Contains(p))
        {
            _monthChosen = true;
            _month = _todayButton.Contains(p) ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                : _month.AddMonths(_previousButton.Contains(p) ? -1 : 1);
            InvalidateVisual();
            return;
        }
        var hit = _hitAreas.LastOrDefault(h => h.Bounds.Contains(p));
        if (hit.Task is null)
            return;
        TaskClicked?.Invoke(this, hit.Task);
        if (e.ClickCount == 2)
        {
            TaskDoubleClicked?.Invoke(this, hit.Task);
            return;
        }
        if (DayAt(p) is { } day && !hit.Task.IsSummary)
        {
            _dragTask = hit.Task;
            _dragFromDay = day;
            _dragStart = _dragCurrent = p;
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTask is null)
            return;
        _dragCurrent = e.GetPosition(this);
        if (!_dragging && (_dragCurrent - _dragStart).Length > 6)
            _dragging = true;
        if (_dragging)
            InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (task, dragging, from) = (_dragTask, _dragging, _dragFromDay);
        _dragTask = null;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();
        if (task is null || !dragging || _viewModel is null || DayAt(e.GetPosition(this)) is not { } to || to == from)
            return;
        // Move by whole days, keeping the time of day; the scheduler snaps to working time.
        _viewModel.MoveTask(task, task.Start.AddDays((to - from).TotalDays));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _monthChosen = true;
        _month = _month.AddMonths(e.Delta > 0 ? -1 : 1);
        InvalidateVisual();
        e.Handled = true;
    }
}
