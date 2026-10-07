using System.Windows;
using System.Windows.Media;

namespace Planora.App.Controls;

/// <summary>
/// The task sheet's Status mark: a green tick when complete, a filling ring while in progress (amber when
/// behind schedule), and a dashed ring when not started.
/// </summary>
public sealed class StatusGlyph : FrameworkElement
{
    private static readonly Brush Done = Frozen(0x3E, 0x8E, 0x68);
    private static readonly Brush Progress = Frozen(0x43, 0x53, 0xD1);
    private static readonly Brush Late = Frozen(0xB7, 0x83, 0x2F);
    private static readonly Pen TodoPen = DashedPen();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(StatusGlyph),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(double), typeof(StatusGlyph),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(15, 15);

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(7.5, 7.5);
        switch (Kind)
        {
            case "done":
                dc.DrawEllipse(Done, null, c, 6.6, 6.6);
                var tick = new StreamGeometry();
                using (var g = tick.Open())
                {
                    g.BeginFigure(new Point(4.8, 7.7), false, false);
                    g.LineTo(new Point(6.6, 9.5), true, true);
                    g.LineTo(new Point(10.2, 5.7), true, true);
                }
                tick.Freeze();
                dc.DrawGeometry(null, new Pen(Brushes.White, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, tick);
                break;
            case "progress":
            case "late":
                var brush = Kind == "late" ? Late : Progress;
                dc.DrawEllipse(null, new Pen(brush, 1.4), c, 6.3, 6.3);
                double angle = Math.Clamp(Percent / 100, 0.05, 0.999) * 2 * Math.PI;
                var end = new Point(c.X + 4 * Math.Sin(angle), c.Y - 4 * Math.Cos(angle));
                var pie = new StreamGeometry();
                using (var g = pie.Open())
                {
                    g.BeginFigure(c, true, true);
                    g.LineTo(new Point(c.X, c.Y - 4), false, false);
                    g.ArcTo(end, new Size(4, 4), 0, angle > Math.PI, SweepDirection.Clockwise, false, false);
                }
                pie.Freeze();
                dc.DrawGeometry(brush, null, pie);
                break;
            case "todo":
                dc.DrawEllipse(null, TodoPen, c, 6, 6);
                break;
        }
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen DashedPen()
    {
        var pen = new Pen(Frozen(0xB3, 0xBA, 0xC6), 1.4) { DashStyle = new DashStyle(new[] { 1.7, 1.5 }, 0) };
        pen.Freeze();
        return pen;
    }
}
