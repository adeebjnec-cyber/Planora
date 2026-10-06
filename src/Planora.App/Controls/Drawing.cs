using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Planora.App.Controls;

/// <summary>Shared colours, pens and text for the custom-drawn views (Microsoft Project-like palette).</summary>
public static class Drawing
{
    public static readonly Brush Background = Brush(0xFF, 0xFF, 0xFF);
    public static readonly Brush HeaderBackground = Brush(0xF3, 0xF3, 0xF3);
    public static readonly Brush NonWorking = Brush(0xEE, 0xEE, 0xEE);
    public static readonly Brush SelectedRow = Brush(0xDC, 0xE9, 0xF7);
    public static readonly Brush TaskFill = Brush(0x8A, 0xB4, 0xE6);
    public static readonly Brush CriticalFill = Brush(0xF0, 0x8E, 0x82);
    public static readonly Brush ManualFill = Brush(0xB9, 0xE3, 0xEA);
    public static readonly Brush ProgressFill = Brush(0x1F, 0x3A, 0x60);
    public static readonly Brush SummaryFill = Brush(0x20, 0x20, 0x20);
    public static readonly Brush DoneFill = Brush(0xB8, 0xD8, 0xB0);
    public static readonly Brush OverallocatedFill = Brush(0xFD, 0xD8, 0xD4);
    public static readonly Brush TextBrush = Brush(0x30, 0x30, 0x30);
    public static readonly Brush MutedText = Brush(0x70, 0x70, 0x70);
    public static readonly Brush AlertText = Brush(0xC6, 0x28, 0x28);
    public static readonly Pen TaskBorder = Pen(0x3E, 0x6F, 0xB0, 1);
    public static readonly Pen CriticalBorder = Pen(0xC0, 0x40, 0x30, 1);
    public static readonly Pen SelectedBorder = Pen(0x1E, 0x3F, 0x70, 2);
    public static readonly Pen GridPen = Pen(0xD8, 0xD8, 0xD8, 1);
    public static readonly Pen LightGridPen = Pen(0xEA, 0xEA, 0xEA, 1);
    public static readonly Pen LinkPen = Pen(0x2F, 0x5F, 0xA8, 1);
    public static readonly Pen CriticalLinkPen = Pen(0xC0, 0x40, 0x30, 1);
    public static readonly Pen TodayPen = Pen(0x2E, 0x9E, 0x4F, 1.5);
    public static readonly Typeface Font = new("Segoe UI");
    public static readonly Typeface BoldFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static FormattedText Text(Visual visual, string text, double size, Brush? brush = null, bool bold = false, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? BoldFont : Font, size, brush ?? TextBrush, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        return formatted;
    }

    public static void ArrowHead(DrawingContext dc, Point tip, Vector direction, Brush brush)
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

    public static void Diamond(DrawingContext dc, Point center, double size, Brush brush)
    {
        var diamond = new StreamGeometry();
        using (var g = diamond.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - size), true, true);
            g.LineTo(new Point(center.X + size, center.Y), true, false);
            g.LineTo(new Point(center.X, center.Y + size), true, false);
            g.LineTo(new Point(center.X - size, center.Y), true, false);
        }
        diamond.Freeze();
        dc.DrawGeometry(brush, null, diamond);
    }

    /// <summary>
    /// Draws the two-tier timescale header used by the Gantt chart and the usage views.
    /// <paramref name="xOf"/> maps a date to an x position.
    /// </summary>
    public static void TimescaleHeader(DrawingContext dc, Visual visual, Func<DateTime, double> xOf,
        DateTime from, DateTime to, double pixelsPerDay, double width, double height)
    {
        double tierHeight = height / 2;
        dc.DrawRectangle(HeaderBackground, null, new Rect(0, 0, width, height));
        var (top, bottom) = Timescale.For(pixelsPerDay);
        Tier(top, 0, centerLabels: false);
        Tier(bottom, tierHeight, centerLabels: true);
        dc.DrawLine(GridPen, new Point(0, tierHeight), new Point(width, tierHeight));
        dc.DrawLine(GridPen, new Point(0, height - 0.5), new Point(width, height - 0.5));

        void Tier(TimescaleTier tier, double y, bool centerLabels)
        {
            foreach (var boundary in tier.Boundaries(from, to))
            {
                double x = Math.Round(xOf(boundary)) + 0.5;
                double cellWidth = xOf(tier.Next(boundary)) - xOf(boundary);
                dc.DrawLine(GridPen, new Point(x, y), new Point(x, y + tierHeight));
                var label = Text(visual, tier.Label(boundary), 11);
                if (label.Width > cellWidth - 4)
                    continue;
                // Top-tier labels stick to the left edge while their period is partly scrolled off.
                double textX = centerLabels ? x + (cellWidth - label.Width) / 2 : Math.Min(Math.Max(x + 3, 3), x + cellWidth - label.Width - 3);
                dc.DrawText(label, new Point(textX, y + (tierHeight - label.Height) / 2));
            }
        }
    }

    private static Brush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(Brush(r, g, b), thickness);
        pen.Freeze();
        return pen;
    }
}
