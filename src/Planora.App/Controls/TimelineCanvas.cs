using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// The Timeline: a one-glance summary of the project from start to finish, showing the tasks marked
/// "show on timeline" as bars and milestones as flags above the bar. With nothing marked, top-level
/// tasks and milestones are shown.
/// </summary>
public sealed class TimelineCanvas : FrameworkElement, ITaskCanvas
{
    private const double SideMargin = 90;
    private const double LaneHeight = 36;
    private const double MilestoneRowHeight = 34;

    private MainViewModel? _viewModel;
    private readonly List<(Rect Bounds, ProjectTask Task)> _hitAreas = new();

    public TimelineCanvas()
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

    protected override void OnRender(DrawingContext dc)
    {
        _hitAreas.Clear();
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        if (_viewModel is null)
            return;
        var project = _viewModel.Project;
        if (project.Tasks.Count == 0)
        {
            dc.DrawText(Drawing.Text(this, "Add tasks to see the timeline.", 14, Drawing.MutedText), new Point(20, 20));
            return;
        }

        var marked = project.Tasks.Where(t => t.ShowOnTimeline && t.IsActive).ToList();
        bool automatic = marked.Count == 0;
        var shown = automatic
            ? project.Tasks.Where(t => t.IsActive && (t.OutlineLevel == 1 || IsMilestone(t))).ToList()
            : marked;
        var bars = shown.Where(t => !IsMilestone(t)).OrderBy(t => t.Start).ToList();
        var milestones = shown.Where(IsMilestone).OrderBy(t => t.Start).ToList();

        var start = project.Start;
        var finish = project.Finish > start ? project.Finish : start.AddDays(1);
        double left = SideMargin, right = Math.Max(left + 100, ActualWidth - SideMargin);
        double X(DateTime d) => left + (d - start).TotalDays / (finish - start).TotalDays * (right - left);

        // Milestone flags above the bar, staggered into rows so labels do not overlap.
        var milestoneRows = new List<double>();
        var milestoneSlots = new List<(ProjectTask Task, int Row)>();
        foreach (var m in milestones)
        {
            var label = Drawing.Text(this, $"{m.Name}  {m.Start.ToString("d MMM", CultureInfo.CurrentCulture)}", 11);
            double x = X(m.Start);
            bool flip = x + 10 + label.Width > ActualWidth - 4;
            double from = flip ? x - 10 - label.Width : x - 7;
            int row = milestoneRows.FindIndex(end => end < from);
            if (row < 0)
            {
                row = milestoneRows.Count;
                milestoneRows.Add(0);
            }
            milestoneRows[row] = flip ? x + 8 : x + label.Width + 16;
            milestoneSlots.Add((m, row));
        }

        double top = 16 + milestoneRows.Count * MilestoneRowHeight;
        var lanes = new List<DateTime>();
        var barLanes = new List<(ProjectTask Task, int Lane)>();
        foreach (var t in bars)
        {
            int lane = lanes.FindIndex(free => free <= t.Start);
            if (lane < 0)
            {
                lane = lanes.Count;
                lanes.Add(default);
            }
            lanes[lane] = t.Finish;
            barLanes.Add((t, lane));
        }
        double bandHeight = Math.Max(1, lanes.Count) * LaneHeight + 12;
        var band = new Rect(left, top, right - left, bandHeight);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB)), Drawing.TaskBorder, band, 4, 4);

        // Start and finish labels at the ends.
        var startLabel = Drawing.Text(this, "Start\n" + DateText.Format(start), 11, Drawing.MutedText);
        dc.DrawText(startLabel, new Point(left - startLabel.Width - 10, top + (bandHeight - startLabel.Height) / 2));
        var finishLabel = Drawing.Text(this, "Finish\n" + DateText.Format(finish), 11, Drawing.MutedText);
        dc.DrawText(finishLabel, new Point(right + 10, top + (bandHeight - finishLabel.Height) / 2));

        var selected = _viewModel.SelectedTasks.ToHashSet();
        foreach (var (task, lane) in barLanes)
        {
            double x1 = X(task.Start), x2 = Math.Max(x1 + 3, X(task.Finish));
            var rect = new Rect(x1, top + 6 + lane * LaneHeight, x2 - x1, LaneHeight - 6);
            bool critical = _viewModel.ShowCriticalPath && task.IsCritical;
            var fill = task.IsSummary ? new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xD6))
                : task.PercentComplete >= 100 ? Drawing.DoneFill
                : critical ? Drawing.CriticalFill : Drawing.TaskFill;
            dc.DrawRectangle(fill, selected.Contains(task) ? Drawing.SelectedBorder : Drawing.TaskBorder, rect);
            var name = Drawing.Text(this, task.Name, 11, task.IsSummary ? Brushes.White : Drawing.TextBrush, bold: true, maxWidth: Math.Max(1, rect.Width - 8));
            dc.DrawText(name, new Point(rect.Left + 4, rect.Top + 1));
            if (rect.Width > 90)
            {
                var dates = Drawing.Text(this, $"{task.Start:d MMM} – {task.Finish:d MMM}", 9.5, task.IsSummary ? Brushes.White : Drawing.MutedText, maxWidth: rect.Width - 8);
                dc.DrawText(dates, new Point(rect.Left + 4, rect.Top + 13));
            }
            _hitAreas.Add((rect, task));
        }

        foreach (var (m, row) in milestoneSlots)
        {
            double x = X(m.Start);
            double y = 12 + row * MilestoneRowHeight;
            dc.DrawLine(Drawing.GridPen, new Point(x, y + 8), new Point(x, top));
            Drawing.Diamond(dc, new Point(x, y + 6), 6, _viewModel.ShowCriticalPath && m.IsCritical ? Drawing.CriticalBorder.Brush : Drawing.SummaryFill);
            var label = Drawing.Text(this, $"{m.Name}  {m.Start.ToString("d MMM", CultureInfo.CurrentCulture)}", 11, bold: selected.Contains(m));
            // Near the right edge, put the label to the left of the flag so it stays visible.
            bool flip = x + 10 + label.Width > ActualWidth - 4;
            double labelX = flip ? x - 10 - label.Width : x + 10;
            dc.DrawText(label, new Point(labelX, y - 1));
            _hitAreas.Add((new Rect(Math.Min(labelX, x - 7), y - 2, label.Width + 18, 18), m));
        }

        // Date scale and today marker below the bar.
        double scaleY = top + bandHeight + 4;
        var (topTier, _) = Timescale.For((right - left) / Math.Max(1, (finish - start).TotalDays) * 2);
        foreach (var boundary in topTier.Boundaries(start, finish))
        {
            double x = X(boundary);
            if (x < left || x > right)
                continue;
            dc.DrawLine(Drawing.GridPen, new Point(x, scaleY), new Point(x, scaleY + 6));
            dc.DrawText(Drawing.Text(this, topTier.Label(boundary), 10, Drawing.MutedText), new Point(x + 2, scaleY + 4));
        }
        if (DateTime.Now >= start && DateTime.Now <= finish)
        {
            double x = X(DateTime.Now);
            dc.DrawLine(Drawing.TodayPen, new Point(x, top - 4), new Point(x, top + bandHeight + 4));
            var today = Drawing.Text(this, "Today", 10, Drawing.TodayPen.Brush, bold: true);
            dc.DrawText(today, new Point(x - today.Width / 2, top + bandHeight + 22));
        }

        if (automatic)
        {
            dc.DrawText(Drawing.Text(this, "Showing top-level tasks and milestones. Select tasks and use Task > Add to Timeline to choose what appears here.",
                11, Drawing.MutedText), new Point(left, scaleY + 44));
        }
    }

    private static bool IsMilestone(ProjectTask t) => !t.IsSummary && (t.IsMilestone || t.DurationMinutes == 0);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var hit = _hitAreas.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this)));
        if (hit.Task is null)
            return;
        TaskClicked?.Invoke(this, hit.Task);
        if (e.ClickCount == 2)
            TaskDoubleClicked?.Invoke(this, hit.Task);
    }
}
