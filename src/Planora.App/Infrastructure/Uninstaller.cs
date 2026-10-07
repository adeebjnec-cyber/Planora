using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Planora.App.Infrastructure;

/// <summary>
/// "Planora.exe --uninstall", run from Windows' Installed apps list: removes the shortcuts, the .planora file
/// association and the uninstall entry that Planora Setup created, then deletes the program folder after
/// Planora exits. Only a folder that Setup marked as its own is ever deleted.
/// </summary>
public static class Uninstaller
{
    public const string MarkerFile = "planora.install";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Planora";

    public static void Run(bool quiet)
    {
        string folder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        bool installed = File.Exists(Path.Combine(folder, MarkerFile));
        if (!quiet && MessageBox.Show(
                installed
                    ? "Remove Planora from this computer?\n\nYour project files are not removed."
                    : "This copy of Planora was not installed by Planora Setup, so there is nothing to uninstall.",
                "Uninstall Planora", installed ? MessageBoxButton.OKCancel : MessageBoxButton.OK,
                installed ? MessageBoxImage.Question : MessageBoxImage.Information) != MessageBoxResult.OK || !installed)
            return;

        foreach (var shortcut in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Planora.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Planora.lnk"),
        })
        {
            TryDelete(shortcut);
        }

        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
        {
            if (classes?.OpenSubKey(".planora")?.GetValue("") as string == "Planora.Project")
                classes.DeleteSubKeyTree(".planora", throwOnMissingSubKey: false);
            classes?.DeleteSubKeyTree("Planora.Project", throwOnMissingSubKey: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        // The folder holds this running program, so a short-lived helper removes it once Planora has exited.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{folder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        if (!quiet)
            MessageBox.Show("Planora has been removed.", "Uninstall Planora", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A shortcut the user moved or locked is left alone.
        }
    }
}
