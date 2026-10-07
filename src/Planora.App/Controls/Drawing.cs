using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Planora.App.Infrastructure;

namespace Planora.App.Controls;

/// <summary>Shared colours, pens and text for the custom-drawn views (Planora's palette).</summary>
public static class Drawing
{
    public static readonly Brush Background = Brush(0xFF, 0xFF, 0xFF);
    public static readonly Brush HeaderBackground = Brush(0xFA, 0xFA, 0xFB);
    public static readonly Brush NonWorking = Brush(0xF8, 0xF9, 0xFA);
    public static readonly Brush SelectedRow = Brush(0xEE, 0xF0, 0xFD);
    public static readonly Brush HoverRow = Brush(0xF6, 0xF7, 0xF9);
    public static readonly Brush TaskFill = Brush(0x8F, 0x9A, 0xEA);
    public static readonly Brush CriticalFill = Brush(0xE2, 0xA3, 0x9C);
    public static readonly Brush ManualFill = Brush(0x9F, 0xD0, 0xD6);
    public static readonly Brush ProgressFill = Brush(0x41, 0x52, 0xCC);
    public static readonly Brush SummaryFill = Brush(0x2A, 0x32, 0x42);
    public static readonly Brush DoneFill = Brush(0xB9, 0xDD, 0xC9);
    public static readonly Brush OverallocatedFill = Brush(0xF6, 0xD9, 0xD5);
    public static readonly Brush TextBrush = Brush(0x14, 0x1A, 0x26);
    public static readonly Brush SecondaryText = Brush(0x54, 0x5D, 0x6C);
    public static readonly Brush MutedText = Brush(0x8A, 0x93, 0xA2);
    public static readonly Brush FaintText = Brush(0xB3, 0xBA, 0xC6);
    public static readonly Brush AlertText = Brush(0xBC, 0x4B, 0x42);
    public static readonly Brush Accent = Brush(0x43, 0x53, 0xD1);
    public static readonly Brush Navy = Brush(0x1B, 0x23, 0x33);
    public static readonly Pen TaskBorder = Pen(0x6E, 0x7B, 0xE0, 1);
    public static readonly Pen CriticalBorder = Pen(0xBC, 0x4B, 0x42, 1);
    public static readonly Pen SelectedBorder = Pen(0x43, 0x53, 0xD1, 2);
    public static readonly Pen GridPen = Pen(0xE4, 0xE6, 0xEB, 1);
    public static readonly Pen LightGridPen = Pen(0xEE, 0xF0, 0xF3, 1);
    public static readonly Pen RowLinePen = Pen(0xF2, 0xF3, 0xF6, 1);
    public static readonly Pen LinkPen = Pen(0xA3, 0xAB, 0xB8, 1.1);
    public static readonly Pen CriticalLinkPen = Pen(0xD3, 0x95, 0x8E, 1.1);
    public static readonly Pen TodayPen = Pen(0x43, 0x53, 0xD1, 1.5);
    public static readonly Typeface Font = UiFont.Regular;
    public static readonly Typeface BoldFont = UiFont.SemiBold;

    public static FormattedText Text(Visual visual, string text, double size, Brush? brush = null, bool bold = false, double maxWidth = 0) =>
        Text(visual, text, size, bold ? BoldFont : Font, brush, maxWidth);

