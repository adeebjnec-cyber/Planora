using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Planora.App.ViewModels;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.App.Dialogs;

/// <summary>
/// Organizer: copy calendars, tables, filters, groups, views, custom fields, reports and macros between
/// this project and another project file.
/// </summary>
public sealed class OrganizerDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly ComboBox _kind = new() { Width = 180, ItemsSource = Enum.GetValues<OrganizerItemKind>().Select(Label).ToList(), SelectedIndex = 0 };
    private readonly ListBox _mine = new() { Width = 280, Height = 300, SelectionMode = SelectionMode.Extended };
    private readonly ListBox _theirs = new() { Width = 280, Height = 300, SelectionMode = SelectionMode.Extended };
    private readonly TextBlock _otherName = new() { Text = "(no file open)", Width = 280, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Width = 720 };
    private Project? _other;
    private string? _otherPath;
    private bool _otherChanged;

    public OrganizerDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Organizer");
        _kind.SelectionChanged += (_, _) => Refresh();

        var open = Button("Open...", OpenOther);
        var toMine = Button("<< Copy", () => CopyToMine());
        var toTheirs = Button("Copy >>", CopyToTheirs);
        var delete = Button("Delete", DeleteMine);
        var saveOther = Button("Save Other File", SaveOther);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 4, 0, 0) };
        close.Click += (_, _) =>
        {
            if (_otherChanged && MessageBox.Show(this, $"Save the changes to {Path.GetFileName(_otherPath)}?", "Organizer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                SaveOther();
        };

        var middle = new StackPanel { Margin = new Thickness(12, 40, 12, 0), Children = { toMine, toTheirs, delete } };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                Rows.Row(Rows.Label("Items:"), _kind),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children =
                    {
                        new StackPanel { Children = { new TextBlock { Text = $"This project: {owner.Project.Name}", FontWeight = FontWeights.SemiBold, Width = 280, TextTrimming = TextTrimming.CharacterEllipsis }, _mine } },
                        middle,
                        new StackPanel { Children = { Rows.Row(new TextBlock { Text = "Other file: ", FontWeight = FontWeights.SemiBold }, _otherName), _theirs, open, saveOther } },
                    },
                },
                _status,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
            },
        };
        Refresh();
    }

    private static string Label(OrganizerItemKind kind) => kind switch
    {
        OrganizerItemKind.CustomField => "Custom fields",
        OrganizerItemKind.View => "Views",
        OrganizerItemKind.Macro => "Macro modules",
        _ => kind + "s",
    };

    private OrganizerItemKind Kind => (OrganizerItemKind)Math.Max(0, _kind.SelectedIndex);

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, MinWidth = 100, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 4, 0, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh()
    {
        _mine.ItemsSource = Organizer.Names(_owner.Project, Kind);
        _theirs.ItemsSource = _other is null ? null : Organizer.Names(_other, Kind);
    }

    private void OpenOther()
    {
        var dialog = new OpenFileDialog { Filter = "Project files (*.planora;*.xml;*.mpp)|*.planora;*.xml;*.mpp|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            _other = ProjectLoader.Load(dialog.FileName);
            _otherPath = dialog.FileName;
            _otherChanged = false;
            _otherName.Text = Path.GetFileName(dialog.FileName);
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            Form.Warn(this, $"Could not open the file: {ex.Message}");
        }
    }

    private void CopyToMine()
    {
        if (_other is null || _theirs.SelectedItems.Count == 0)
            return;
        var names = _theirs.SelectedItems.Cast<string>().ToList();
        var other = _other;
        var kind = Kind;
        if (_owner.TryEdit("Organizer copy", p => names.ForEach(n => Organizer.Copy(other, p, kind, n))))
        {
            _owner.DefinitionsChanged();
            _status.Text = $"Copied {names.Count} item(s) into this project.";
            Refresh();
        }
    }

    private void CopyToTheirs()
    {
        if (_other is null || _mine.SelectedItems.Count == 0)
            return;
        foreach (var name in _mine.SelectedItems.Cast<string>())
            Organizer.Copy(_owner.Project, _other, Kind, name);
        _otherChanged = true;
        _status.Text = $"Copied into {Path.GetFileName(_otherPath)}; use Save Other File to keep the change.";
        Refresh();
    }

    private void DeleteMine()
    {
        var names = _mine.SelectedItems.Cast<string>().ToList();
        if (names.Count == 0)
            return;
        var kind = Kind;
        var kept = new List<string>();
        if (_owner.TryEdit("Organizer delete", p =>
            {
                foreach (var name in names)
                {
                    if (!Organizer.Delete(p, kind, name))
                        kept.Add(name);
                }
            }))
        {
            _owner.DefinitionsChanged();
            _status.Text = kept.Count == 0 ? $"Deleted {names.Count} item(s)." : $"Kept {string.Join(", ", kept)}: still in use.";
            Refresh();
        }
    }

    private void SaveOther()
    {
        if (_other is null || _otherPath is null)
            return;
        try
        {
            if (Path.GetExtension(_otherPath).Equals(".planora", StringComparison.OrdinalIgnoreCase))
                ProjectFile.Save(_other, _otherPath);
            else if (Path.GetExtension(_otherPath).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                MspdiFile.Save(_other, _otherPath);
            else
            {
                Form.Warn(this, "Only Planora (.planora) and Microsoft Project XML (.xml) files can be saved from the Organizer.");
                return;
            }
            _otherChanged = false;
            _status.Text = $"Saved {Path.GetFileName(_otherPath)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Form.Warn(this, $"Could not save the file: {ex.Message}");
        }
    }
}
