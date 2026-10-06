using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Controls;

/// <summary>
/// Resource Graph: one resource's allocation per timescale period as a column chart, as a percentage of
/// its working time, against its maximum units. The part above the maximum is drawn in red.
/// </summary>
public sealed class ResourceGraph : TimelineElement
{
    private static readonly Brush AllocatedFill = Frozen(Color.FromRgb(0x5B, 0x8F, 0xD0));
    private static readonly Brush OverFill = Frozen(Color.FromRgb(0xE0, 0x50, 0x40));
    private static readonly Pen MaxPen = FrozenPen(Color.FromRgb(0x20, 0x20, 0x20), 1.5);

    private MainViewModel? _viewModel;
    private Resource? _resource;

    public ResourceGraph()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public double HeaderHeight { get; set; } = 44;

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
            OnRefreshed(this, EventArgs.Empty);
        }
    }

    /// <summary>The resource drawn (the view's resource picker sets it).</summary>
    public Resource? Resource
    {
        get => _resource;
        set
        {
            _resource = value;
            InvalidateVisual();
        }
    }

    protected override double ZoomPixelsPerDay => _viewModel?.PixelsPerDay ?? 24;
    protected override Project? Project => _viewModel?.Project;

    private void OnRefreshed(object? sender, EventArgs e)
    {
        UpdateTimeline();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        if (Project is not { } project)
            return;
        var from = DateAt(0);
        var to = DateAt(ActualWidth);
        Drawing.TimescaleHeader(dc, this, XOf, from, to, PixelsPerDay, ActualWidth, HeaderHeight, project.GanttFormat.Timescale);

        double plotTop = HeaderHeight + 30, plotBottom = ActualHeight - 22;
        if (plotBottom - plotTop < 40)
            return;

        var resource = _resource is not null ? project.GetResource(_resource.Id) : null;
        if (resource is null)
        {
            var hint = Drawing.Text(this, "Choose a resource above.", 13, Drawing.MutedText);
            dc.DrawText(hint, new Point(12, plotTop));
            return;
        }

        var tiers = Timescale.Tiers(PixelsPerDay, project.GanttFormat.Timescale);
        var tier = tiers[^1];
        var calendar = resource.Calendar ?? project.Calendar;
        var assignments = ResourceAnalysis.ScheduledAssignments(project, resource).ToList();

        // Percent of the period's working time, and the resource's maximum units then.
        var periods = new List<(DateTime Start, DateTime End, double Percent, double Max)>();
        foreach (var start in tier.Boundaries(from, to))
        {
            var end = tier.Next(start);
            long available = calendar.WorkingMinutesBetween(start, end);
            double work = assignments.Sum(a => resource.Type == ResourceType.Work ? Timephased.WorkBetween(a, start, end) : 0);
            double percent = available > 0 ? 100 * work / available : 0;
            periods.Add((start, end, percent, resource.MaxUnitsAt(start) * 100));
        }

        double top = Math.Max(100, Math.Max(periods.Count == 0 ? 0 : periods.Max(p => p.Percent), periods.Count == 0 ? 0 : periods.Max(p => p.Max)) * 1.15);
        top = Math.Ceiling(top / 50) * 50;
        double Y(double percent) => plotBottom - (plotBottom - plotTop) * percent / top;

        // Axis lines every 50%.
        for (double v = 0; v <= top; v += top > 300 ? 100 : 50)
        {
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(Drawing.LightGridPen, new Point(0, y), new Point(ActualWidth, y));
            var label = Drawing.Text(this, $"{v:0}%", 10, Drawing.MutedText);
            dc.DrawText(label, new Point(3, v == 0 ? y - label.Height : y + 1));
        }

        foreach (var (start, end, percent, max) in periods)
        {
            double x1 = XOf(start) + 1, x2 = XOf(end) - 1;
            if (x2 - x1 < 1 || percent <= 0)
                continue;
            double within = Math.Min(percent, max);
            dc.DrawRectangle(AllocatedFill, null, new Rect(x1, Y(within), x2 - x1, plotBottom - Y(within)));
            if (percent > max + 0.01)
                dc.DrawRectangle(OverFill, null, new Rect(x1, Y(percent), x2 - x1, Y(within) - Y(percent)));
            if (x2 - x1 > 26)
            {
                var value = Drawing.Text(this, $"{percent:0}%", 10, percent > max + 0.01 ? Drawing.AlertText : Drawing.TextBrush);
                dc.DrawText(value, new Point((x1 + x2 - value.Width) / 2, Y(percent) - value.Height));
            }
        }

        // Maximum units as a step line.
        foreach (var (start, end, _, max) in periods)
        {
            double y = Math.Round(Y(max)) + 0.5;
            dc.DrawLine(MaxPen, new Point(XOf(start), y), new Point(XOf(end), y));
        }

        var title = Drawing.Text(this, $"{resource.Name}: allocation as % of working time (line = maximum units, red = over-allocated)", 12, bold: true);
        dc.DrawText(title, new Point(8, HeaderHeight + 4));
        dc.DrawLine(Drawing.GridPen, new Point(0, plotBottom + 0.5), new Point(ActualWidth, plotBottom + 0.5));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewModel is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var anchor = DateAt(e.GetPosition(this).X);
            double anchorX = e.GetPosition(this).X;
            _viewModel.ZoomIndex += e.Delta > 0 ? 1 : -1;
            KeepDateAt(anchor, anchorX);
            e.Handled = true;
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}

