using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// The Task Board: cards for each task in Not Started / In Progress / Complete columns. Dragging a card
/// to another column sets its % complete (0%, 50%, 100%), as Microsoft Project's board does by default.
/// </summary>
public sealed class TaskBoard : FrameworkElement, ITaskCanvas
{
    private static readonly (string Title, double Percent)[] Columns = { ("Not Started", 0), ("In Progress", 50), ("Complete", 100) };
    private const double ColumnWidth = 260;
    private const double ColumnGap = 14;
    private const double CardHeight = 78;
    private const double CardGap = 8;
    private const double HeaderHeight = 36;
    private const double Padding = 14;

    private MainViewModel? _viewModel;
    private readonly List<(Rect Bounds, ProjectTask Task)> _cards = new();
    private ProjectTask? _dragTask;
    private Point _dragStart;
    private Point _dragCurrent;
    private bool _dragging;

    public TaskBoard()
    {
        Focusable = true;
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

    private static int ColumnOf(ProjectTask task) => task.PercentComplete >= 100 ? 2 : task.PercentComplete > 0 ? 1 : 0;

    private List<ProjectTask> Tasks => _viewModel?.Project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList() ?? new();

    protected override Size MeasureOverride(Size availableSize)
    {
        int most = Tasks.GroupBy(ColumnOf).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        return new Size(Padding * 2 + Columns.Length * (ColumnWidth + ColumnGap), Padding * 2 + HeaderHeight + most * (CardHeight + CardGap));
    }

    protected override void OnRender(DrawingContext dc)
    {
        _cards.Clear();
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(RenderSize));
        if (_viewModel is null)
            return;

        var project = _viewModel.Project;
        var selected = _viewModel.SelectedTasks.ToHashSet();
        var tasks = Tasks;
        for (int c = 0; c < Columns.Length; c++)
        {
            double x = Padding + c * (ColumnWidth + ColumnGap);
            var inColumn = tasks.Where(t => ColumnOf(t) == c).ToList();
            var columnRect = new Rect(x, Padding, ColumnWidth, Math.Max(ActualHeight - 2 * Padding, HeaderHeight));
            bool dropTarget = _dragging && columnRect.Contains(_dragCurrent);
            dc.DrawRoundedRectangle(dropTarget ? Drawing.SelectedRow : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)), null, columnRect, 6, 6);
            dc.DrawText(Drawing.Text(this, $"{Columns[c].Title}  ({inColumn.Count})", 13, bold: true), new Point(x + 10, Padding + 9));

            double y = Padding + HeaderHeight;
            foreach (var task in inColumn)
            {
                var card = new Rect(x + 8, y, ColumnWidth - 16, CardHeight);
                bool critical = _viewModel.ShowCriticalPath && task.IsCritical;
                dc.DrawRoundedRectangle(Drawing.Background, selected.Contains(task) ? Drawing.SelectedBorder : Drawing.GridPen, card, 4, 4);
                dc.DrawRectangle(critical ? Drawing.CriticalBorder.Brush : Drawing.TaskBorder.Brush, null, new Rect(card.Left, card.Top + 4, 3, card.Height - 8));
                dc.DrawText(Drawing.Text(this, task.Name, 12, bold: true, maxWidth: card.Width - 20), new Point(card.Left + 10, card.Top + 6));
                dc.DrawText(Drawing.Text(this, $"{DateText.Format(task.Start)} – {DateText.Format(task.Finish)}", 10.5, Drawing.MutedText, maxWidth: card.Width - 20),
                    new Point(card.Left + 10, card.Top + 26));
                var resources = ResourceNamesText.Format(task);
                if (resources.Length > 0)
                    dc.DrawText(Drawing.Text(this, resources, 10.5, maxWidth: card.Width - 20), new Point(card.Left + 10, card.Top + 42));

                var track = new Rect(card.Left + 10, card.Bottom - 12, card.Width - 20, 5);
                dc.DrawRoundedRectangle(Drawing.HeaderBackground, null, track, 2, 2);
                dc.DrawRoundedRectangle(Drawing.ProgressFill, null, new Rect(track.Left, track.Top, track.Width * task.PercentComplete / 100, track.Height), 2, 2);

                _cards.Add((card, task));
                y += CardHeight + CardGap;
            }
        }

        if (_dragging && _dragTask is not null)
        {
            var ghost = new Rect(_dragCurrent.X - 100, _dragCurrent.Y - 15, 200, 30);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), Drawing.SelectedBorder, ghost, 4, 4);
            dc.DrawText(Drawing.Text(this, _dragTask.Name, 12, bold: true, maxWidth: 184), new Point(ghost.Left + 8, ghost.Top + 6));
        }
    }

    private int? ColumnAt(Point p)
    {
        for (int c = 0; c < Columns.Length; c++)
        {
            double x = Padding + c * (ColumnWidth + ColumnGap);
            if (p.X >= x && p.X <= x + ColumnWidth)
                return c;
        }
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        var hit = _cards.FirstOrDefault(c => c.Bounds.Contains(p));
        if (hit.Task is null)
            return;
        TaskClicked?.Invoke(this, hit.Task);
        if (e.ClickCount == 2)
        {
            TaskDoubleClicked?.Invoke(this, hit.Task);
            return;
        }
        _dragTask = hit.Task;
        _dragStart = p;
        CaptureMouse();
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
        var (task, dragging) = (_dragTask, _dragging);
        _dragTask = null;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();

        if (task is null || !dragging || _viewModel is null || ColumnAt(e.GetPosition(this)) is not { } column || column == ColumnOf(task))
            return;
        _viewModel.SetProgress(task, Columns[column].Percent);
    }
}
