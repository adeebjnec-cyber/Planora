using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>A row of the Task Usage or Resource Usage view: a task/resource or one of its assignments.</summary>
public sealed class UsageRow
{
    public required string Name { get; init; }
    public int Level { get; init; }
    public bool IsGroup { get; init; }
    public double IndentWidth => Level * 16;
    public required string TotalWork { get; init; }
    public string Details { get; init; } = "Work";

    /// <summary>Amount between two moments: work in minutes, or material quantity.</summary>
    public required Func<DateTime, DateTime, double> WorkBetween { get; init; }

    /// <summary>Formats a cell amount (hours of work by default).</summary>
    public Func<double, string> FormatValue { get; init; } = UsageGrid.FormatHours;

    /// <summary>Over-allocated periods (resource rows only).</summary>
    public IReadOnlyList<(DateTime From, DateTime To)> Overallocations { get; init; } = Array.Empty<(DateTime, DateTime)>();

    public bool IsOverallocatedResource => Overallocations.Count > 0;
    public ProjectTask? Task { get; init; }
    public Resource? Resource { get; init; }
    public Assignment? Assignment { get; init; }
}

/// <summary>
/// The time-phased half of the usage views: one cell per row per timescale period showing hours of
/// work, with over-allocated periods highlighted in red.
/// </summary>
public sealed class UsageGrid : TimelineElement
{
    private MainViewModel? _viewModel;
    private double _verticalOffset;

    public UsageGrid()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public double RowHeight { get; set; } = 24;
    public double HeaderHeight { get; set; } = 44;
    public IReadOnlyList<UsageRow> Rows { get; set; } = Array.Empty<UsageRow>();

    public event EventHandler<int>? WheelScrolled;

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

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Drawing.Background, null, new Rect(RenderSize));
        if (Project is null)
            return;

        var from = DateAt(0);
        var to = DateAt(ActualWidth);
        var (_, bottom) = Timescale.For(PixelsPerDay);
        var boundaries = bottom.Boundaries(from, to).ToList();
        boundaries.Add(bottom.Next(boundaries[^1]));

        int firstRow = Math.Max(0, (int)(_verticalOffset / RowHeight));
        int lastRow = Math.Min(Rows.Count - 1, (int)((_verticalOffset + ActualHeight - HeaderHeight) / RowHeight) + 1);

        dc.PushClip(new RectangleGeometry(new Rect(0, HeaderHeight, ActualWidth, Math.Max(0, ActualHeight - HeaderHeight))));
        for (int r = firstRow; r <= lastRow; r++)
        {
            var row = Rows[r];
            double top = HeaderHeight + r * RowHeight - _verticalOffset;
            if (row.IsGroup)
                dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(0, top, ActualWidth, RowHeight));

            for (int i = 0; i + 1 < boundaries.Count; i++)
            {
                var (periodStart, periodEnd) = (boundaries[i], boundaries[i + 1]);
                double x1 = XOf(periodStart), x2 = XOf(periodEnd);
                var cell = new Rect(x1, top, x2 - x1, RowHeight);

                if (row.Overallocations.Any(o => o.From < periodEnd && o.To > periodStart))
                    dc.DrawRectangle(Drawing.OverallocatedFill, null, cell);

                double minutes = row.WorkBetween(periodStart, periodEnd);
                if (minutes >= 0.5)
                {
                    var brush = row.Overallocations.Any(o => o.From < periodEnd && o.To > periodStart) ? Drawing.AlertText : Drawing.TextBrush;
                    var text = Drawing.Text(this, row.FormatValue(minutes), 11, brush, bold: row.IsGroup);
                    if (text.Width < cell.Width - 4)
                        dc.DrawText(text, new Point(x2 - text.Width - 4, top + (RowHeight - text.Height) / 2));
                }
                dc.DrawLine(Drawing.LightGridPen, new Point(Math.Round(x2) + 0.5, top), new Point(Math.Round(x2) + 0.5, top + RowHeight));
            }
            dc.DrawLine(Drawing.LightGridPen, new Point(0, top + RowHeight - 0.5), new Point(ActualWidth, top + RowHeight - 0.5));
        }
        dc.Pop();

        Drawing.TimescaleHeader(dc, this, XOf, from, to, PixelsPerDay, ActualWidth, HeaderHeight);
    }

    public static string FormatHours(double minutes)
    {
        double hours = minutes / 60;
        return (hours >= 100 ? hours.ToString("0", CultureInfo.CurrentCulture) : hours.ToString("0.##", CultureInfo.CurrentCulture)) + "h";
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewModel is null)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
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
}
