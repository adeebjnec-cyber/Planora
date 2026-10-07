using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Planora.App.Infrastructure;

/// <summary>
/// Planora's line icons: drawn on a 24-unit grid with a consistent stroke, coloured by the inherited
/// foreground. XAML: <c>&lt;infra:LineIcon Kind="gantt" Size="16" /&gt;</c>.
/// </summary>
public sealed class LineIcon : FrameworkElement
{
    private static readonly Dictionary<string, string[]> Paths = new()
    {
        ["undo"] = new[] { "M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11" },
        ["redo"] = new[] { "m15 14 5-5-5-5", "M20 9H9.5a5.5 5.5 0 0 0 0 11H13" },
        ["schedule"] = new[] { "M21 12a9 9 0 1 1-2.64-6.36", "M21 3v6h-6" },
        ["update"] = new[] { "m9 11 3 3 8-8", "M20 12v6a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h9" },
        ["inspect"] = new[] { "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "m20 20-4-4", "M11 8v3.5", "M11 14h.01" },
        ["goto"] = new[] { "M3 12h12", "m11 7 5 5-5 5", "M20 5v14" },
        ["plus"] = new[] { "M12 5v14", "M5 12h14" },
        ["minus"] = new[] { "M5 12h14" },
        ["milestone"] = new[] { "M12 3.5 20.5 12 12 20.5 3.5 12z" },
        ["recurring"] = new[] { "m17 2 4 4-4 4", "M3 11v-1a4 4 0 0 1 4-4h14", "m7 22-4-4 4-4", "M21 13v1a4 4 0 0 1-4 4H3" },
        ["find"] = new[] { "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "m20 20-4-4" },
        ["filter"] = new[] { "M3 6h18", "M7 12h10", "M10 18h4" },
        ["props"] = new[] { "M4 7h9", "M17 7h3", "M15 4.5v5", "M4 17h3", "M11 17h9", "M9 14.5v5" },
        ["bell"] = new[] { "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9", "M10.3 21a1.94 1.94 0 0 0 3.4 0" },
        ["settings"] = new[] { "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z", "M12 2v2.5", "M12 19.5V22", "M4.93 4.93 6.7 6.7", "M17.3 17.3l1.77 1.77", "M2 12h2.5", "M19.5 12H22", "M4.93 19.07 6.7 17.3", "M17.3 6.7l1.77-1.77" },
        ["chevD"] = new[] { "m6 9 6 6 6-6" },
        ["chevR"] = new[] { "m9 6 6 6-6 6" },
        ["chevL"] = new[] { "m15 6-6 6 6 6" },
        ["check"] = new[] { "m5 12 5 5L20 7" },
        ["close"] = new[] { "M6 6l12 12", "M18 6 6 18" },
        ["gantt"] = new[] { "M3 4h10v4H3z", "M8 10h12v4H8z", "M6 16h9v4H6z" },
        ["tracking"] = new[] { "M3 5h11", "M3 8.5h8", "M9 14h12", "M9 17.5h9", "M5 21h6" },
        ["network"] = new[] { "M3 4h6v5H3z", "M15 4h6v5h-6z", "M9 15h6v5H9z", "M9 6.5h6", "M18 9v3.5h-6V15" },
        ["calendar"] = new[] { "M5 5h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z", "M4 10h16", "M8 3v4", "M16 3v4" },
        ["kanban"] = new[] { "M4 4h4v16H4z", "M10 4h4v10h-4z", "M16 4h4v7h-4z" },
        ["timeline"] = new[] { "M2 12h20", "M6 7h6", "M6 7v5", "M12 17h7", "M19 17v-5" },
        ["taskusage"] = new[] { "M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z", "M4 9.5h16", "M4 15h16", "M10 4v16" },
        ["tasksheet"] = new[] { "M8 6h13", "M8 12h13", "M8 18h13", "M3 6h.01", "M3 12h.01", "M3 18h.01" },
        ["resources"] = new[] { "M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1", "M16 3.5a4 4 0 0 1 0 7.5", "M22 21v-1a6 6 0 0 0-4-5.6" },
        ["resusage"] = new[] { "M3 20h18", "M6 16v-5", "M11 16V7", "M16 16v-8", "M20 16v-3" },
        ["resgraph"] = new[] { "M3 3v18h18", "M7 15l4-5 4 3 5-7" },
        ["team"] = new[] { "M3 5h18", "M3 12h18", "M3 19h18", "M7 3v4", "M12 10v4", "M16 17v4" },
        ["relations"] = new[] { "M5 12h4", "M15 12h4", "M9 9h6v6H9z", "M2 10h3v4H2z", "M19 10h3v4h-3z" },
        ["building"] = new[] { "M4 21V5a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v16", "M15 9h4a1 1 0 0 1 1 1v11", "M2 21h20", "M8 8h3", "M8 12h3", "M8 16h3" },
        ["auto"] = new[] { "M13 2 4 14h7l-1 8 9-12h-7z" },
        ["pin"] = new[] { "M12 17v5", "M8 3h8", "M9.5 3v6L6 13v2h12v-2l-3.5-4V3" },
        ["file"] = new[] { "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z", "M14 3v5h5" },
        ["save"] = new[] { "M5 3h11l5 5v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M7 3v5h8", "M7 21v-7h10v7" },
        ["info"] = new[] { "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z", "M12 16v-4", "M12 8h.01" },
        ["link"] = new[] { "M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7", "M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7" },
        ["critical"] = new[] { "M3 17l6-6 4 4 8-8", "M15 7h6v6" },
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(LineIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(LineIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(LineIcon),
        new FrameworkPropertyMetadata(1.75, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(LineIcon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public LineIcon()
    {
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static bool Exists(string kind) => Paths.ContainsKey(kind);

    /// <summary>The icon's outline on the 24-unit grid (cached).</summary>
    public static Geometry? GeometryOf(string kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
            return cached;
        if (!Paths.TryGetValue(kind, out var paths))
            return null;
        var group = new GeometryGroup();
        foreach (var data in paths)
            group.Children.Add(Geometry.Parse(data));
        group.Freeze();
        return Cache[kind] = group;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (GeometryOf(Kind) is not { } geometry)
            return;
        double scale = Size / 24;
        var pen = new Pen(Foreground, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    /// <summary>Draws an icon into custom-drawn views at a position and size.</summary>
    public static void Draw(DrawingContext dc, string kind, Point topLeft, double size, Brush brush, double strokeWidth = 1.75)
    {
        if (GeometryOf(kind) is not { } geometry)
            return;
        var pen = new Pen(brush, strokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new MatrixTransform(size / 24, 0, 0, size / 24, topLeft.X, topLeft.Y));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}

/// <summary>Planora's typeface (Inter, bundled), with Segoe UI as the fallback for characters Inter lacks.</summary>
public static class UiFont
{
    public static readonly FontFamily Family = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter, Segoe UI");

    public static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface Medium = new(Family, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    public static readonly Typeface SemiBold = new(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static Typeface Of(bool bold, bool italic = false) =>
        new(Family, italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
}
