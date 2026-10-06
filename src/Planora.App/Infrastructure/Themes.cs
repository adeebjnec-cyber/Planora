using System.Windows;
using System.Windows.Media;

namespace Planora.App.Infrastructure;

/// <summary>
/// Office-style themes (File > Options > Theme). A theme recolours the shared brushes in App.xaml, so
/// every window that uses them follows without reloading.
/// </summary>
public static class Themes
{
    private sealed record Palette(Color Chrome, Color ChromeBorder, Color Accent, Color AccentLight, Color ViewBar);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["Project Green"] = new(Rgb(0xF3, 0xF3, 0xF3), Rgb(0xD4, 0xD4, 0xD4), Rgb(0x31, 0x75, 0x2F), Rgb(0xDF, 0xEE, 0xDC), Rgb(0x31, 0x75, 0x2F)),
        ["Blue"] = new(Rgb(0xF3, 0xF3, 0xF3), Rgb(0xD4, 0xD4, 0xD4), Rgb(0x2B, 0x57, 0x9A), Rgb(0xDC, 0xE9, 0xF7), Rgb(0x2B, 0x57, 0x9A)),
        ["White"] = new(Rgb(0xFF, 0xFF, 0xFF), Rgb(0xE1, 0xE1, 0xE1), Rgb(0x2B, 0x57, 0x9A), Rgb(0xE8, 0xEF, 0xF8), Rgb(0x50, 0x50, 0x50)),
        ["Light Gray"] = new(Rgb(0xE6, 0xE6, 0xE6), Rgb(0xC8, 0xC8, 0xC8), Rgb(0x44, 0x44, 0x44), Rgb(0xDD, 0xDD, 0xDD), Rgb(0x44, 0x44, 0x44)),
    };

    public static IReadOnlyList<string> Names => Palettes.Keys.ToList();

    public const string Default = "Blue";

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>Applies a theme by name (unknown names fall back to the default).</summary>
    public static void Apply(string? name)
    {
        var palette = Palettes.TryGetValue(name ?? "", out var p) ? p : Palettes[Default];
        var resources = Application.Current.Resources;
        Set(resources, "ChromeBackground", palette.Chrome);
        Set(resources, "ChromeBorder", palette.ChromeBorder);
        Set(resources, "Accent", palette.Accent);
        Set(resources, "AccentLight", palette.AccentLight);
        Set(resources, "ViewBarBackground", palette.ViewBar);
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }
}
