using System.IO;

namespace Planora.App.ViewModels;

/// <summary>Recent files (File > Open), kept in the user's settings.</summary>
public sealed partial class MainViewModel
{
    private const int MaxRecentFiles = 12;

    /// <summary>True in the unattended screenshot and export modes, which must not touch the user's settings.</summary>
    public bool IsHeadless { get; set; }

    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;

    /// <summary>The Office theme, applied at once and remembered.</summary>
    public string Theme
    {
        get => _settings.Theme;
        set
        {
            _settings.Theme = value;
            Infrastructure.Themes.Apply(value);
            if (!IsHeadless)
                _settings.Save();
        }
    }

    internal void AddRecentFile(string path)
    {
        if (IsHeadless)
            return;
        var full = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, full);
        if (_settings.RecentFiles.Count > MaxRecentFiles)
            _settings.RecentFiles.RemoveRange(MaxRecentFiles, _settings.RecentFiles.Count - MaxRecentFiles);
        _settings.Save();
    }

    public void OpenRecent(string path)
    {
        if (!ConfirmDiscard())
            return;
        if (IsSpreadsheet(path))
            ImportSpreadsheet(path);
        else
            OpenFile(path);
    }
}