    public static FormattedText Text(Visual visual, string text, double size, Typeface typeface, Brush? brush = null, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush ?? TextBrush, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
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
            g.LineTo(tip - direction * 6 + normal * 3.2, true, false);
            g.LineTo(tip - direction * 6 - normal * 3.2, true, false);
        }
        head.Freeze();
        dc.DrawGeometry(brush, null, head);
    }

    public static void Diamond(DrawingContext dc, Point center, double size, Brush brush, Pen? outline = null)
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
        dc.DrawGeometry(brush, outline, diamond);
    }

    /// <summary>A polyline with softly rounded corners (dependency lines).</summary>
    public static Geometry RoundedPolyline(IReadOnlyList<Point> points, double radius)
    {
        var pts = new List<Point>();
        foreach (var p in points)
        {
            if (pts.Count == 0 || (pts[^1] - p).Length > 0.01)
                pts.Add(p);
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector a = pts[i] - pts[i - 1], b = pts[i + 1] - pts[i];
                double r = Math.Min(radius, Math.Min(a.Length / 2, b.Length / 2));
                a.Normalize();
                b.Normalize();
                g.LineTo(pts[i] - a * r, true, true);
                g.QuadraticBezierTo(pts[i], pts[i] + b * r, true, true);
            }
            if (pts.Count > 1)
                g.LineTo(pts[^1], true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Draws the timescale header (automatic tiers, or one to three fixed tiers from the project's timescale
    /// settings) used by the Gantt chart and the usage views. <paramref name="xOf"/> maps a date to an x position.
    /// When <paramref name="today"/> is given, its day is marked in the bottom tier.
    /// </summary>
    public static void TimescaleHeader(DrawingContext dc, Visual visual, Func<DateTime, double> xOf,
        DateTime from, DateTime to, double pixelsPerDay, double width, double height, Engine.Views.TimescaleSettings? settings = null,
        DateTime? today = null, Func<DateTime, bool>? isNonWorking = null)
    {
        var tiers = Timescale.Tiers(pixelsPerDay, settings);
        double tierHeight = height / tiers.Count;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        for (int i = 0; i < tiers.Count; i++)
        {
            bool top = i == 0 && tiers.Count > 1, bottom = i == tiers.Count - 1 && tiers.Count > 1;
            Tier(tiers[i], i * tierHeight, top, bottom);
            if (i > 0)
                dc.DrawLine(LightGridPen, new Point(0, Math.Round(i * tierHeight) + 0.5), new Point(width, Math.Round(i * tierHeight) + 0.5));
        }
        dc.DrawLine(GridPen, new Point(0, height - 0.5), new Point(width, height - 0.5));

        void Tier(TimescaleTier tier, double y, bool top, bool bottom)
        {
            var typeface = top ? UiFont.SemiBold : bottom ? UiFont.Regular : UiFont.Medium;
            double size = top ? 12 : bottom ? 11 : 11.5;
            foreach (var boundary in tier.Boundaries(from, to))
            {
                double x = Math.Round(xOf(boundary)) + 0.5;
                double cellWidth = xOf(tier.Next(boundary)) - xOf(boundary);
                bool dayCell = bottom && tier.Unit == TimeUnit.Day;
                bool weekend = dayCell && isNonWorking?.Invoke(boundary) == true;
                if (weekend)
                    dc.DrawRectangle(NonWorking, null, new Rect(x, y, cellWidth, tierHeight));
                if (!dayCell)
                    dc.DrawLine(LightGridPen, new Point(x, y), new Point(x, y + tierHeight));
                bool isToday = dayCell && today is { } t && boundary.Date == t.Date;
                var brush = isToday ? Brushes.White : weekend ? FaintText : top ? TextBrush : SecondaryText;
                var label = Text(visual, tier.Label(boundary), size, isToday ? UiFont.SemiBold : typeface, brush);
                if (label.Width > cellWidth - 4)
                {
                    // Keep only the first part of a long week label ("Week 41") when its dates do not fit.
                    var text = tier.Label(boundary);
                    int cut = text.IndexOf("   ", StringComparison.Ordinal);
                    if (cut <= 0)
                        continue;
                    label = Text(visual, text[..cut], size, typeface, brush);
                    if (label.Width > cellWidth - 4)
                        continue;
                }
                double textX = dayCell ? x + (cellWidth - label.Width) / 2 : Math.Min(Math.Max(x + 8, 8), x + cellWidth - label.Width - 4);
                double textY = y + (tierHeight - label.Height) / 2;
                if (isToday)
                {
                    var pill = new Rect(textX - 4, textY, label.Width + 8, label.Height);
                    dc.DrawRoundedRectangle(Accent, null, pill, 4, 4);
                }
                dc.DrawText(label, new Point(textX, textY));
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
