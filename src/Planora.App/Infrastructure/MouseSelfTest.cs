using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Planora.App.Controls;
using Planora.App.ViewModels;
using Planora.App.Views;
using Planora.Engine.Model;

namespace Planora.App.Infrastructure;

/// <summary>
/// Developer aid: "Planora.exe --mousetest log.txt" checks the interactions only a mouse and keyboard can do, using
/// real input (the cursor moves): dragging, resizing and linking Gantt bars, setting progress by dragging, the bar
/// hover card, the sidebar, menus, the Critical path switch, collapsing a phase and Ctrl+K command search.
/// </summary>
public sealed class MouseSelfTest
{
    private readonly MainWindow _window;
    private readonly string _logPath;
    private readonly StringBuilder _log = new();
    private int _passed, _failed;

    public MouseSelfTest(MainWindow window, string logPath)
    {
        _window = window;
        _logPath = logPath;
    }

    private MainViewModel Vm => _window.ViewModel;

    public async void Run()
    {
        try
        {
            Vm.LoadSample();
            _window.WindowState = WindowState.Maximized;
            await Wait(2500);
            await Click(new Point(_window.ActualWidth / 2, 20), _window);   // the breadcrumb: brings Planora to the front
            await Wait(300);

            var gantt = ((GanttView)_window.ViewHost.Content).Gantt;

            int downs = 0, moves = 0;
            gantt.PreviewMouseLeftButtonDown += (_, _) => downs++;
            gantt.PreviewMouseMove += (_, _) => moves++;
            var probe = BarPoint(gantt, Task("Design office layout"), 0.5);
            var hit = _window.InputHitTest(gantt.TranslatePoint(probe, _window)) as DependencyObject;
            _log.AppendLine($"probe {probe} on chart {gantt.ActualWidth:0}x{gantt.ActualHeight:0}; hit: {hit?.GetType().Name}; screen {gantt.PointToScreen(probe)}");

            // 1. Drag a bar to the right: the task moves later (Start No Earlier Than).
            var design = Task("Design office layout");
            var before = design.Start;
            await Drag(BarPoint(gantt, design, 0.5), new Vector(gantt.XOf(before.AddDays(3)) - gantt.XOf(before), 0), gantt);
            design = Task("Design office layout");
            Check("drag a bar moves the task later", design.Start > before && design.ConstraintType == ConstraintType.StartNoEarlierThan,
                $"start {before:d MMM} → {design.Start:d MMM}; chart saw {downs} presses, {moves} moves");

            // 2. Ctrl+Z puts it back.
            await Keys(VK_CONTROL, 0x5A);
            Check("Ctrl+Z undoes the drag", Task("Design office layout").Start == before, $"start {Task("Design office layout").Start:d MMM}");

            // 3. Drag the right edge: the duration grows.
            var cabling = Task("Design office layout");
            long duration = cabling.DurationMinutes;
            var grab = BarPoint(gantt, cabling, 1.0, inset: -2);
            var drop = grab + new Vector(gantt.XOf(cabling.Finish.AddDays(4)) - gantt.XOf(cabling.Finish), 0);
            _log.AppendLine($"resize: {cabling.Start:ddd d MMM HH:mm} – {cabling.Finish:ddd d MMM HH:mm}, {duration / 60.0:0.#} h; grab at {gantt.DateAt(grab.X):ddd d MMM HH:mm}, drop at {gantt.DateAt(drop.X):ddd d MMM HH:mm}");
            await Drag(BarPoint(gantt, cabling, 1.0, inset: -2), new Vector(gantt.XOf(cabling.Finish.AddDays(4)) - gantt.XOf(cabling.Finish), 0), gantt);
            var after = Task("Design office layout");
            _log.AppendLine($"resize result: {after.Start:ddd d MMM HH:mm} – {after.Finish:ddd d MMM HH:mm}, {after.DurationMinutes / 60.0:0.#} h");
            Check("drag the right edge lengthens the task", Task("Design office layout").DurationMinutes > duration,
                $"{duration / 480.0:0.#} → {Task("Design office layout").DurationMinutes / 480.0:0.#} days; finish {cabling.Finish:ddd d MMM HH:mm} → {Task("Design office layout").Finish:ddd d MMM HH:mm}");

            // 4. Drag the left edge: sets % complete.
            var furniture = Task("Order furniture");
            await Drag(BarPoint(gantt, furniture, 0.0, inset: 2), new Vector((gantt.XOf(furniture.Finish) - gantt.XOf(furniture.Start)) * 0.5, 0), gantt);
            Check("drag the left edge sets progress", Task("Order furniture").PercentComplete > 0, $"{Task("Order furniture").PercentComplete:0}% complete");

            // 5. Drag a bar down onto another task: links them.
            var pack = Task("Gather space requirements");
            var setup = Task("Sign lease");
            double rowGap = (Row(setup) - Row(pack)) * gantt.RowHeight;
            await Drag(BarPoint(gantt, pack, 0.5), new Vector(0, rowGap), gantt);
            Check("drag a bar onto another task links them", Task("Sign lease").Predecessors.Any(l => l.PredecessorId == pack.Id),
                string.Join(", ", Task("Sign lease").Predecessors.Select(l => l.PredecessorId)) + $"; pack row {Row(pack)}, target row {Row(setup)}, status '{Vm.Status}', pack {pack.Start:d MMM}-{pack.Finish:d MMM}");

            // 6. Hovering a bar shows the details card (after leaving the chart, as tooltips stay hidden after a click).
            await Move(new Point(100, 300), _window);
            await Wait(600);
            await Move(BarPoint(gantt, Task("Sign lease"), 0.5) + new Vector(-3, 0), gantt);
            await Move(BarPoint(gantt, Task("Sign lease"), 0.5), gantt);
            await Wait(1600);
            Check("hovering a bar shows the details card", gantt.ToolTip is ToolTip { IsOpen: true }, gantt.ToolTip?.GetType().Name ?? "no tooltip");
            await Move(new Point(gantt.ActualWidth - 20, gantt.ActualHeight - 30), gantt);

            // 7. Clicking the arrow of a phase collapses it.
            int rows = Vm.Rows.Count;
            if (FindToggle() is { } toggle)
            {
                await Click(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2), toggle);
                Check("the phase arrow collapses the phase", Vm.Rows.Count < rows, $"{rows} → {Vm.Rows.Count} rows");
                if (FindToggle() is { } again)
                    await Click(new Point(again.ActualWidth / 2, again.ActualHeight / 2), again);
                Check("the phase arrow expands it again", Vm.Rows.Count == rows, $"{Vm.Rows.Count} rows");
            }
            else
            {
                Check("the phase arrow is on screen", false, "not found");
            }

            // 8. The Critical path switch.
            bool critical = Vm.ShowCriticalPath;
            var criticalSwitch = FindVisual<ToggleButton>(_window, t => t.Content as string == "Critical path");
            if (criticalSwitch is not null)
                await Click(new Point(criticalSwitch.ActualWidth / 2, criticalSwitch.ActualHeight / 2), criticalSwitch);
            Check("the Critical path switch turns the critical path on", Vm.ShowCriticalPath != critical, $"now {(Vm.ShowCriticalPath ? "on" : "off")}");

            // 9. A menu opens on click and closes with Esc.
            var taskMenu = _window.MainMenu.Items.OfType<MenuItem>().First(m => m.Header as string == "Task");
            await Click(new Point(taskMenu.ActualWidth / 2, taskMenu.ActualHeight / 2), taskMenu);
            await Wait(400);
            Check("clicking Task opens its menu", taskMenu.IsSubmenuOpen, "");
            await Keys(VK_ESCAPE);
            await Keys(VK_ESCAPE);
            Check("Esc closes the menu", !taskMenu.IsSubmenuOpen, "");

            // 10. Ctrl+K, type, Enter runs a command.
            int zoom = Vm.ZoomIndex;
            await Keys(VK_CONTROL, 0x4B);
            await Wait(500);
            Check("Ctrl+K opens command search", Application.Current.Windows.OfType<Dialogs.CommandPalette>().Any(), "");
            await Type("zoom in");
            await Keys(VK_RETURN);
            await Wait(500);
            Check("typing a command and Enter runs it", Vm.ZoomIndex == zoom + 1 && !Application.Current.Windows.OfType<Dialogs.CommandPalette>().Any(),
                $"zoom {zoom} → {Vm.ZoomIndex}");
            await Keys(VK_CONTROL, 0x4B);
            await Wait(500);
            await Keys(VK_ESCAPE);
            await Wait(400);
            Check("Esc closes command search", !Application.Current.Windows.OfType<Dialogs.CommandPalette>().Any(), "");

            // 11. The sidebar switches views.
            var nav = _window.Nav;
            var sheet = Vm.Views.First(v => v.Kind == ViewKind.ResourceSheet);
            if (nav.ItemContainerGenerator.ContainerFromItem(sheet) is ListBoxItem item)
            {
                await Click(new Point(item.ActualWidth / 2, item.ActualHeight / 2), item);
                Check("clicking Resource Sheet in the sidebar opens it", Vm.CurrentView == ViewKind.ResourceSheet, Vm.CurrentView.ToString());
            }
            var gant = Vm.Views.First(v => v.Kind == ViewKind.Gantt);
            if (nav.ItemContainerGenerator.ContainerFromItem(gant) is ListBoxItem back)
                await Click(new Point(back.ActualWidth / 2, back.ActualHeight / 2), back);
            Check("and back to the Gantt Chart", Vm.CurrentView == ViewKind.Gantt, Vm.CurrentView.ToString());
        }
        catch (Exception ex)
        {
            _failed++;
            _log.AppendLine("ERROR " + ex);
        }
        _log.Insert(0, $"Planora mouse and keyboard test: {_passed} passed, {_failed} failed\n\n");
        File.WriteAllText(_logPath, _log.ToString());
        Vm.Document.ForgetChanges();
        Vm.Document.DeleteAutosave();
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ helpers

