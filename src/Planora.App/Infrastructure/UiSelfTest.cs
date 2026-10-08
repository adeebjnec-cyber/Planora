using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Planora.App.ViewModels;

namespace Planora.App.Infrastructure;

/// <summary>
/// Developer aid: "Planora.exe --uitest log.txt [files...]" drives the real window through every menu command,
/// toolbar toggle, view, table, filter and group, on the sample project and on each file given, then writes a log
/// of anything that went wrong. A watchdog closes every dialog and message box a command opens (recording the
/// message text), so the run never waits for a person.
/// </summary>
public sealed class UiSelfTest
{
    private readonly MainWindow _window;
    private readonly string _logPath;
    private readonly List<string> _files;
    private readonly StringBuilder _log = new();
    private readonly List<string> _problems = new();
    private readonly DispatcherTimer _watchdog;
    private readonly Dictionary<IntPtr, DateTime> _seen = new();
    private string _step = "";
    private int _steps;

    /// <summary>Commands that would leave the test (exit) or touch files outside the test.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "Open Hyperlink", "Print" };

    public UiSelfTest(MainWindow window, string logPath, IEnumerable<string> files)
    {
        _window = window;
        _logPath = logPath;
        _files = files.ToList();
        _watchdog = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        _watchdog.Tick += (_, _) => CloseDialogs();
    }

