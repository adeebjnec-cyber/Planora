using System.Windows;
using System.Windows.Threading;

namespace Planora.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        MainWindow = window;

        // Developer aid: "--screenshot out.png [--view Kind] [--dialog Name] [file]" renders the window
        // (or a dialog) to an image and exits.
        var options = ParseOptions(e.Args, out var positional);
        if (options.TryGetValue("--screenshot", out var output))
        {
            // Never block on a message box in this unattended mode: log the error and exit.
            DispatcherUnhandledException -= OnUnhandledException;
            DispatcherUnhandledException += (_, args) =>
            {
                System.IO.File.WriteAllText(output + ".error.txt", args.Exception.ToString());
                args.Handled = true;
                Shutdown(1);
            };
            window.Show();
            if (positional.FirstOrDefault() is { } file)
                window.ViewModel.OpenFile(file);
            else
                window.ViewModel.LoadSample();
            if (options.TryGetValue("--view", out var view))
                window.ViewModel.CurrentView = Enum.Parse<ViewModels.ViewKind>(view, ignoreCase: true);
            if (options.TryGetValue("--table", out var table))
                window.ViewModel.CurrentTableName = table;
            if (options.TryGetValue("--filter", out var filter))
                window.ViewModel.CurrentFilterName = filter;
            if (options.TryGetValue("--group", out var group))
                window.ViewModel.CurrentGroupName = group;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (options.TryGetValue("--dialog", out var dialogName))
                {
                    var dialog = Dialogs.DialogFactory.ForScreenshot(window.ViewModel, dialogName);
                    dialog.Owner = window;
                    dialog.ContentRendered += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        Dialogs.DialogFactory.SaveScreenshot(dialog, output);
                        window.ViewModel.Document.DeleteAutosave();
                        Shutdown();
                    });
                    dialog.Show();
                    return;
                }
                window.SaveScreenshot(output);
                window.ViewModel.Document.DeleteAutosave();
                Shutdown();
            });
            return;
        }

        window.Show();
        if (e.Args.Length > 0)
            window.ViewModel.OpenFile(e.Args[0]);
    }

    /// <summary>Splits "--name value" pairs from positional arguments.</summary>
    private static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
                options[args[i]] = args[++i];
            else
                positional.Add(args[i]);
        }
        return options;
    }

    /// <summary>Last-resort handler: report the problem instead of closing and losing work.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, $"Something went wrong: {e.Exception.Message}\n\nYour work has not been lost; please save it.",
            "Planora", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
