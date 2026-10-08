using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>
/// Spelling (F7): checks task names and notes, then resource names, with Windows' built-in spell checker
/// (works offline). Change, Change All, Ignore and Ignore All work as in Microsoft Project.
/// </summary>
public sealed class SpellingDialog : Window
{
    private sealed record Spot(int? TaskId, int? ResourceId, string Field, int Start, int Length, string Word);

    private readonly MainViewModel _owner;
    private readonly TextBox _checker = new();
    private readonly HashSet<string> _ignored = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly TextBlock _where = new() { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _notInDictionary = Rows.Box(220), _changeTo = Rows.Box(220);
    private readonly ListBox _suggestions = new() { Height = 110, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<Button> _actionButtons = new();
    private List<Spot> _spots = new();
    private IReadOnlyList<string> _currentSuggestions = Array.Empty<string>();
    private int _index;

    public SpellingDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Spelling");
        _checker.Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
        SpellCheck.SetIsEnabled(_checker, true);
        _notInDictionary.IsReadOnly = true;
        _suggestions.SelectionChanged += (_, _) =>
        {
            if (_suggestions.SelectedItem is string s && _currentSuggestions.Count > 0)
                _changeTo.Text = s;
        };

        var buttons = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        AddButton(buttons, "Ignore", Ignore);
        AddButton(buttons, "Ignore All", IgnoreAll);
        AddButton(buttons, "Change", () => Apply(all: false));
        AddButton(buttons, "Change All", () => Apply(all: true));
        buttons.Children.Add(new Button { Content = "Close", IsCancel = true, MinWidth = 100, Padding = new Thickness(8, 3, 8, 3) });

        var form = new StackPanel
        {
            Width = 340,
            Children =
            {
                _where,
                Rows.Row(Rows.Label("Not in dictionary:", 110), _notInDictionary),
                Rows.Row(Rows.Label("Change to:", 110), _changeTo),
                Rows.Row(new TextBlock { Text = "Suggestions:", Width = 110, Margin = new Thickness(0, 4, 6, 0) }, _suggestions),
            },
        };
        var panel = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        // The spell checker only reports errors for a text box inside a live window; keep it out of sight.
        var hidden = new Border { Width = 1, Height = 1, ClipToBounds = true, Child = _checker };
        DockPanel.SetDock(hidden, Dock.Bottom);
        panel.Children.Add(hidden);
        panel.Children.Add(form);
        Content = panel;

        Loaded += (_, _) =>
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                Rescan();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            Show(0);
        };
    }