/// <summary>
/// Team Planner: one row per resource with the tasks it works on drawn as bars on the timeline, plus a row
/// of tasks nobody is assigned to. Drag a bar to another resource's row to reassign it.
/// </summary>
public sealed class TeamPlanner : TimelineElement
{
    private static readonly Brush RowAlt = Frozen(Color.FromRgb(0xF7, 0xF9, 0xFC));
    private static readonly Brush BarFill = Frozen(Color.FromRgb(0x8A, 0xB4, 0xE6));
    private static readonly Brush DoneFill = Frozen(Color.FromRgb(0x9A, 0xB0, 0xC8));
    private static readonly Brush UnassignedFill = Frozen(Color.FromRgb(0xF6, 0xD3, 0x8B));
    private static readonly Brush OverFill = Frozen(Color.FromArgb(0x40, 0xE0, 0x40, 0x30));
    private static readonly Pen BarBorder = FrozenPen(Color.FromRgb(0x3E, 0x6F, 0xB0), 1);
    private static readonly Pen DragPen = FrozenPen(Color.FromRgb(0x20, 0x20, 0x20), 1.5);

    private MainViewModel? _viewModel;
    private readonly List<(Resource? Resource, List<ProjectTask> Tasks)> _rows = new();
    private readonly List<(Rect Bar, ProjectTask Task, Resource? Resource)> _hits = new();
    private (ProjectTask Task, Resource? From)? _drag;
    private Point _dragPoint;

