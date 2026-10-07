using System.Windows;
using System.Windows.Media;

namespace Planora.App.Infrastructure;

/// <summary>
/// Accent themes (File > Options > Theme). A theme recolours the shared accent brushes in App.xaml, so
/// every element that uses them through DynamicResource follows without reloading.
/// </summary>
public static class Themes
{
    private sealed record Palette(Color Accent, Color AccentSoft);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["Indigo"] = new(Rgb(0x43, 0x53, 0xD1), Rgb(0xEE, 0xF0, 0xFC)),
        ["Slate Blue"] = new(Rgb(0x2F, 0x5D, 0x9E), Rgb(0xE9, 0xF0, 0xF8)),
        ["Teal"] = new(Rgb(0x1F, 0x7A, 0x7A), Rgb(0xE6, 0xF3, 0xF2)),
        ["Graphite"] = new(Rgb(0x3D, 0x46, 0x57), Rgb(0xEC, 0xEE, 0xF1)),
    };

    public static IReadOnlyList<string> Names => Palettes.Keys.ToList();

    public const string Default = "Indigo";

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>Applies a theme by name (unknown names, including the old Office-style ones, fall back to the default).</summary>
    public static void Apply(string? name)
    {
        var palette = Palettes.TryGetValue(name ?? "", out var p) ? p : Palettes[Default];
        var resources = Application.Current.Resources;
        Set(resources, "Accent", palette.Accent);
        Set(resources, "AccentLight", palette.AccentSoft);
        Set(resources, "AccentSoft", palette.AccentSoft);
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }
}
