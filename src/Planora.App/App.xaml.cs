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

        // Developer aid: "--screenshot out.png [file]" renders the window to an image and exits.
        int shot = Array.IndexOf(e.Args, "--screenshot");
        if (shot >= 0 && shot + 1 < e.Args.Length)
        {
            var output = e.Args[shot + 1];
            var file = e.Args.Where((_, i) => i != shot && i != shot + 1).FirstOrDefault();
            window.Show();
            if (file is not null)
                window.ViewModel.OpenFile(file);
            else
                window.ViewModel.LoadSample();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
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

    /// <summary>Last-resort handler: report the problem instead of closing and losing work.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, $"Something went wrong: {e.Exception.Message}\n\nYour work has not been lost; please save it.",
            "Planora", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
