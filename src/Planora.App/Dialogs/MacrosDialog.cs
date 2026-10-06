using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Macros;

namespace Planora.App.Dialogs;

/// <summary>
/// Macros (View > Macros, Alt+F8): edit the project's macro modules and run their Subs. A macro runs as one
/// undoable change; if it fails, the project is left as it was and the failing line is highlighted.
/// </summary>
public sealed class MacrosDialog : Window
{
    private const string Example = """
        ' Planora macros use a VBA-like language. Durations and work are in minutes.
        Sub MarkLongTasks()
            Dim count
            count = 0
            For Each t In ActiveProject.Tasks
                If Not t.Summary And t.Duration > 5 * 480 Then
                    t.Marked = True
                    count = count + 1
                End If
            Next t
            MsgBox count & " task(s) longer than 5 days were marked."
        End Sub
        """;

    private const string Help = """
        Statements: Sub/End Sub, Dim, x = expr, Set t = Tasks.Add("Name"), t.Field = expr, [Field] = expr,
        For Each t In Tasks | Resources | SelectedTasks | t.Children | t.Assignments ... Next,
        For i = 1 To 10 [Step 2] ... Next, If ... Then ... ElseIf ... Else ... End If, Do While/Until ... Loop,
        Exit For/Do/Sub, Call OtherSub, MsgBox text, Debug.Print values, t.Delete, LinkTasks a, b[, "SS"],
        UnlinkTasks a, b, AssignResource t, "Name"[, units], Indent t, Outdent t, CalculateProject.
        Values: t.Name, t.Duration, t.Start, t.Finish, t.PercentComplete, t.Text1, t.[Resource Names],
        r.Name, r.MaxUnits, r.StandardRate, ActiveProject.Name; functions as in custom field formulas
        (IIf, Left, Len, Format, DateAdd, ProjDateAdd, ...).
        """;

    private readonly MainViewModel _owner;
    private readonly List<MacroModule> _modules;
    private readonly ListBox _moduleList = new() { Width = 170, Height = 120 };
    private readonly ListBox _subList = new() { Width = 170, Height = 230 };
    private readonly TextBox _code = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Width = 640,
        Height = 400,
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Width = 640 };
    private MacroModule? _current;

    public MacrosDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Macros");
        _modules = owner.Project.Macros.Select(m => new MacroModule { Name = m.Name, Code = m.Code }).ToList();
        if (_modules.Count == 0)
            _modules.Add(new MacroModule { Name = "Module1", Code = Example });

        _moduleList.SelectionChanged += (_, _) => ShowModule();
        _code.TextChanged += (_, _) =>
        {
            if (_current is not null)
                _current.Code = _code.Text;
            RefreshSubs();
        };

        var run = Button("Run", Run);
        var newModule = Button("New Module", () =>
        {
            var module = new MacroModule { Name = $"Module{_modules.Count + 1}", Code = "Sub NewMacro()\n    \nEnd Sub\n" };
            _modules.Add(module);
            RefreshModules(module);
        });
        var delete = Button("Delete Module", () =>
        {
            if (_current is null || MessageBox.Show(this, $"Delete {_current.Name}?", "Macros", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            _modules.Remove(_current);
            RefreshModules(_modules.FirstOrDefault());
        });
        var save = Button("Save to Project", () => Save());
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 4, 0, 0) };
        close.Click += (_, _) =>
        {
            if (Changed() && MessageBox.Show(this, "Save the macro changes to the project?", "Macros", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Save();
        };

        var left = new StackPanel
        {
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock { Text = "Modules:" }, _moduleList,
                new TextBlock { Text = "Macros (Subs):", Margin = new Thickness(0, 8, 0, 0) }, _subList,
                run, newModule, delete, save, close,
            },
        };
        var right = new StackPanel
        {
            Children =
            {
                _code,
                _status,
                new TextBlock { Text = Help, TextWrapping = TextWrapping.Wrap, Width = 640, Foreground = Brushes.DimGray, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) },
            },
        };
        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14), Children = { left, right } };
        RefreshModules(_modules[0]);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 4, 0, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshModules(MacroModule? select)
    {
        _moduleList.ItemsSource = null;
        _moduleList.ItemsSource = _modules;
        _moduleList.DisplayMemberPath = nameof(MacroModule.Name);
        _moduleList.SelectedItem = select;
        if (select is null)
            ShowModule();
    }

    private void ShowModule()
    {
        _current = _moduleList.SelectedItem as MacroModule;
        _code.IsEnabled = _current is not null;
        _code.Text = _current?.Code ?? "";
        RefreshSubs();
    }

    private void RefreshSubs()
    {
        var selected = _subList.SelectedItem as string;
        var subs = Macro.SubNames(_code.Text);
        _subList.ItemsSource = subs;
        _subList.SelectedItem = subs.Contains(selected) ? selected : subs.FirstOrDefault();
    }

    private bool Changed()
    {
        var saved = _owner.Project.Macros;
        return saved.Count != _modules.Count || saved.Zip(_modules).Any(p => p.First.Name != p.Second.Name || p.First.Code != p.Second.Code);
    }

    private bool Save()
    {
        var modules = _modules.Select(m => new MacroModule { Name = m.Name, Code = m.Code }).ToList();
        bool ok = _owner.TryEdit("Edit macros", p =>
        {
            p.Macros.Clear();
            p.Macros.AddRange(modules);
        });
        if (ok)
            _status.Text = "Macros saved with the project.";
        return ok;
    }

    private void Run()
    {
        if (_current is null)
            return;
        var code = _current.Code;
        var sub = _subList.SelectedItem as string;
        var selection = _owner.SelectedTasks.ToList();
        MacroResult? result = null;
        try
        {
            _owner.Document.Edit($"Macro {sub ?? _current.Name}", p =>
                result = Macro.Run(p, code, sub, selection, message => MessageBox.Show(this, message, "Macro", MessageBoxButton.OK, MessageBoxImage.Information)));
            _status.Foreground = Brushes.DarkGreen;
            _status.Text = $"{sub ?? "Macro"} finished ({result!.StatementsRun} statements). Edit > Undo reverses it."
                + (result.Output.Count > 0 ? Environment.NewLine + "Output:" + Environment.NewLine + string.Join(Environment.NewLine, result.Output) : "");
        }
        catch (MacroException ex)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = ex.Message + " The project was not changed.";
            HighlightLine(ex.Line);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Engine.Scheduling.SchedulingException)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = ex.Message + " The project was not changed.";
        }
    }

    private void HighlightLine(int line)
    {
        if (line <= 0 || line > _code.LineCount)
            return;
        int start = _code.GetCharacterIndexFromLineIndex(line - 1);
        _code.Focus();
        _code.Select(start, _code.GetLineLength(line - 1));
        _code.ScrollToLine(line - 1);
    }
}
