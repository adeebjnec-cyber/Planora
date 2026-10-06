using System.IO;
using System.Text.Json;

namespace Planora.App.Infrastructure;

/// <summary>Per-user application settings, stored in %APPDATA%\Planora\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Planora", "settings.json");

    public int AutosaveMinutes { get; set; } = 2;

    /// <summary>Recently opened or saved files, newest first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Office theme name (see <see cref="Themes"/>).</summary>
    public string Theme { get; set; } = Themes.Default;

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them is not worth interrupting the user.
        }
    }
}