    public TeamPlanner()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public double HeaderHeight { get; set; } = 44;
    public double NameWidth { get; set; } = 170;
    public double RowHeight { get; set; } = 30;

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
            OnRefreshed(this, EventArgs.Empty);
        }
    }

    protected override double ZoomPixelsPerDay => _viewModel?.PixelsPerDay ?? 24;
    protected override Project? Project => _viewModel?.Project;

    private void OnRefreshed(object? sender, EventArgs e)
    {
        UpdateTimeline();
        InvalidateVisual();
    }

    private void BuildRows(Project project)
    {
        _rows.Clear();
        foreach (var resource in project.Resources.Where(r => r.Type == ResourceType.Work && !r.IsBudget))
            _rows.Add((resource, project.AssignmentsOf(resource).Select(a => a.Task).Where(t => !t.IsSummary).Distinct().ToList()));
        var unassigned = project.Tasks.Where(t => !t.IsSummary && t.IsActive && !t.Assignments.Any(a => a.IsWork) && !(t.IsMilestone || t.DurationMinutes == 0)).ToList();
        _rows.Add((null, unassigned));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        _hits.Clear();
        if (Project is not { } project)
            return;
        BuildRows(project);
        var from = DateAt(NameWidth);
        var to = DateAt(ActualWidth);

        dc.PushClip(new RectangleGeometry(new Rect(NameWidth, 0, Math.Max(0, ActualWidth - NameWidth), ActualHeight)));
        Drawing.TimescaleHeader(dc, this, XOf, from, to, PixelsPerDay, ActualWidth, HeaderHeight, project.GanttFormat.Timescale);
        dc.Pop();

        var selected = _viewModel!.SelectedTasks.ToHashSet();
        for (int i = 0; i < _rows.Count; i++)
        {
            var (resource, tasks) = _rows[i];
            double top = HeaderHeight + i * RowHeight;
            if (i % 2 == 1)
                dc.DrawRectangle(RowAlt, null, new Rect(0, top, ActualWidth, RowHeight));
            dc.DrawLine(Drawing.LightGridPen, new Point(0, top + RowHeight - 0.5), new Point(ActualWidth, top + RowHeight - 0.5));

            dc.PushClip(new RectangleGeometry(new Rect(NameWidth, top, Math.Max(0, ActualWidth - NameWidth), RowHeight)));
            if (resource is not null)
            {
                foreach (var over in ResourceAnalysis.FindOverallocations(project, resource, OverallocationBasis.DayByDay))
                    dc.DrawRectangle(OverFill, null, new Rect(XOf(over.From), top, Math.Max(1, XOf(over.To) - XOf(over.From)), RowHeight));
            }
            // Overlapping tasks share the row in lanes.
            var lanes = new List<DateTime>();
            var placed = new List<(ProjectTask Task, int Lane)>();
            foreach (var task in tasks.OrderBy(t => t.Start))
            {
                int lane = lanes.FindIndex(end => end <= task.Start);
                if (lane < 0)
                {
                    lanes.Add(task.Finish);
                    lane = lanes.Count - 1;
                }
                else
                {
                    lanes[lane] = task.Finish;
                }
                placed.Add((task, lane));
            }
            double laneHeight = (RowHeight - 6) / Math.Max(1, lanes.Count);
            foreach (var (task, lane) in placed)
            {
                double x1 = XOf(task.Start), x2 = Math.Max(x1 + 3, XOf(task.Finish));
                var bar = new Rect(x1, top + 3 + lane * laneHeight, x2 - x1, Math.Max(3, laneHeight - 1));
                var fill = resource is null ? UnassignedFill : task.PercentComplete >= 100 ? DoneFill : BarFill;
                dc.DrawRectangle(fill, selected.Contains(task) ? Drawing.SelectedBorder : BarBorder, bar);
                if (bar.Width > 24 && bar.Height >= 12)
                {
                    var name = Drawing.Text(this, task.Name, 11, maxWidth: bar.Width - 4);
                    dc.DrawText(name, new Point(bar.X + 2, bar.Y + (bar.Height - name.Height) / 2));
                }
                _hits.Add((bar, task, resource));
            }
            dc.Pop();

            dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(0, top, NameWidth, RowHeight));
            var label = Drawing.Text(this, resource?.Name ?? "Unassigned tasks", 12, resource is null ? Drawing.MutedText : Drawing.TextBrush, bold: true, maxWidth: NameWidth - 12);
            dc.DrawText(label, new Point(8, top + (RowHeight - label.Height) / 2));
        }
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(0, 0, NameWidth, HeaderHeight));
        dc.DrawText(Drawing.Text(this, "Resource Name", 12, bold: true), new Point(8, HeaderHeight - 22));
        dc.DrawLine(Drawing.GridPen, new Point(NameWidth - 0.5, 0), new Point(NameWidth - 0.5, ActualHeight));

        if (_drag is not null)
        {
            int row = RowAt(_dragPoint.Y);
            if (row >= 0)
                dc.DrawRectangle(null, DragPen, new Rect(NameWidth, HeaderHeight + row * RowHeight + 1, ActualWidth - NameWidth, RowHeight - 2));
        }
    }

    private int RowAt(double y)
    {
        int row = (int)((y - HeaderHeight) / RowHeight);
        return y >= HeaderHeight && row < _rows.Count ? row : -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        var hit = _hits.LastOrDefault(h => h.Bar.Contains(p));
        if (hit.Task is null)
            return;
        TaskClicked?.Invoke(this, hit.Task);
        if (e.ClickCount == 2)
        {
            TaskDoubleClicked?.Invoke(this, hit.Task);
            return;
        }
        _drag = (hit.Task, hit.Resource);
        _dragPoint = p;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is null)
            return;
        _dragPoint = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is not { } drag)
            return;
        ReleaseMouseCapture();
        _drag = null;
        int row = RowAt(e.GetPosition(this).Y);
        InvalidateVisual();
        if (row < 0 || _viewModel is null || _rows[row].Resource == drag.From)
            return;
        var target = _rows[row].Resource;
        int taskId = drag.Task.Id;
        int? fromId = drag.From?.Id, toId = target?.Id;
        _viewModel.TryEdit(target is null ? "Unassign task" : $"Reassign to {target.Name}", p =>
        {
            var task = p.GetRequiredTask(taskId);
            var old = fromId is { } f ? task.Assignments.FirstOrDefault(a => a.Resource.Id == f) : null;
            double units = old?.Units ?? 1.0;
            if (old is not null)
                p.Unassign(task, old.Resource);
            if (toId is { } t && p.GetResource(t) is { } resource && task.Assignments.All(a => a.Resource != resource))
                p.Assign(task, resource, units);
        });
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewModel is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var anchor = DateAt(e.GetPosition(this).X);
            double anchorX = e.GetPosition(this).X;
            _viewModel.ZoomIndex += e.Delta > 0 ? 1 : -1;
            KeepDateAt(anchor, anchorX);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int rows = Project is { } p ? p.Resources.Count(r => r.Type == ResourceType.Work && !r.IsBudget) + 1 : 1;
        return new Size(0, HeaderHeight + rows * RowHeight + 4);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