    private void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, MinWidth = 100, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 4) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        _actionButtons.Add(button);
    }

    private static readonly System.Text.RegularExpressions.Regex WordPattern = new(@"\p{L}[\p{L}'’]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The words Windows' spell checker does not know, by checking each distinct word once.</summary>
    private readonly Dictionary<string, bool> _known = new();

    /// <summary>Every misspelt word in the project's texts, in order.</summary>
    private void Rescan()
    {
        var project = _owner.Project;
        var texts = new List<(string Text, int? TaskId, int? ResourceId, string Field)>();
        foreach (var task in project.Tasks)
        {
            texts.Add((task.Name, task.Id, null, "Name"));
            texts.Add((task.Notes, task.Id, null, "Notes"));
        }
        foreach (var resource in project.Resources)
            texts.Add((resource.Name, null, resource.Id, "Resource Name"));

        // Checking one long text per note is slow (the checker re-reads the text at every step), so all
        // new distinct words are checked together in a single pass.
        var unchecked_ = texts.Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .SelectMany(t => WordPattern.Matches(t.Text).Select(m => m.Value))
            .Where(w => !_known.ContainsKey(w)).Distinct().ToList();
        var misspelt = new HashSet<string>();
        // Small batches keep each scan short: the checker's cost grows with the text length at every step.
        foreach (var batch in unchecked_.Chunk(40))
        {
            var joined = string.Join("\n", batch);
            _checker.Text = joined;
            _checker.UpdateLayout();
            int at = _checker.GetNextSpellingErrorCharacterIndex(0, LogicalDirection.Forward);
            while (at >= 0 && at < joined.Length)
            {
                int length = Math.Max(1, _checker.GetSpellingErrorLength(at));
                misspelt.Add(joined.Substring(at, Math.Min(length, joined.Length - at)));
                // The checker can report the same position again; always move forward so the scan ends.
                int next = _checker.GetNextSpellingErrorCharacterIndex(at + length, LogicalDirection.Forward);
                if (next <= at)
                    break;
                at = next;
            }
        }
        foreach (var word in unchecked_)
            _known[word] = !misspelt.Contains(word);

        var spots = new List<Spot>();
        foreach (var (text, taskId, resourceId, field) in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;
            foreach (System.Text.RegularExpressions.Match m in WordPattern.Matches(text))
            {
                if (_known.TryGetValue(m.Value, out var ok) && !ok)
                    spots.Add(new Spot(taskId, resourceId, field, m.Index, m.Length, m.Value));
            }
        }
        _spots = spots.Where(s => !_ignored.Contains(s.Word)).ToList();
    }

    private void Show(int index)
    {
        _index = index;
        while (_index < _spots.Count && _ignored.Contains(_spots[_index].Word))
            _index++;
        if (_index >= _spots.Count)
        {
            _where.Text = "The spelling check is complete.";
            _notInDictionary.Text = _changeTo.Text = "";
            _suggestions.ItemsSource = null;
            _actionButtons.ForEach(b => b.IsEnabled = false);
            return;
        }
        var spot = _spots[_index];
        var project = _owner.Project;
        string where = spot.TaskId is { } id && project.GetTask(id) is { } t
            ? $"task {project.IndexOf(t) + 1} '{t.Name}'"
            : $"resource '{project.Resources.FirstOrDefault(r => r.Id == spot.ResourceId)?.Name}'";
        _where.Text = $"{spot.Field} of {where}";
        _notInDictionary.Text = spot.Word;
        _currentSuggestions = SuggestionsFor(spot.Word);
        _suggestions.ItemsSource = _currentSuggestions.Count > 0 ? _currentSuggestions : new[] { "(no suggestions)" };
        _suggestions.SelectedIndex = _currentSuggestions.Count > 0 ? 0 : -1;
        _changeTo.Text = _currentSuggestions.FirstOrDefault() ?? spot.Word;
        if (spot.TaskId is { } taskId && project.GetTask(taskId) is { } task)
            _owner.SelectTask(task);
    }

    private IReadOnlyList<string> SuggestionsFor(string word)
    {
        _checker.Text = word;
        _checker.UpdateLayout();
        int at = _checker.GetNextSpellingErrorCharacterIndex(0, LogicalDirection.Forward);
        return at >= 0 && _checker.GetSpellingError(at) is { } error ? error.Suggestions.Take(12).ToList() : Array.Empty<string>();
    }

    private void Ignore() => Show(_index + 1);

    private void IgnoreAll()
    {
        if (_index < _spots.Count)
            _ignored.Add(_spots[_index].Word);
        Show(_index + 1);
    }

    private void Apply(bool all)
    {
        if (_index >= _spots.Count)
            return;
        var current = _spots[_index];
        var targets = all ? _spots.Where(s => s.Word == current.Word).ToList() : new List<Spot> { current };
        string replacement = _changeTo.Text;
        int before = _spots.Take(_index).Count(s => !targets.Contains(s));
        _owner.TryEdit("Spelling", p =>
        {
            // Replace from the end of each text so earlier positions stay valid.
            foreach (var spot in targets.OrderByDescending(s => s.Start))
                Set(p, spot, replacement);
        });
        Rescan();
        Show(Math.Min(before, _spots.Count));
    }

    private static string Swap(string text, Spot s, string with) =>
        s.Start + s.Length <= text.Length && text.Substring(s.Start, s.Length) == s.Word ? text[..s.Start] + with + text[(s.Start + s.Length)..] : text;

    private static void Set(Project project, Spot spot, string replacement)
    {
        if (spot.TaskId is { } id && project.GetTask(id) is { } task)
        {
            if (spot.Field == "Name")
                task.Name = Swap(task.Name, spot, replacement);
            else
                task.Notes = Swap(task.Notes, spot, replacement);
        }
        else if (project.Resources.FirstOrDefault(r => r.Id == spot.ResourceId) is { } resource)
        {
            resource.Name = Swap(resource.Name, spot, replacement);
        }
    }
}
