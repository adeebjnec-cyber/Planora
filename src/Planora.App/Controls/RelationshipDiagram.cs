using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// Relationship Diagram: the selected task in the middle, its predecessors on the left and successors on
/// the right, with each link's type and lag. Click a neighbour to move to it.
/// </summary>
public sealed class RelationshipDiagram : FrameworkElement, ITaskCanvas
{
    private const double BoxWidth = 200;
    private const double BoxHeight = 64;
    private const double Gap = 16;
    private const double ColumnGap = 120;

    private MainViewModel? _viewModel;
    private readonly List<(Rect Box, ProjectTask Task)> _boxes = new();

    public RelationshipDiagram()
    {
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
        InvalidateMeasure();
        InvalidateVisual();
    }

    private ProjectTask? Focus => _viewModel?.SelectedTasks.FirstOrDefault();

    private (List<(ProjectTask Task, TaskLink Link)> Before, List<(ProjectTask Task, TaskLink Link)> After) Neighbours(Project project, ProjectTask task)
    {
        var before = task.Predecessors
            .Select(l => (Task: project.GetTask(l.PredecessorId), Link: l))
            .Where(x => x.Task is not null)
            .Select(x => (x.Task!, x.Link))
            .ToList();
        var after = project.Tasks
            .SelectMany(t => t.Predecessors.Where(l => l.PredecessorId == task.Id).Select(l => (Task: t, Link: l)))
            .ToList();
        return (before, after);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_viewModel?.Project is not { } project || Focus is not { } task)
            return new Size(0, 0);
        var (before, after) = Neighbours(project, task);
        int rows = Math.Max(1, Math.Max(before.Count, after.Count));
        return new Size(3 * BoxWidth + 2 * ColumnGap + 40, rows * (BoxHeight + Gap) + 80);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        _boxes.Clear();
        if (_viewModel?.Project is not { } project)
            return;
        if (Focus is not { } task)
        {
            dc.DrawText(Drawing.Text(this, "Select a task (in the Gantt Chart or another view) to see its predecessors and successors.", 13, Drawing.MutedText), new Point(20, 20));
            return;
        }
        var (before, after) = Neighbours(project, task);
        int rows = Math.Max(1, Math.Max(before.Count, after.Count));
        double height = rows * (BoxHeight + Gap) - Gap;
        double top = 50;
        double left = 20, middle = left + BoxWidth + ColumnGap, right = middle + BoxWidth + ColumnGap;

        dc.DrawText(Drawing.Text(this, "Predecessors", 12, Drawing.MutedText, bold: true), new Point(left, 20));
        dc.DrawText(Drawing.Text(this, "Task", 12, Drawing.MutedText, bold: true), new Point(middle, 20));
        dc.DrawText(Drawing.Text(this, "Successors", 12, Drawing.MutedText, bold: true), new Point(right, 20));

        var center = new Rect(middle, top + (height - BoxHeight) / 2, BoxWidth, BoxHeight);
        DrawBox(dc, project, task, center, focus: true);

        for (int i = 0; i < before.Count; i++)
        {
            var (pred, link) = before[i];
            var box = new Rect(left, top + i * (BoxHeight + Gap), BoxWidth, BoxHeight);
            DrawBox(dc, project, pred, box, focus: false);
            Arrow(dc, project, new Point(box.Right, box.Top + BoxHeight / 2), new Point(center.Left, center.Top + BoxHeight / 2), link, pred.IsCritical && task.IsCritical);
        }
        for (int i = 0; i < after.Count; i++)
        {
            var (succ, link) = after[i];
            var box = new Rect(right, top + i * (BoxHeight + Gap), BoxWidth, BoxHeight);
            DrawBox(dc, project, succ, box, focus: false);
            Arrow(dc, project, new Point(center.Right, center.Top + BoxHeight / 2), new Point(box.Left, box.Top + BoxHeight / 2), link, succ.IsCritical && task.IsCritical);
        }
        if (before.Count == 0)
            dc.DrawText(Drawing.Text(this, "(none)", 12, Drawing.MutedText), new Point(left, top));
        if (after.Count == 0)
            dc.DrawText(Drawing.Text(this, "(none)", 12, Drawing.MutedText), new Point(right, top));
    }

    private void DrawBox(DrawingContext dc, Project project, ProjectTask task, Rect box, bool focus)
    {
        var border = task.IsCritical ? Drawing.CriticalBorder : Drawing.TaskBorder;
        dc.DrawRoundedRectangle(focus ? Drawing.SelectedRow : Drawing.Background, focus ? Drawing.SelectedBorder : border, box, 4, 4);
        var name = Drawing.Text(this, $"{project.IndexOf(task) + 1}  {task.Name}", 12, bold: true, maxWidth: box.Width - 12);
        dc.DrawText(name, new Point(box.X + 6, box.Y + 6));
        var dates = Drawing.Text(this, $"{task.Start.ToString("d MMM", CultureInfo.CurrentCulture)} – {task.Finish.ToString("d MMM", CultureInfo.CurrentCulture)}", 11, Drawing.MutedText, maxWidth: box.Width - 12);
        dc.DrawText(dates, new Point(box.X + 6, box.Y + 26));
        var slack = Drawing.Text(this, $"Slack: {DurationText.Format(task.TotalFloatMinutes, project.Options)}", 11, Drawing.MutedText, maxWidth: box.Width - 12);
        dc.DrawText(slack, new Point(box.X + 6, box.Y + 43));
        _boxes.Add((box, task));
    }

    private void Arrow(DrawingContext dc, Project project, Point from, Point to, TaskLink link, bool critical)
    {
        var pen = critical ? Drawing.CriticalLinkPen : Drawing.LinkPen;
        double midX = (from.X + to.X) / 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false, false);
            g.LineTo(new Point(midX, from.Y), true, false);
            g.LineTo(new Point(midX, to.Y), true, false);
            g.LineTo(to, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        Drawing.ArrowHead(dc, to, new Vector(1, 0), pen.Brush);
        string type = link.Type switch { LinkType.StartToStart => "SS", LinkType.FinishToFinish => "FF", LinkType.StartToFinish => "SF", _ => "FS" };
        string lag = link.LagMinutes == 0 && link.LagPercent == 0 ? "" : " " + DurationText.FormatLag(link, project.Options);
        var label = Drawing.Text(this, type + lag, 11, pen.Brush);
        dc.DrawRectangle(Drawing.Background, null, new Rect(midX - label.Width / 2 - 2, (from.Y + to.Y) / 2 - label.Height / 2, label.Width + 4, label.Height));
        dc.DrawText(label, new Point(midX - label.Width / 2, (from.Y + to.Y) / 2 - label.Height / 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        var hit = _boxes.LastOrDefault(b => b.Box.Contains(p));
        if (hit.Task is null)
            return;
        if (e.ClickCount == 2)
            TaskDoubleClicked?.Invoke(this, hit.Task);
        else
            TaskClicked?.Invoke(this, hit.Task);
    }
}
