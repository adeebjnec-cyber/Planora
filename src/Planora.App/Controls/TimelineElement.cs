using System.Windows;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>
/// Base for views drawn against a time axis (Gantt chart, usage grids): maps dates to pixels, keeps the
/// timeline covering the project (and today) with margins, and scrolls horizontally.
/// </summary>
public abstract class TimelineElement : FrameworkElement
{
    private double _horizontalOffset;

    /// <summary>Raised when the timeline width changes, so the host can size its horizontal scroll bar.</summary>
    public event EventHandler? ExtentChanged;

    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            _horizontalOffset = Math.Max(0, value);
            InvalidateVisual();
        }
    }

    public DateTime TimelineStart { get; private set; }
    public DateTime TimelineEnd { get; private set; }
    public double ExtentWidth => (TimelineEnd - TimelineStart).TotalDays * PixelsPerDay;

    protected abstract double PixelsPerDay { get; }
    protected abstract Project? Project { get; }

    public double XOf(DateTime date) => (date - TimelineStart).TotalDays * PixelsPerDay - _horizontalOffset;

    public DateTime DateAt(double x) => TimelineStart.AddDays((x + _horizontalOffset) / PixelsPerDay);

    /// <summary>Scrolls horizontally so that a date is near the left edge.</summary>
    public void ScrollTo(DateTime date)
    {
        HorizontalOffset = Math.Max(0, (date - TimelineStart).TotalDays * PixelsPerDay - 40);
        RaiseExtentChanged();
    }

    /// <summary>Changes zoom while keeping the date at <paramref name="anchorX"/> in place.</summary>
    protected void KeepDateAt(DateTime anchor, double anchorX)
    {
        UpdateTimeline();
        HorizontalOffset = (anchor - TimelineStart).TotalDays * PixelsPerDay - anchorX;
        RaiseExtentChanged();
    }

    protected void RaiseExtentChanged() => ExtentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Timeline spans the project with margins, and always includes today.</summary>
    protected void UpdateTimeline()
    {
        var p = Project;
        if (p is null)
            return;
        var start = p.Tasks.Count > 0 ? p.Start : p.StartDate;
        var finish = p.Tasks.Count > 0 ? p.Finish : p.StartDate;
        if (DateTime.Today < start) start = DateTime.Today;
        if (DateTime.Today > finish) finish = DateTime.Today;

        double marginDays = Math.Max(7, 60 / PixelsPerDay);
        var (top, _) = Timescale.For(PixelsPerDay);
        var newStart = top.Floor(start.AddDays(-marginDays));
        var newEnd = finish.AddDays(Math.Max(28, ActualWidth / PixelsPerDay));

        if (newStart != TimelineStart || newEnd != TimelineEnd)
        {
            // Keep the visible date steady when the timeline origin moves.
            var anchor = TimelineStart == default ? (DateTime?)null : DateAt(0);
            TimelineStart = newStart;
            TimelineEnd = newEnd;
            if (anchor is { } a)
                _horizontalOffset = Math.Max(0, (a - TimelineStart).TotalDays * PixelsPerDay);
            RaiseExtentChanged();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateTimeline();
    }
}
