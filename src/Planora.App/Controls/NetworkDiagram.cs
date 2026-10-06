using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// The Network Diagram: a box per task, laid out left to right by dependency depth, with link arrows.
/// Rows within a column are ordered by the average position of each task's predecessors to reduce
/// crossing lines. Critical tasks have red boxes.
/// </summary>
public sealed class NetworkDiagram : FrameworkElement, ITaskCanvas
{
    private const double BoxWidth = 190;
    private const double BoxHeight = 86;
    private const double ColumnGap = 60;
    private const double RowGap = 24;
    private const double Edge = 20;

    private MainViewModel? _viewModel;
    private Dictionary<ProjectTask, Rect> _boxes = new();
    private List<(ProjectTask From, ProjectTask To)> _edges = new();
    private double _scale = 1;

    public NetworkDiagram()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
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
            OnRefreshed(this, EventArgs.Empty);
        }
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        Layout();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Assigns each task a column (longest chain of predecessors) and a row.</summary>
    private void Layout()
    {
        _boxes = new();
        _edges = new();
        var project = _viewModel?.Project;
        if (project is null)
            return;

        var tasks = project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        var shown = tasks.ToHashSet();
        IEnumerable<ProjectTask> Expand(ProjectTask t) => t.IsSummary ? project.Subtree(t).Where(shown.Contains) : shown.Contains(t) ? new[] { t } : Array.Empty<ProjectTask>();

        // Links on summary tasks are drawn between the leaf tasks beneath them.
        var predecessors = tasks.ToDictionary(t => t, _ => new List<ProjectTask>());
        foreach (var successor in project.Tasks)
        {
            foreach (var link in successor.Predecessors)
            {
                if (project.GetTask(link.PredecessorId) is not { } pred)
                    continue;
                foreach (var to in Expand(successor))
                {
                    foreach (var from in Expand(pred))
                    {
                        if (from != to && !predecessors[to].Contains(from))
                        {
                            predecessors[to].Add(from);
                            _edges.Add((from, to));
                        }
                    }
                }
            }
        }

        var column = new Dictionary<ProjectTask, int>();
        int ColumnOf(ProjectTask t, HashSet<ProjectTask> visiting)
        {
            if (column.TryGetValue(t, out int c))
                return c;
            if (!visiting.Add(t))
                return 0; // dependency loop: the scheduler reports it; just avoid recursing forever
            c = predecessors[t].Count == 0 ? 0 : predecessors[t].Max(p => ColumnOf(p, visiting)) + 1;
            visiting.Remove(t);
            return column[t] = c;
        }
        foreach (var t in tasks)
            ColumnOf(t, new HashSet<ProjectTask>());

        var rowOf = new Dictionary<ProjectTask, double>();
        foreach (var group in tasks.GroupBy(t => column[t]).OrderBy(g => g.Key))
        {
            var ordered = group
                .OrderBy(t => predecessors[t].Count == 0 ? project.IndexOf(t) : predecessors[t].Average(p => rowOf.GetValueOrDefault(p)))
                .ThenBy(project.IndexOf)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                rowOf[ordered[i]] = i;
                _boxes[ordered[i]] = new Rect(
                    Edge + group.Key * (BoxWidth + ColumnGap),
                    Edge + i * (BoxHeight + RowGap),
                    BoxWidth, BoxHeight);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_boxes.Count == 0)
            return new Size(0, 0);
        return new Size(
            (_boxes.Values.Max(b => b.Right) + Edge) * _scale,
            (_boxes.Values.Max(b => b.Bottom) + Edge) * _scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        if (_viewModel is null)
            return;
        if (_boxes.Count == 0)
        {
            dc.DrawText(Drawing.Text(this, "Add tasks to see them in the network diagram.", 14, Drawing.MutedText), new Point(20, 20));
            return;
        }

        dc.PushTransform(new ScaleTransform(_scale, _scale));
        bool showCritical = _viewModel.ShowCriticalPath;
        foreach (var (from, to) in _edges)
        {
            var a = _boxes[from];
            var b = _boxes[to];
            var pen = showCritical && from.IsCritical && to.IsCritical ? Drawing.CriticalLinkPen : Drawing.LinkPen;
            var start = new Point(a.Right, a.Top + a.Height / 2);
            var end = new Point(b.Left, b.Top + b.Height / 2);
            double midX = b.Left - ColumnGap / 2;
            if (midX <= start.X)
                midX = start.X + 10;
            dc.DrawLine(pen, start, new Point(midX, start.Y));
            dc.DrawLine(pen, new Point(midX, start.Y), new Point(midX, end.Y));
            dc.DrawLine(pen, new Point(midX, end.Y), end);
            Drawing.ArrowHead(dc, end, new Vector(1, 0), pen.Brush);
        }

        var selected = _viewModel.SelectedTasks.ToHashSet();
        var options = _viewModel.Project.Options;
        foreach (var (task, box) in _boxes)
        {
            bool critical = showCritical && task.IsCritical;
            var fill = task.PercentComplete >= 100 ? Drawing.DoneFill : critical ? Drawing.OverallocatedFill : Drawing.Background;
            var border = selected.Contains(task) ? Drawing.SelectedBorder : critical ? Drawing.CriticalBorder : Drawing.TaskBorder;
            dc.DrawRoundedRectangle(fill, border, box, task.IsMilestone || task.DurationMinutes == 0 ? 14 : 3, task.IsMilestone || task.DurationMinutes == 0 ? 14 : 3);

            if (task.PercentComplete > 0 && task.PercentComplete < 100)
            {
                // Diagonal mark for in-progress tasks, as in Microsoft Project.
                dc.DrawLine(border, box.TopLeft, box.BottomRight);
            }

            double x = box.Left + 8, y = box.Top + 5, width = box.Width - 16;
            dc.DrawText(Drawing.Text(this, task.Name, 12, bold: true, maxWidth: width), new Point(x, y));
            y += 20;
            void Line(string label, string value)
            {
                dc.DrawText(Drawing.Text(this, label, 10.5, Drawing.MutedText), new Point(x, y));
                dc.DrawText(Drawing.Text(this, value, 10.5, maxWidth: width - 48), new Point(x + 48, y));
                y += 15;
            }
            Line("Start:", DateText.Format(task.Start));
            Line("Finish:", DateText.Format(task.Finish));
            Line("ID: " + (_viewModel.Project.IndexOf(task) + 1), "Dur: " + DurationText.Format(task.DurationMinutes, options, isElapsed: task.IsElapsedDuration));
            var resources = ResourceNamesText.Format(task);
            if (resources.Length > 0)
                Line("Res:", resources);
        }
        dc.Pop();

        if (_linking)
            dc.DrawLine(new Pen(Drawing.TextBrush, 1.5) { DashStyle = DashStyles.Dash }, _linkStart, _linkCurrent);
    }

    private ProjectTask? TaskAt(Point p)
    {
        var point = new Point(p.X / _scale, p.Y / _scale);
        return _boxes.FirstOrDefault(kv => kv.Value.Contains(point)).Key;
    }

    private ProjectTask? _linkFrom;
    private Point _linkStart;
    private Point _linkCurrent;
    private bool _linking;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (TaskAt(e.GetPosition(this)) is not { } task)
            return;
        TaskClicked?.Invoke(this, task);
        if (e.ClickCount == 2)
        {
            TaskDoubleClicked?.Invoke(this, task);
            e.Handled = true;
            return;
        }
        // Dragging from one box to another links them, as in Microsoft Project's Network Diagram.
        _linkFrom = task;
        _linkStart = _linkCurrent = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_linkFrom is null)
            return;
        _linkCurrent = e.GetPosition(this);
        if (!_linking && (_linkCurrent - _linkStart).Length > 8)
            _linking = true;
        if (_linking)
            InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (from, linking) = (_linkFrom, _linking);
        _linkFrom = null;
        _linking = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();
        if (linking && from is not null && TaskAt(e.GetPosition(this)) is { } to && to != from)
            _viewModel?.LinkTasks(from, to);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            base.OnMouseWheel(e);
            return;
        }
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.3, 2.5);
        InvalidateMeasure();
        InvalidateVisual();
        e.Handled = true;
    }
}
