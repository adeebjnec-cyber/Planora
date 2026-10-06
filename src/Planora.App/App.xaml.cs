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

        // Developer aid: "--screenshot out.png [--view Kind] [--dialog Name] [--select row] [--details 1] [file]" renders the window
        // (or a dialog) to an image and exits.
        var options = ParseOptions(e.Args, out var positional);

        // Developer aid: "--export out.pdf|out.png [--report Name] [file]" writes output without dialogs.
        if (options.TryGetValue("--export", out var exportPath))
        {
            window.ViewModel.IsHeadless = true;
            window.Show();
            if (positional.FirstOrDefault() is { } source)
                window.ViewModel.OpenFile(source);
            else
                window.ViewModel.LoadSample();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try
                {
                    var vm = window.ViewModel;
                    if (options.TryGetValue("--report", out var report))
                    {
                        var doc = Output.ReportDocument.Build(Engine.Reports.ReportCatalog.Build(vm.Project, report));
                        var pages = Output.PageOutput.Paginate(doc, new Size(793.7, 1122.5));
                        if (exportPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            // One image per page: out.png, out-2.png, ...
                            for (int i = 0; i < pages.Pages.Count; i++)
                                Output.PageOutput.SavePng(pages.Pages[i], pages.PageSize, i == 0 ? exportPath : exportPath[..^4] + $"-{i + 1}.png", 96);
                        }
                        else
                        {
                            Output.PageOutput.SavePdf(pages, exportPath, report);
                        }
                    }
                    else if (exportPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        var (visual, size) = Output.GanttPages.BuildImage(vm);
                        Output.PageOutput.SavePng(visual, size, exportPath);
                    }
                    else
                    {
                        Output.PageOutput.SavePdf(Output.GanttPages.Build(vm, Output.GanttPages.PageSize(vm)), exportPath, vm.Project.Name);
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(exportPath + ".error.txt", ex.ToString());
                }
                window.ViewModel.Document.DeleteAutosave();
                Shutdown();
            });
            return;
        }
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
            window.ViewModel.IsHeadless = true;
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
            if (options.ContainsKey("--progress"))
                window.ViewModel.ShowProgressLines = true;
            if (options.TryGetValue("--group", out var group))
                window.ViewModel.CurrentGroupName = group;
            if (options.TryGetValue("--select", out var select) && int.TryParse(select, out var row) && row >= 1 && row <= window.ViewModel.Project.Tasks.Count)
                window.ViewModel.SelectTask(window.ViewModel.Project.Tasks[row - 1]);
            if (options.TryGetValue("--theme", out var theme))
                window.ViewModel.Theme = theme;
            if (options.TryGetValue("--backstage", out var backstagePage))
                window.ShowBackstage(backstagePage is "1" or "" ? "Info" : backstagePage);
            if (options.ContainsKey("--timeline"))
                window.ViewModel.ShowTimelinePane = true;
            if (options.TryGetValue("--selectresource", out var resRow) && int.TryParse(resRow, out var ri) && ri >= 1 && ri <= window.ViewModel.Project.Resources.Count)
                window.ViewModel.SetResourceSelection(new[] { window.ViewModel.Project.Resources[ri - 1] });
            if (options.ContainsKey("--details"))
                window.ViewModel.ShowTaskForm = true;
            if (options.TryGetValue("--usagedetails", out var usage) && int.TryParse(usage, out var flags))
                window.ViewModel.UsageDetails = (ViewModels.UsageDetail)flags;
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
        else
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, window.ViewModel.OfferRecovery);
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
