using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace Planora.App.Infrastructure;

/// <summary>
/// XAML: <c>{infra:Glyph &amp;#xE710;}</c> turns a Segoe MDL2 Assets icon into a vector image, for controls
/// that need an <see cref="ImageSource"/> (ribbon buttons).
/// </summary>
[MarkupExtensionReturnType(typeof(ImageSource))]
public sealed class GlyphExtension : MarkupExtension
{
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x2B, 0x57, 0x9A));
    private static readonly Dictionary<string, ImageSource> Cache = new();

    public GlyphExtension(string glyph) => Glyph = glyph;

    public string Glyph { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Create(Glyph);

    public static ImageSource Create(string glyph)
    {
        if (Cache.TryGetValue(glyph, out var cached))
            return cached;
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe MDL2 Assets"), 32, Accent, 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        // A transparent square keeps every icon the same size and centred.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        group.Children.Add(new GeometryDrawing(Accent, null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        Cache[glyph] = image;
        return image;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