    private ProjectTask Task(string name) => Vm.Project.Tasks.First(t => t.Name == name);

    private int Row(ProjectTask task) => Vm.Rows.ToList().FindIndex(r => r.Task?.Id == task.Id);

    /// <summary>A point on a task's bar: <paramref name="along"/> 0 = start, 1 = finish, moved inwards by <paramref name="inset"/> pixels.</summary>
    private Point BarPoint(GanttChart gantt, ProjectTask task, double along, double inset = 0)
    {
        double x1 = gantt.XOf(task.Start), x2 = gantt.XOf(task.Finish);
        double x = x1 + (x2 - x1) * along + (along <= 0 ? inset : along >= 1 ? inset : 0);
        double y = gantt.HeaderHeight + Row(task) * gantt.RowHeight + gantt.RowHeight / 2;
        return new Point(x, y);
    }

    private Button? FindToggle() => FindVisual<Button>(_window, b => b.Name == "Toggle" && b.IsVisible && b.DataContext is TaskRowViewModel { Name: "Planning" or "Preparation" or "Move" });

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t))
                return t;
            if (FindVisual(child, match) is { } found)
                return found;
        }
        return null;
    }

    private void Check(string name, bool ok, string detail)
    {
        if (ok) _passed++; else _failed++;
        _log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
    }

    private static System.Threading.Tasks.Task Wait(int ms) => System.Threading.Tasks.Task.Delay(ms);

    private static Point Screen(Point p, Visual relativeTo) => relativeTo.PointToScreen(p);

    private static async System.Threading.Tasks.Task Move(Point p, Visual relativeTo)
    {
        var s = Screen(p, relativeTo);
        SetCursorPos((int)s.X, (int)s.Y);
        await Wait(150);
    }

    private static async System.Threading.Tasks.Task Click(Point p, Visual relativeTo)
    {
        await Move(p, relativeTo);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        await Wait(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        await Wait(400);
    }

    private static async System.Threading.Tasks.Task Drag(Point from, Vector by, Visual relativeTo)
    {
        await Move(from, relativeTo);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        await Wait(120);
        for (int i = 1; i <= 12; i++)
        {
            var s = Screen(from + by * i / 12, relativeTo);
            SetCursorPos((int)s.X, (int)s.Y);
            await Wait(90);
        }
        await Wait(500);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        await Wait(600);
    }

    private static async System.Threading.Tasks.Task Keys(params byte[] keys)
    {
        foreach (var k in keys)
            keybd_event(k, 0, 0, IntPtr.Zero);
        await Wait(50);
        foreach (var k in keys.Reverse())
            keybd_event(k, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        await Wait(300);
    }

    private static async System.Threading.Tasks.Task Type(string text)
    {
        foreach (char c in text.ToUpperInvariant())
        {
            byte vk = c == ' ' ? (byte)0x20 : (byte)c;
            keybd_event(vk, 0, 0, IntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            await Wait(40);
        }
        await Wait(200);
    }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11, VK_ESCAPE = 0x1B, VK_RETURN = 0x0D;

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
