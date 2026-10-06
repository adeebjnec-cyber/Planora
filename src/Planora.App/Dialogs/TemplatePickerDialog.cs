using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Planora.App.Documents;
using Planora.App.Infrastructure;
using Planora.Engine.Interop;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>New from Template: pick a built-in or saved template and a start date.</summary>
public sealed class TemplatePickerDialog : Window
{
    public static string TemplatesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Planora", "Templates");

    private readonly ListBox _list;
    private readonly TextBox _start;

    public TemplatePickerDialog()
    {
        Title = "New from Template";
        Owner = Application.Current.MainWindow;
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var entries = BuiltInTemplates.All.Select(t => new Entry(t.Name, t.Create, null)).ToList();
        if (Directory.Exists(TemplatesFolder))
        {
            foreach (var file in Directory.GetFiles(TemplatesFolder, "*" + ProjectTemplate.Extension).OrderBy(f => f))
                entries.Add(new Entry(Path.GetFileNameWithoutExtension(file) + "  (my template)", null, file));
        }

        _list = new ListBox { ItemsSource = entries, DisplayMemberPath = nameof(Entry.Name), Width = 360, Height = 240, SelectedIndex = 0 };
        var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7).AddHours(8);
        _start = new TextBox { Text = DateText.Format(nextMonday), Width = 160, Padding = new Thickness(3, 2, 3, 2), HorizontalAlignment = HorizontalAlignment.Left };
        var browse = new Button { Content = "Browse...", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Create", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        browse.Click += (_, _) => Browse();
        ok.Click += (_, _) => Create(((Entry)_list.SelectedItem)!);
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is Entry entry) Create(entry); };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Template:", Margin = new Thickness(0, 0, 0, 4) }, _list, browse,
                new TextBlock { Text = "Project start:", Margin = new Thickness(0, 12, 0, 4) }, _start,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
    }

    /// <summary>The project created from the chosen template.</summary>
    public Project? Result { get; private set; }

    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = $"Planora template (*{ProjectTemplate.Extension})|*{ProjectTemplate.Extension}", InitialDirectory = TemplatesFolder };
        if (dialog.ShowDialog(this) == true)
            Create(new Entry(Path.GetFileNameWithoutExtension(dialog.FileName), null, dialog.FileName));
    }

    private void Create(Entry entry)
    {
        if (!DateText.TryParse(_start.Text, Engine.Calendars.WorkingCalendar.CreateStandard(), false, out var start))
        {
            MessageBox.Show(this, "Enter a valid start date.", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Result = entry.Path is { } path ? ProjectTemplate.Instantiate(path, start) : entry.Create!(start);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open the template: {ex.Message}", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed record Entry(string Name, Func<DateTime, Project>? Create, string? Path);
}
