using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.Controls;

/// <summary>
/// The Gantt chart: a timescale header plus one bar row per task-sheet row, drawn directly for speed.
/// Rows line up with the task sheet (same row and header heights); the window keeps the vertical
/// scroll positions in step. Bars can be dragged to move, resized at the right edge, dragged at the
/// left edge to set progress, or dragged up/down onto another task to link them.
/// </summary>
public sealed class GanttChart : TimelineElement
{
    private enum DragMode { None, Move, Resize, Progress, Link }

    private const double EdgeGrip = 5;

    // Microsoft Project-like palette.
    private static readonly Brush Background = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush HeaderBackground = Frozen(Color.FromRgb(0xF3, 0xF3, 0xF3));
    private static readonly Brush NonWorking = Frozen(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush SelectedRow = Frozen(Color.FromRgb(0xDC, 0xE9, 0xF7));
    private static readonly Brush TaskFill = Frozen(Color.FromRgb(0x8A, 0xB4, 0xE6));
    private static readonly Brush CriticalFill = Frozen(Color.FromRgb(0xF0, 0x8E, 0x82));
    private static readonly Brush ManualFill = Frozen(Color.FromRgb(0xB9, 0xE3, 0xEA));
    private static readonly Brush ProgressFill = Frozen(Color.FromRgb(0x1F, 0x3A, 0x60));
    private static readonly Brush SummaryFill = Frozen(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush MilestoneFill = Frozen(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush BaselineFill = Frozen(Color.FromRgb(0xA6, 0xA6, 0xA6));
    private static readonly Brush DeadlineFill = Frozen(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush MissedFill = Frozen(Color.FromRgb(0xD0, 0x30, 0x30));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0x30, 0x30, 0x30));
    private static readonly Brush GhostFill = Frozen(Color.FromArgb(0x60, 0x40, 0x70, 0xC0));
    private static readonly Pen TaskBorder = FrozenPen(Color.FromRgb(0x3E, 0x6F, 0xB0), 1);
    private static readonly Pen CriticalBorder = FrozenPen(Color.FromRgb(0xC0, 0x40, 0x30), 1);
    private static readonly Pen ManualBorder = FrozenPen(Color.FromRgb(0x3A, 0x9A, 0xAE), 1);
    private static readonly Pen InactiveBorder = FrozenPen(Color.FromRgb(0x9A, 0x9A, 0x9A), 1, dashed: true);
    private static readonly Pen LinkPen = FrozenPen(Color.FromRgb(0x2F, 0x5F, 0xA8), 1);
    private static readonly Pen CriticalLinkPen = FrozenPen(Color.FromRgb(0xC0, 0x40, 0x30), 1);
    private static readonly Pen SplitPen = FrozenPen(Color.FromRgb(0x3E, 0x6F, 0xB0), 1, dashed: true);
    private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0xD8, 0xD8, 0xD8), 1);
    private static readonly Pen TodayPen = FrozenPen(Color.FromRgb(0x2E, 0x9E, 0x4F), 1.5);
    private static readonly Pen StatusPen = FrozenPen(Color.FromRgb(0x80, 0x40, 0xC0), 1.5, dashed: true);
    private static readonly Pen ProgressLinePen = FrozenPen(Color.FromRgb(0xD0, 0x30, 0x30), 1.5);
    private static readonly Pen ProjectLinePen = FrozenPen(Color.FromRgb(0x40, 0x40, 0x90), 1, dashed: true);
    private static readonly Pen DragLinkPen = FrozenPen(Color.FromRgb(0x20, 0x20, 0x20), 1.5, dashed: true);
    private static readonly Typeface Font = new("Segoe UI");

    private MainViewModel? _viewModel;
    private double _verticalOffset;

    private DragMode _drag;
    private ProjectTask? _dragTask;
    private Point _dragOrigin;
    private Point _dragCurrent;
    private ProjectTask? _hoverTask;