    public void Run()
    {
        Application.Current.DispatcherUnhandledException += (_, e) =>
        {
            Problem($"UNHANDLED {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.Handled = true;
        };
        _watchdog.Start();
        var total = Stopwatch.StartNew();
        try
        {
            var vm = _window.ViewModel;
            vm.LoadSample();
            Settle();
            Pass("sample project");
            for (int i = 0; i < _files.Count; i++)
            {
                Step($"open {Path.GetFileName(_files[i])}", () => vm.OpenFile(_files[i]));
                RoundTrip(_files[i]);
                Pass(Path.GetFileName(_files[i]));
            }
            Step("open sample again", () => { vm.Document.ForgetChanges(); vm.LoadSample(); });
            Collapse();
            Pass("sample (second pass, after commands)");
            Step("new blank project", () => { vm.Document.ForgetChanges(); vm.NewCommand.Execute(null); });
            Pass("blank project");
        }
        catch (Exception ex)
        {
            Problem($"TEST HARNESS STOPPED at '{_step}': {ex}");
        }
        _watchdog.Stop();
        _log.Insert(0, $"Planora UI self-test: {_steps} steps, {_problems.Count} problem(s), {total.Elapsed.TotalSeconds:0} s\n" +
                       (_problems.Count == 0 ? "" : "PROBLEMS:\n  " + string.Join("\n  ", _problems) + "\n") + "\nLOG:\n");
        File.WriteAllText(_logPath, _log.ToString());
        _window.ViewModel.Document.DeleteAutosave();
        _window.ViewModel.Document.ForgetChanges();
        Application.Current.Shutdown();
    }

    /// <summary>One full pass over the current project: views, tables, filters, groups, every menu command, undo.</summary>
    private void Pass(string label)
    {
        var vm = _window.ViewModel;
        _log.AppendLine($"== {label}: {vm.Project.Tasks.Count} tasks, {vm.Project.Resources.Count} resources");
        SelectRow(2);
        foreach (var view in vm.Views)
            Step($"view {view.Title}", () => vm.CurrentView = view.Kind);
        Step("back to Gantt", () => vm.CurrentView = ViewKind.Gantt);
        foreach (var name in vm.TableNames.ToList())
            Step($"table {name}", () => vm.CurrentTableName = name);
        Step("table Entry", () => vm.CurrentTableName = vm.TableNames[0]);
        foreach (var name in vm.FilterNames.ToList().Where(n => !n.StartsWith("Using Resource", StringComparison.Ordinal)))
            Step($"filter {name}", () => vm.CurrentFilterName = name);
        Step("filter All Tasks", () => vm.CurrentFilterName = vm.FilterNames[0]);
        foreach (var name in vm.GroupNames.ToList())
            Step($"group {name}", () => vm.CurrentGroupName = name);
        Step("group none", () => vm.CurrentGroupName = vm.GroupNames[0]);
        for (int z = 0; z < MainViewModel.ZoomLevels.Length; z++)
        {
            int zoom = z;
            Step($"zoom {zoom}", () => vm.ZoomIndex = zoom);
        }
        Step("zoom days", () => vm.ZoomIndex = 4);

        foreach (var (path, item) in MenuItems(_window.MainMenu.Items, ""))
        {
            if (Skipped.Contains(Label(item)))
                continue;
            if (Environment.GetEnvironmentVariable("PLANORA_UITEST_SKIP") is { Length: > 0 } skip && skip.Split('|').Any(o => path.Contains(o, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (Environment.GetEnvironmentVariable("PLANORA_UITEST_ONLY") is { Length: > 0 } only && !only.Split('|').Any(o => path.Contains(o, StringComparison.OrdinalIgnoreCase)))
                continue;
            SelectRow(2);
            int before = vm.Project.Tasks.Count;
            Step($"menu {path}", () => Invoke(item));
            if (vm.Project.Tasks.Count != before && vm.UndoCommand.CanExecute(null))
                Step($"undo after {path}", () => vm.UndoCommand.Execute(null));
        }
        Step("command search", () => _window.ShowForCapture("palette"));
        Step("collapse all", () => vm.CollapseAllCommand.Execute(null));
        Step("expand all", () => vm.ExpandAllCommand.Execute(null));
        Step("critical path on", () => vm.ShowCriticalPath = true);
        Step("critical path off", () => vm.ShowCriticalPath = false);
        for (int i = 0; i < 20 && vm.UndoCommand.CanExecute(null); i++)
            Step("undo", () => vm.UndoCommand.Execute(null));
        for (int i = 0; i < 20 && vm.RedoCommand.CanExecute(null); i++)
            Step("redo", () => vm.RedoCommand.Execute(null));
        Step("recalculate", () => vm.CalculateCommand.Execute(null));
        CheckSchedule(label);
    }

    private void Collapse()
    {
        var vm = _window.ViewModel;
        var summary = vm.Project.Tasks.FirstOrDefault(t => t.IsSummary);
        if (summary is null)
            return;
        Step("collapse a phase", () => vm.ToggleCollapsed(summary));
        Step("expand the phase", () => vm.ToggleCollapsed(vm.Project.GetRequiredTask(summary.Id)));
    }

    /// <summary>Saves the open project as .planora and Microsoft Project XML, reopens both and compares the dates.</summary>
    private void RoundTrip(string source)
    {
        var vm = _window.ViewModel;
        var folder = Path.Combine(Path.GetTempPath(), "planora-uitest");
        Directory.CreateDirectory(folder);
        var expected = vm.Project.Tasks.Select(t => (t.Name, t.Start, t.Finish)).ToList();
        foreach (var ext in new[] { ".planora", ".xml" })
        {
            var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + ext);
            Step($"save {ext}", () =>
            {
                if (ext == ".planora")
                    Engine.Persistence.ProjectFile.Save(vm.Project, target);
                else
                    Engine.Interop.MspdiFile.Save(vm.Project, target);
            });
            Step($"reopen {ext}", () => { vm.Document.ForgetChanges(); vm.OpenFile(target); });
            var actual = vm.Project.Tasks.Select(t => (t.Name, t.Start, t.Finish)).ToList();
            int differ = expected.Zip(actual).Count(p => p.First != p.Second);
            if (actual.Count != expected.Count || differ > 0)
                Problem($"round trip {ext} of {Path.GetFileName(source)}: {expected.Count} tasks before, {actual.Count} after, {differ} with different name/dates");
            else
                _log.AppendLine($"   round trip {ext}: {actual.Count} tasks identical");
        }
    }

    private void CheckSchedule(string label)
    {
        var p = _window.ViewModel.Project;
        if (_window.ViewModel.Document.ScheduleError is { } error)
            Problem($"{label}: schedule error '{error}'");
        foreach (var t in p.Tasks.Where(t => !t.IsSummary && t.Finish < t.Start))
            Problem($"{label}: task {t.Id} '{t.Name}' finishes before it starts");
    }

    // ------------------------------------------------------------------ driving

    private void SelectRow(int row)
    {
        var vm = _window.ViewModel;
        if (vm.Project.Tasks.Count > row)
            vm.SelectTask(vm.Project.Tasks[row]);
    }

    private static string Label(MenuItem item) => (item.Header as string ?? "").Replace("...", "").Replace("_", "");

    private static IEnumerable<(string Path, MenuItem Item)> MenuItems(ItemCollection items, string path)
    {
        foreach (var item in items.OfType<MenuItem>().ToList())
        {
            var label = Label(item);
            var here = path.Length == 0 ? label : path + " › " + label;
            if (item.ItemsSource is null && item.Items.Count > 0)
            {
                foreach (var child in MenuItems(item.Items, here))
                    yield return child;
            }
            else if (item.ItemsSource is null)
            {
                yield return (here, item);
            }
        }
    }

    private static void Invoke(MenuItem item)
    {
        if (item.Command is { } command)
        {
            if (command.CanExecute(item.CommandParameter))
                command.Execute(item.CommandParameter);
        }
        else if (item.IsCheckable)
        {
            item.IsChecked = !item.IsChecked;
            item.IsChecked = !item.IsChecked;
        }
        else
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        }
    }

    private void Step(string name, Action action)
    {
        _step = name;
        _steps++;
        File.AppendAllText(_logPath + ".progress", $"{DateTime.Now:HH:mm:ss.fff} {name}{Environment.NewLine}");
        var watch = Stopwatch.StartNew();
        try
        {
            action();
            Settle();
            // Close what the command left open (report windows and the like) before the next command, as a person would.
            foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w != _window && w.IsVisible).ToList())
            {
                _log.AppendLine($"           window '{window.Title}' ({window.GetType().Name}) opened and closed");
                if (window is Dialogs.CommandPalette palette)
                    palette.KeepOpen = false;
                window.Close();
            }
            Settle();
        }
        catch (Exception ex)
        {
            Problem($"{name}: {ex.GetType().Name}: {ex.Message}\n      {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        }
        if (watch.ElapsedMilliseconds > 3000)
            Problem($"{name}: slow ({watch.ElapsedMilliseconds} ms)");
        _log.AppendLine($"   {watch.ElapsedMilliseconds,5} ms  {name}");
    }

    /// <summary>Lets the window lay out and render, so drawing errors surface inside the step that caused them.</summary>
    private void Settle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        _window.UpdateLayout();
    }

    private void Problem(string text)
    {
        _problems.Add(text);
        _log.AppendLine("   !! " + text);
    }

    // ------------------------------------------------------------------ watchdog

    /// <summary>Closes dialogs that commands open: WPF windows after they have rendered, and Windows message and file boxes.</summary>
    private void CloseDialogs()
    {
        var now = DateTime.Now;
        // While a Windows dialog (message box, file dialog) is open, its owner window is locked, as it is for a
        // person: close the dialog first, never the window underneath it.
        bool systemDialogOpen = false;
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            systemDialogOpen |= cls.ToString() == "#32770" && IsWindowVisible(hwnd);
            return true;
        }, IntPtr.Zero);
        foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
        {
            if (systemDialogOpen || window == _window || !window.IsVisible)
                continue;
            var handle = new WindowInteropHelper(window).Handle;
            if (!_seen.TryGetValue(handle, out var first))
            {
                _seen[handle] = now;
                continue;
            }
            if ((now - first).TotalMilliseconds < 250)
                continue;
            _log.AppendLine($"           dialog '{window.Title}' ({window.GetType().Name}) opened and closed");
            try
            {
                if (window is Dialogs.CommandPalette palette)
                    palette.KeepOpen = false;
                window.Close();
            }
            catch (Exception ex)
            {
                Problem($"{_step}: closing '{window.Title}' failed: {ex.Message}");
            }
        }

        uint thread = GetCurrentThreadId();
        EnumThreadWindows(thread, (hwnd, _) =>
        {
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "#32770" && IsWindowVisible(hwnd))
            {
                if (!_seen.TryGetValue(hwnd, out var first) || first == DateTime.MaxValue)
                {
                    if (first != DateTime.MaxValue)
                        _seen[hwnd] = now;
                    return true;
                }
                // Give Windows' own dialogs time to finish opening, as a person would, before pressing Cancel.
                if ((now - first).TotalMilliseconds < 1500)
                    return true;
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                var text = new StringBuilder(2048);
                GetDlgItemText(hwnd, 0xFFFF, text, text.Capacity);
                string message = text.ToString().Replace("\r", " ").Replace("\n", " ");
                _log.AppendLine($"           box '{title}': {message}");
                if (message.Contains("went wrong", StringComparison.OrdinalIgnoreCase) || message.Contains("Exception", StringComparison.Ordinal))
                    Problem($"{_step}: message box '{message}'");
                _seen[hwnd] = DateTime.MaxValue;
                PostMessage(hwnd, 0x0111 /* WM_COMMAND */, (IntPtr)2 /* IDCANCEL */, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumProc callback, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDlgItemText(IntPtr hwnd, int id, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