    public GanttChart()
    {
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 600);
    }

    /// <summary>Highlight selected rows (off when printing).</summary>
    public bool ShowSelection { get; set; } = true;

    public double RowHeight { get; set; } = 24;
    public double HeaderHeight { get; set; } = 44;

    /// <summary>Raised when a bar is clicked, so the task sheet can select the task.</summary>
    public event EventHandler<ProjectTask>? TaskClicked;

    /// <summary>Raised for mouse-wheel scrolling over the chart (vertical scrolling is owned by the task sheet).</summary>
    public event EventHandler<int>? WheelScrolled;

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

    /// <summary>Stops repainting on project changes while keeping the current picture (used for printed pages).</summary>
    public void DetachFromViewModel()
    {
        if (_viewModel is not null)
            _viewModel.Refreshed -= OnRefreshed;
    }

    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            _verticalOffset = value;
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

    // ------------------------------------------------------------------ drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (_viewModel is null || Project is null)
            return;

        var rows = _viewModel.Rows;
        var rowIndex = new Dictionary<ProjectTask, int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Task is { } t)
                rowIndex[t] = i;
        }

        var visibleFrom = DateAt(0);
        var visibleTo = DateAt(ActualWidth);
        int firstRow = Math.Max(0, (int)(_verticalOffset / RowHeight));
        int lastRow = Math.Min(rows.Count - 1, (int)((_verticalOffset + ActualHeight - HeaderHeight) / RowHeight) + 1);

        dc.PushClip(new RectangleGeometry(new Rect(0, HeaderHeight, ActualWidth, Math.Max(0, ActualHeight - HeaderHeight))));
        var gridlines = Project.GanttFormat.Gridlines;
        if (Project.GanttFormat.Timescale.ShadeNonWorkingTime)
            DrawNonWorkingTime(dc, visibleFrom, visibleTo);
        DrawGridlines(dc, gridlines, visibleFrom, visibleTo, firstRow, lastRow);

        var selected = ShowSelection ? _viewModel.SelectedTasks.ToHashSet() : new HashSet<ProjectTask>();
        for (int i = firstRow; i <= lastRow; i++)
        {
            if (rows[i].Task is { } t && selected.Contains(t))
                dc.DrawRectangle(SelectedRow, null, new Rect(0, RowTop(i), ActualWidth, RowHeight));
        }

        if (gridlines.CurrentDate)
            DrawVerticalLine(dc, DateTime.Now, TodayPen);
        if (gridlines.StatusDate && Project.StatusDate is { } status)
            DrawVerticalLine(dc, status, StatusPen);
        if (gridlines.ProjectStart && Project.Tasks.Count > 0)
            DrawVerticalLine(dc, Project.Start, ProjectLinePen);
        if (gridlines.ProjectFinish && Project.Tasks.Count > 0)
            DrawVerticalLine(dc, Project.Finish, ProjectLinePen);

        for (int i = firstRow; i <= lastRow; i++)
        {
            if (rows[i].Task is { } task)
                DrawTask(dc, task, i);
            else if (rows[i].Group is { } group && group.AllTasks.Any())
                DrawGroupBar(dc, group, i);
        }

        DrawLinks(dc, rowIndex, firstRow, lastRow);
        if (_viewModel.ShowProgressLines)
            DrawProgressLines(dc, rows, firstRow, lastRow);
        DrawDragFeedback(dc, rowIndex);
        dc.Pop();

        DrawHeader(dc, visibleFrom, visibleTo);
    }

    private double RowTop(int row) => HeaderHeight + row * RowHeight - _verticalOffset;

    private void DrawHeader(DrawingContext dc, DateTime from, DateTime to) =>
        Drawing.TimescaleHeader(dc, this, XOf, from, to, PixelsPerDay, ActualWidth, HeaderHeight, Project!.GanttFormat.Timescale);

    /// <summary>Row lines and timescale column lines, as set in the Gridlines dialog.</summary>
    private void DrawGridlines(DrawingContext dc, GridlineSettings gridlines, DateTime from, DateTime to, int firstRow, int lastRow)
    {
        var pen = PenOf(gridlines.Color, GridPen);
        if (gridlines.RowLines)
        {
            for (int i = firstRow; i <= lastRow; i++)
            {
                double y = Math.Round(RowTop(i) + RowHeight) - 0.5;
                dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
            }
        }
        if (!gridlines.TopTierColumns && !gridlines.BottomTierColumns)
            return;
        var tiers = Timescale.Tiers(PixelsPerDay, Project!.GanttFormat.Timescale);
        var chosen = new List<TimescaleTier>();
        if (gridlines.TopTierColumns)
            chosen.Add(tiers[0]);
        if (gridlines.BottomTierColumns)
            chosen.Add(tiers[^1]);
        foreach (var tier in chosen)
        {
            foreach (var boundary in tier.Boundaries(from, to))
            {
                double x = Math.Round(XOf(boundary)) + 0.5;
                dc.DrawLine(pen, new Point(x, HeaderHeight), new Point(x, ActualHeight));
            }
        }
    }

    private void DrawNonWorkingTime(DrawingContext dc, DateTime from, DateTime to)
    {
        if (PixelsPerDay < 3)
            return;
        var calendar = Project!.Calendar;
        for (var day = from.Date; day <= to; day = day.AddDays(1))
        {
            if (!calendar.IsWorkingDay(DateOnly.FromDateTime(day)))
            {
                double x = XOf(day);
                dc.DrawRectangle(NonWorking, null, new Rect(x, HeaderHeight, PixelsPerDay, Math.Max(0, ActualHeight - HeaderHeight)));
            }
        }
    }

    private void DrawVerticalLine(DrawingContext dc, DateTime date, Pen pen)
    {
        double x = Math.Round(XOf(date)) + 0.5;
        if (x >= 0 && x <= ActualWidth)
            dc.DrawLine(pen, new Point(x, HeaderHeight), new Point(x, ActualHeight));
    }

    private bool IsMilestone(ProjectTask t) => !t.IsSummary && (t.IsMilestone || t.DurationMinutes == 0);

    private bool ShowCritical(ProjectTask t) => _viewModel!.ShowCriticalPath && t.IsCritical;

    private GanttFormat Format => Project!.GanttFormat;

    private readonly Dictionary<string, Brush> _brushes = new();
    private readonly Dictionary<string, Pen> _pens = new();

    /// <summary>A frozen brush for a #RRGGBB colour (cached).</summary>
    private Brush BrushOf(string? hex, Brush fallback)
    {
        if (string.IsNullOrEmpty(hex))
            return fallback;
        if (_brushes.TryGetValue(hex, out var brush))
            return brush;
        try
        {
            brush = Frozen((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            brush = fallback;
        }
        return _brushes[hex] = brush;
    }

    private Pen PenOf(string? hex, Pen fallback, bool dashed = false)
    {
        if (string.IsNullOrEmpty(hex))
            return fallback;
        string key = dashed ? hex + "-" : hex;
        if (_pens.TryGetValue(key, out var pen))
            return pen;
        pen = new Pen(BrushOf(hex, fallback.Brush), 1);
        if (dashed)
            pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return _pens[key] = pen;
    }

    private BarKind KindOf(ProjectTask task) => task switch
    {
        { IsSummary: true } => BarKind.Summary,
        _ when IsMilestone(task) => BarKind.Milestone,
        { IsActive: false } => BarKind.InactiveTask,
        { Mode: TaskMode.ManuallyScheduled } => BarKind.ManualTask,
        _ when ShowCritical(task) => BarKind.CriticalTask,
        _ => BarKind.Task,
    };

    private void DrawTask(DrawingContext dc, ProjectTask task, int row)
    {
        if (task.HideBar)
            return;
        var project = Project!;
        var format = Format;
        double top = RowTop(row);
        double center = top + RowHeight / 2;

        if (_viewModel!.ShowBaseline && task.Baselines[_viewModel.GanttBaselineIndex] is { } baseline)
        {
            var style = format.StyleFor(project, task, BarKind.Baseline);
            if (style.Shape != BarShape.Hidden)
            {
                double bx = XOf(baseline.Start);
                double height = style.Shape == BarShape.Bar ? RowHeight * 0.3 : 4;
                dc.DrawRectangle(BrushOf(style.Fill, BaselineFill), null, new Rect(bx, top + RowHeight * 0.72, Math.Max(2, XOf(baseline.Finish) - bx), Math.Min(height, RowHeight * 0.28)));
            }
        }

        var kind = KindOf(task);
        var barStyle = format.StyleFor(project, task, kind);
        var fill = BrushOf(barStyle.Fill, TaskFill);
        double barStart, barEnd;
        if (kind == BarKind.Summary)
        {
            double x1 = XOf(task.Start), x2 = XOf(task.Finish);
            double barTop = barStyle.Shape == BarShape.Bar ? top + RowHeight * 0.25 : top + RowHeight * 0.3;
            double barHeight = barStyle.Shape == BarShape.Bar ? RowHeight * 0.4 : 5;
            if (barStyle.Shape != BarShape.Hidden)
            {
                dc.DrawRectangle(fill, null, new Rect(x1, barTop, Math.Max(1, x2 - x1), barHeight));
                DrawSummaryCap(dc, x1, barTop + barHeight, fill);
                DrawSummaryCap(dc, x2, barTop + barHeight, fill);
                if (task.PercentComplete > 0)
                {
                    var progress = format.StyleFor(project, task, BarKind.Progress);
                    double px = XOf(task.CompleteThrough);
                    if (progress.Shape != BarShape.Hidden)
                        dc.DrawRectangle(BrushOf(progress.Fill, ProgressFill), null, new Rect(x1, barTop + barHeight / 2 - 1, Math.Max(0, px - x1), 2));
                }
            }
            (barStart, barEnd) = (x1 - 5, x2 + 5);
        }
        else if (kind == BarKind.Milestone)
        {
            double x = XOf(task.Start);
            double size = RowHeight * 0.3;
            if (barStyle.Shape != BarShape.Hidden)
            {
                var diamondFill = ShowCritical(task) && barStyle.FlagField is null ? BrushOf(format.StyleFor(project, task, BarKind.CriticalTask).Border, CriticalBorder.Brush) : fill;
                Drawing.Diamond(dc, new Point(x, center), size, diamondFill);
            }
            (barStart, barEnd) = (x - size, x + size);
        }
        else
        {
            var border = PenOf(barStyle.Border, TaskBorder, dashed: kind == BarKind.InactiveTask);
            Brush? body = kind == BarKind.InactiveTask && barStyle.Fill.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase) ? null : fill;
            double barTop = barStyle.Shape == BarShape.Thin ? center - 3 : top + RowHeight * 0.25;
            double barHeight = barStyle.Shape == BarShape.Thin ? 6 : RowHeight * 0.5;
            double previousEnd = double.NaN;
            double first = double.NaN;
            foreach (var segment in task.Segments)
            {
                double x1 = XOf(segment.Start), x2 = XOf(segment.Finish);
                if (double.IsNaN(first))
                    first = x1;
                if (barStyle.Shape != BarShape.Hidden)
                {
                    if (!double.IsNaN(previousEnd))
                        dc.DrawLine(PenOf(barStyle.Border, SplitPen, dashed: true), new Point(previousEnd, center), new Point(x1, center));
                    dc.DrawRectangle(body, border, new Rect(x1, barTop, Math.Max(2, x2 - x1), barHeight));
                }
                previousEnd = x2;
            }

            if (task.PercentComplete > 0)
            {
                var progress = format.StyleFor(project, task, BarKind.Progress);
                if (progress.Shape != BarShape.Hidden)
                {
                    double x1 = XOf(task.Start);
                    double px = XOf(task.CompleteThrough);
                    double h = progress.Shape == BarShape.Bar ? barHeight : barHeight / 3;
                    dc.DrawRectangle(BrushOf(progress.Fill, ProgressFill), null, new Rect(x1, center - h / 2, Math.Max(0, px - x1), h));
                }
            }
            (barStart, barEnd) = (first, previousEnd);
        }

        if (task.Deadline is { } deadline)
            DrawDeadline(dc, XOf(deadline), top, task.MissesDeadline);

        if (!double.IsNaN(barEnd))
            DrawBarTexts(dc, task, barStyle, barStart, barEnd, top, center);
    }

    /// <summary>The bar style's left, right, top and inside texts, in the Bar Text style.</summary>
    private void DrawBarTexts(DrawingContext dc, ProjectTask task, BarStyle style, double barStart, double barEnd, double top, double center)
    {
        var textStyle = Format.Text(TextItem.BarText);
        double size = (textStyle.Size ?? 8.5) * 96 / 72;
        var brush = BrushOf(textStyle.Color, TextBrush);
        var typeface = new Typeface(new FontFamily("Segoe UI"), textStyle.Italic ? FontStyles.Italic : FontStyles.Normal,
            textStyle.Bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);

        FormattedText? Make(string? key)
        {
            if (key is null || BarFieldText(task, key) is not { Length: > 0 } text)
                return null;
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        if (Make(style.RightText) is { } right)
            dc.DrawText(right, new Point(barEnd + 5, center - right.Height / 2));
        if (Make(style.LeftText) is { } left)
            dc.DrawText(left, new Point(barStart - 5 - left.Width, center - left.Height / 2));
        if (Make(style.TopText) is { } above)
        {
            above.MaxLineCount = 1;
            dc.DrawText(above, new Point((barStart + barEnd - above.Width) / 2, top - 1));
        }
        if (Make(style.InsideText) is { } inside && barEnd - barStart > 8)
        {
            inside.MaxTextWidth = Math.Max(1, barEnd - barStart - 4);
            inside.MaxLineCount = 1;
            inside.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(inside, new Point(barStart + 2, center - inside.Height / 2));
        }
    }

    /// <summary>A field's text for bar labels; dates are short ("8 Oct").</summary>
    private string? BarFieldText(ProjectTask task, string key)
    {
        if (key == "ResourceNames")
            return ResourceNamesText.Format(task);
        if (Engine.Fields.TaskFields.Find(Project!, key) is not { } field)
            return null;
        return field.Get(Project!, task) is DateTime date ? date.ToString("d MMM", CultureInfo.CurrentCulture) : field.Format(Project!, task);
    }

    /// <summary>Working minutes of a task's own work before a moment (split gaps excluded), snapped to whole hours.</summary>
    private long? WorkBefore(ProjectTask task, DateTime moment)
    {
        var cal = task.Calendar ?? Project!.Calendar;
        double minutes = 0;
        foreach (var segment in task.Segments)
        {
            if (moment <= segment.Start)
                break;
            var end = moment < segment.Finish ? moment : segment.Finish;
            minutes += cal.WorkingTimeBetween(segment.Start, end).TotalMinutes;
        }
        return (long)Math.Round(minutes / 60) * 60;
    }

    /// <summary>
    /// Progress lines, as in Microsoft Project: from the status date at the top, the line bends to each
    /// in-progress or late task's progress point; tasks that are done or not yet due stay on the status date.
    /// </summary>
    private void DrawProgressLines(DrawingContext dc, IReadOnlyList<TaskRowViewModel> rows, int firstRow, int lastRow)
    {
        var statusDate = Project!.StatusDate ?? DateTime.Now;
        double statusX = XOf(statusDate);
        var points = new List<Point> { new(statusX, HeaderHeight) };
        for (int i = firstRow; i <= lastRow; i++)
        {
            if (rows[i].Task is not { IsSummary: false } task)
                continue;
            double y = RowTop(i) + RowHeight / 2;
            bool due = task.Start < statusDate && task.PercentComplete < 100;
            points.Add(new Point(due ? XOf(task.CompleteThrough) : statusX, y));
        }
        points.Add(new Point(statusX, ActualHeight));
        for (int i = 1; i < points.Count; i++)
            dc.DrawLine(ProgressLinePen, points[i - 1], points[i]);
        foreach (var point in points.Skip(1).Take(points.Count - 2).Where(pt => Math.Abs(pt.X - statusX) > 0.5))
            dc.DrawEllipse(ProgressLinePen.Brush, null, point, 3, 3);
    }
    /// <summary>Group header rows get a roll-up bar spanning their tasks, like Microsoft Project's group summaries.</summary>
    private void DrawGroupBar(DrawingContext dc, Engine.Views.TaskGroup group, int row)
    {
        double top = RowTop(row);
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(0, top, ActualWidth, RowHeight));
        double x1 = XOf(group.Start), x2 = XOf(group.Finish);
        double barTop = top + RowHeight * 0.3;
        dc.DrawRectangle(Drawing.MutedText, null, new Rect(x1, barTop, Math.Max(1, x2 - x1), 5));
        DrawSummaryCap(dc, x1, barTop + 5, Drawing.MutedText);
        DrawSummaryCap(dc, x2, barTop + 5, Drawing.MutedText);
    }

    private static void DrawSummaryCap(DrawingContext dc, double x, double y, Brush fill)
    {
        var cap = new StreamGeometry();
        using (var g = cap.Open())
        {
            g.BeginFigure(new Point(x - 4, y), true, true);
            g.LineTo(new Point(x + 4, y), true, false);
            g.LineTo(new Point(x, y + 5), true, false);
        }
        cap.Freeze();
        dc.DrawGeometry(fill, null, cap);
    }

    private void DrawDeadline(DrawingContext dc, double x, double top, bool missed)
    {
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(new Point(x - 4, top + 2), true, true);
            g.LineTo(new Point(x + 4, top + 2), true, false);
            g.LineTo(new Point(x, top + 8), true, false);
        }
        arrow.Freeze();
        dc.DrawGeometry(missed ? MissedFill : DeadlineFill, null, arrow);
        dc.DrawLine(new Pen(missed ? MissedFill : DeadlineFill, 1.5), new Point(x, top + 7), new Point(x, top + RowHeight - 3));
    }

    // ------------------------------------------------------------------ dependency arrows

    private void DrawLinks(DrawingContext dc, Dictionary<ProjectTask, int> rowIndex, int firstRow, int lastRow)
    {
        var project = Project!;
        foreach (var successor in project.Tasks)
        {
            if (!rowIndex.TryGetValue(successor, out int succRow))
                continue;
            foreach (var link in successor.Predecessors)
            {
                if (project.GetTask(link.PredecessorId) is not { } predecessor || !rowIndex.TryGetValue(predecessor, out int predRow))
                    continue;
                if (predecessor.HideBar || successor.HideBar)
                    continue;
                if (Math.Max(predRow, succRow) < firstRow || Math.Min(predRow, succRow) > lastRow)
                    continue;
                bool critical = ShowCritical(predecessor) && ShowCritical(successor);
                DrawLink(dc, predecessor, predRow, successor, succRow, link.Type, critical ? CriticalLinkPen : LinkPen);
            }
        }
    }

    private (double Start, double End) BarEdges(ProjectTask task)
    {
        double start = XOf(task.Start), end = XOf(task.Finish);
        if (IsMilestone(task))
        {
            double half = RowHeight * 0.3;
            return (start - half, start + half);
        }
        return (start, end);
    }

    private void DrawLink(DrawingContext dc, ProjectTask pred, int predRow, ProjectTask succ, int succRow, LinkType type, Pen pen)
    {
        var (predStart, predEnd) = BarEdges(pred);
        var (succStart, succEnd) = BarEdges(succ);
        double fromY = RowTop(predRow) + RowHeight / 2;
        double toY = RowTop(succRow) + RowHeight / 2;
        bool down = succRow > predRow;
        double gapY = down ? RowTop(predRow) + RowHeight : RowTop(predRow);
        const double Stub = 7;

        var points = new List<Point>();
        Point tip;
        Vector arrowDirection;

        switch (type)
        {
            case LinkType.FinishToStart when succStart >= predEnd + 4:
                // Right from the predecessor, then down (or up) onto the successor's start.
                double x = succStart + 4;
                double edgeY = toY + (down ? -RowHeight * 0.25 : RowHeight * 0.25);
                points.Add(new Point(predEnd, fromY));
                points.Add(new Point(x, fromY));
                tip = new Point(x, edgeY);
                arrowDirection = new Vector(0, down ? 1 : -1);
                break;
            case LinkType.FinishToStart:
                points.Add(new Point(predEnd, fromY));
                points.Add(new Point(predEnd + Stub, fromY));
                points.Add(new Point(predEnd + Stub, gapY));
                points.Add(new Point(succStart - Stub, gapY));
                points.Add(new Point(succStart - Stub, toY));
                tip = new Point(succStart, toY);
                arrowDirection = new Vector(1, 0);
                break;
            case LinkType.StartToStart:
                double left = Math.Min(predStart, succStart) - Stub;
                points.Add(new Point(predStart, fromY));
                points.Add(new Point(left, fromY));
                points.Add(new Point(left, toY));
                tip = new Point(succStart, toY);
                arrowDirection = new Vector(1, 0);
                break;
            case LinkType.FinishToFinish:
                double right = Math.Max(predEnd, succEnd) + Stub;
                points.Add(new Point(predEnd, fromY));
                points.Add(new Point(right, fromY));
                points.Add(new Point(right, toY));
                tip = new Point(succEnd, toY);
                arrowDirection = new Vector(-1, 0);
                break;
            default: // StartToFinish
                points.Add(new Point(predStart, fromY));
                points.Add(new Point(predStart - Stub, fromY));
                points.Add(new Point(predStart - Stub, gapY));
                points.Add(new Point(succEnd + Stub, gapY));
                points.Add(new Point(succEnd + Stub, toY));
                tip = new Point(succEnd, toY);
                arrowDirection = new Vector(-1, 0);
                break;
        }
        points.Add(tip);

        for (int i = 1; i < points.Count; i++)
            dc.DrawLine(pen, points[i - 1], points[i]);
        DrawArrowHead(dc, tip, arrowDirection, pen.Brush);
    }

    private static void DrawArrowHead(DrawingContext dc, Point tip, Vector direction, Brush brush)
    {
        var normal = new Vector(-direction.Y, direction.X);
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(tip, true, true);
            g.LineTo(tip - direction * 6 + normal * 3.5, true, false);
            g.LineTo(tip - direction * 6 - normal * 3.5, true, false);
        }
        head.Freeze();
        dc.DrawGeometry(brush, null, head);
    }

    private void DrawDragFeedback(DrawingContext dc, Dictionary<ProjectTask, int> rowIndex)
    {
        if (_drag == DragMode.None || _dragTask is null || !rowIndex.TryGetValue(_dragTask, out int row))
            return;

        double dx = _dragCurrent.X - _dragOrigin.X;
        double top = RowTop(row) + RowHeight * 0.25, height = RowHeight * 0.5;
        double x1 = XOf(_dragTask.Start), x2 = XOf(_dragTask.Finish);
        switch (_drag)
        {
            case DragMode.Move:
                dc.DrawRectangle(GhostFill, TaskBorder, new Rect(x1 + dx, top, Math.Max(2, x2 - x1), height));
                DrawDragLabel(dc, $"Start: {TaskRowViewModel.FormatDate(SnapStart(_dragTask, DateAt(x1 + dx)))}", x1 + dx, top);
                break;
            case DragMode.Resize:
                dc.DrawRectangle(GhostFill, TaskBorder, new Rect(x1, top, Math.Max(2, x2 - x1 + dx), height));
                DrawDragLabel(dc, $"Finish: {TaskRowViewModel.FormatDate(SnapFinish(_dragTask, DateAt(x2 + dx)))}", x1, top);
                break;
            case DragMode.Progress:
                double px = Math.Clamp(_dragCurrent.X, x1, x2);
                dc.DrawRectangle(ProgressFill, null, new Rect(x1, top + height / 3, px - x1, height / 3));
                DrawDragLabel(dc, $"{ProgressAt(_dragTask, DateAt(px)):0}% complete", x1, top);
                break;
            case DragMode.Link:
                dc.DrawLine(DragLinkPen, new Point(_dragOrigin.X, RowTop(row) + RowHeight / 2), _dragCurrent);
                var target = TaskAt(_dragCurrent);
                var label = target is null || target == _dragTask ? "Drop on a task to link" : $"Link to {target.Name}";
                DrawDragLabel(dc, label, _dragCurrent.X + 8, _dragCurrent.Y - 8);
                break;
        }
    }

    private void DrawDragLabel(DrawingContext dc, string label, double x, double y)
    {
        var text = Text(label, 11);
        var box = new Rect(x, y - text.Height - 4, text.Width + 8, text.Height + 2);
        dc.DrawRectangle(Brushes.LightYellow, GridPen, box);
        dc.DrawText(text, new Point(box.X + 4, box.Y + 1));
    }

    private FormattedText Text(string text, double size) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ------------------------------------------------------------------ mouse editing

    private ProjectTask? TaskAt(Point p)
    {
        if (_viewModel is null || p.Y < HeaderHeight)
            return null;
        int row = (int)((p.Y - HeaderHeight + _verticalOffset) / RowHeight);
        return row >= 0 && row < _viewModel.Rows.Count ? _viewModel.Rows[row].Task : null;
    }

    private DragMode HitTest(ProjectTask task, Point p)
    {
        if (task.IsSummary || task.HideBar)
            return DragMode.None;
        var (start, end) = BarEdges(task);
        if (p.X < start - EdgeGrip || p.X > end + EdgeGrip)
            return DragMode.None;
        if (IsMilestone(task))
            return DragMode.Move;
        if (Math.Abs(p.X - end) <= EdgeGrip)
            return DragMode.Resize;
        if (Math.Abs(p.X - start) <= EdgeGrip)
            return DragMode.Progress;
        return DragMode.Move;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        if (TaskAt(p) is not { } task)
            return;

        if (_viewModel is { IsSplitMode: true })
        {
            if (MainViewModel.CanSplit(task) && WorkBefore(task, DateAt(p.X)) is { } offset && offset > 0 && offset < task.DurationMinutes)
                _viewModel.SplitTask(task, offset, Project!.Options.MinutesPerDay);
            else
                _viewModel.IsSplitMode = false;
            e.Handled = true;
            return;
        }
        TaskClicked?.Invoke(this, task);
        if (e.ClickCount == 2)
        {
            TaskDoubleClicked?.Invoke(this, task);
            return;
        }

        var mode = HitTest(task, p);
        if (mode == DragMode.None)
            return;
        _drag = mode;
        _dragTask = task;
        _dragOrigin = _dragCurrent = p;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);

        if (_drag != DragMode.None && _dragTask is not null)
        {
            _dragCurrent = p;
            // A mostly vertical drag out of a bar's row turns a move into a link.
            if (_drag == DragMode.Move && Math.Abs(p.Y - _dragOrigin.Y) > RowHeight * 0.75 && Math.Abs(p.X - _dragOrigin.X) < RowHeight * 2)
                _drag = DragMode.Link;
            InvalidateVisual();
            return;
        }

        var task = TaskAt(p);
        if (_viewModel is { IsSplitMode: true })
        {
            Cursor = task is not null && MainViewModel.CanSplit(task) ? Cursors.Cross : Cursors.No;
            return;
        }
        Cursor = task is null ? null : HitTest(task, p) switch
        {
            DragMode.Resize => Cursors.SizeWE,
            DragMode.Progress => Cursors.ScrollE,
            DragMode.Move => Cursors.SizeAll,
            _ => null,
        };
        if (task != _hoverTask)
        {
            _hoverTask = task;
            ToolTip = task is null ? null : Describe(task);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == DragMode.None || _dragTask is null || _viewModel is null)
            return;

        var (mode, task, p) = (_drag, _dragTask, e.GetPosition(this));
        EndDrag();

        double dx = p.X - _dragOrigin.X;
        switch (mode)
        {
            case DragMode.Move when Math.Abs(dx) >= 3:
                var newStart = SnapStart(task, DateAt(XOf(task.Start) + dx));
                if (newStart != task.Start)
                    _viewModel.MoveTask(task, newStart);
                break;
            case DragMode.Resize when Math.Abs(dx) >= 3:
                var finish = SnapFinish(task, DateAt(XOf(task.Finish) + dx));
                var cal = task.Calendar ?? Project!.Calendar;
                _viewModel.ResizeTask(task, Math.Max(0, cal.WorkingMinutesBetween(task.Start, finish)));
                break;
            case DragMode.Progress:
                _viewModel.SetProgress(task, ProgressAt(task, DateAt(Math.Clamp(p.X, XOf(task.Start), XOf(task.Finish)))));
                break;
            case DragMode.Link:
                if (TaskAt(p) is { } target && target != task)
                    _viewModel.LinkTasks(task, target);
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _viewModel is { IsSplitMode: true })
        {
            _viewModel.IsSplitMode = false;
            e.Handled = true;
        }
        if (e.Key == Key.Escape && _drag != DragMode.None)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag != DragMode.None)
            EndDrag();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewModel is null)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom around the date under the mouse.
            var anchorX = e.GetPosition(this).X;
            var anchor = DateAt(anchorX);
            _viewModel.ZoomIndex += e.Delta > 0 ? 1 : -1;
            KeepDateAt(anchor, anchorX);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            HorizontalOffset -= e.Delta;
            RaiseExtentChanged();
        }
        else
        {
            WheelScrolled?.Invoke(this, e.Delta);
        }
        e.Handled = true;
    }

    private void EndDrag()
    {
        _drag = DragMode.None;
        _dragTask = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>Snaps a dragged start to the timescale: whole hours when zoomed into hours, otherwise days.</summary>
    private DateTime SnapStart(ProjectTask task, DateTime date)
    {
        var cal = task.Calendar ?? Project!.Calendar;
        var snapped = PixelsPerDay >= 100 ? date.Date.AddHours(Math.Round(date.TimeOfDay.TotalHours)) : date.Date.AddDays(date.TimeOfDay.TotalHours >= 12 ? 1 : 0);
        return cal.NextWorkingMoment(snapped);
    }

    private DateTime SnapFinish(ProjectTask task, DateTime date)
    {
        var cal = task.Calendar ?? Project!.Calendar;
        var snapped = PixelsPerDay >= 100 ? date.Date.AddHours(Math.Round(date.TimeOfDay.TotalHours)) : date.Date.AddDays(date.TimeOfDay.TotalHours >= 12 ? 1 : 0);
        var finish = cal.PreviousWorkingMoment(snapped);
        return finish < task.Start ? task.Start : finish;
    }

    private double ProgressAt(ProjectTask task, DateTime date)
    {
        var cal = task.Calendar ?? Project!.Calendar;
        long total = cal.WorkingMinutesBetween(task.Start, task.Finish);
        return total <= 0 ? 0 : Math.Clamp(100.0 * cal.WorkingMinutesBetween(task.Start, date) / total, 0, 100);
    }

    private string Describe(ProjectTask task)
    {
        var options = Project!.Options;
        var lines = new List<string>
        {
            task.Name,
            $"Start: {task.Start:g}",
            $"Finish: {task.Finish:g}",
            $"Duration: {DurationText.Format(task.DurationMinutes, options, isElapsed: task.IsElapsedDuration)}",
            $"Complete: {task.PercentComplete:0.#}%",
            $"Total slack: {DurationText.Format(task.TotalFloatMinutes, options)}",
        };
        if (task.IsCritical)
            lines.Add("Critical");
        if (task.Deadline is { } deadline)
            lines.Add($"Deadline: {deadline:g}{(task.MissesDeadline ? " (missed)" : "")}");
        return string.Join(Environment.NewLine, lines);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness, bool dashed = false)
    {
        var pen = new Pen(Frozen(color), thickness);
        if (dashed)
            pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return pen;
    }
}
