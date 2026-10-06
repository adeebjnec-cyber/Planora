using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Planora.App.ViewModels;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Dialogs;

/// <summary>Link to a task in another project file: choose the file, the task, the link type and lag.</summary>
public sealed class ExternalLinkDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly int _taskId;
    private readonly TextBox _file = Rows.Box(380);
    private readonly ListBox _tasks = new() { Height = 240, Width = 520, DisplayMemberPath = "Label" };
    private readonly ComboBox _type = new() { Width = 160, ItemsSource = new[] { "Finish-to-Start (FS)", "Start-to-Start (SS)", "Finish-to-Finish (FF)", "Start-to-Finish (SF)" }, SelectedIndex = 0 };
    private readonly TextBox _lag = Rows.Box(80);

    public ExternalLinkDialog(MainViewModel owner, ProjectTask task)
    {
        _owner = owner;
        _taskId = task.Id;
        Form.Setup(this, $"Link '{task.Name}' to a task in another project");
        _file.IsReadOnly = true;
        _lag.Text = "0d";
        var browse = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2) };
        browse.Click += (_, _) => Browse();

        var existing = task.ExternalPredecessors.Select(l => $"{l.TaskName} in {Path.GetFileName(l.ProjectPath)} ({TypeCode(l.Type)})").ToList();
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                Rows.Row(Rows.Label("Project file:", 90), _file, browse),
                new TextBlock { Text = "Predecessor task:", Margin = new Thickness(0, 8, 0, 2) },
                _tasks,
                Rows.Row(Rows.Label("Link type:", 90), _type, Rows.Label("   Lag:"), _lag),
                new TextBlock
                {
                    Text = existing.Count == 0 ? "This task has no external predecessors yet."
                        : "Already linked: " + string.Join("; ", existing),
                    TextWrapping = TextWrapping.Wrap, Width = 520, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0),
                },
                Form.Buttons(this, Apply, "Link"),
            },
        };
        Loaded += (_, _) => Browse();
    }

    private static string TypeCode(LinkType type) => type switch
    {
        LinkType.StartToStart => "SS",
        LinkType.FinishToFinish => "FF",
        LinkType.StartToFinish => "SF",
        _ => "FS",
    };

    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "Project files (*.planora;*.xml;*.mpp)|*.planora;*.xml;*.mpp|All files (*.*)|*.*", Title = "Choose the project with the predecessor task" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var other = ProjectLoader.Load(dialog.FileName);
            ProjectScheduler.Schedule(other);
            _file.Text = dialog.FileName;
            _tasks.ItemsSource = other.Tasks.Select(t => new
            {
                t.Id,
                Label = $"{other.IndexOf(t) + 1}. {new string(' ', (t.OutlineLevel - 1) * 3)}{t.Name}   ({t.Start.ToString("d MMM", CultureInfo.CurrentCulture)} – {t.Finish.ToString("d MMM", CultureInfo.CurrentCulture)})",
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            Form.Warn(this, $"Could not open the project: {ex.Message}");
        }
    }

    private void Apply()
    {
        if (_file.Text.Length == 0 || _tasks.SelectedItem is null)
        {
            Form.Warn(this, "Choose a project file and a task in it.");
            return;
        }
        if (!DurationText.TryParseLag(_lag.Text, _owner.Project.Options, out var parsedLag) || parsedLag.Format == LagFormat.PercentOfPredecessor)
        {
            Form.Warn(this, $"'{_lag.Text}' is not a valid lag (use a duration such as 2d or -1d).");
            return;
        }
        long lagMinutes = parsedLag.Minutes;
        int otherId = (int)_tasks.SelectedItem.GetType().GetProperty("Id")!.GetValue(_tasks.SelectedItem)!;
        var type = _type.SelectedIndex switch { 1 => LinkType.StartToStart, 2 => LinkType.FinishToFinish, 3 => LinkType.StartToFinish, _ => LinkType.FinishToStart };
        var path = _file.Text;
        if (_owner.TryEdit("Link to external task", p => ExternalLinks.Add(p.GetRequiredTask(_taskId), path, otherId, type, lagMinutes)))
            DialogResult = true;
    }
}

/// <summary>Over-allocations of pool resources once every sharing project's work is counted.</summary>
public sealed class PoolOverallocationsDialog : Window
{
    public PoolOverallocationsDialog(MainViewModel owner)
    {
        Form.Setup(this, "Resource Pool: Over-allocations Across Projects");
        var panel = new StackPanel { Margin = new Thickness(14), Width = 720 };
        try
        {
            var path = owner.Project.ResourcePoolPath!;
            var sharers = ResourcePool.Sharers(path);
            var results = ResourcePool.SharedOverallocations(owner.Project, owner.Document.FilePath);
            panel.Children.Add(new TextBlock { Text = $"Pool: {path}", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = sharers.Count == 0 ? "No projects are recorded as sharing this pool yet (share it from a saved project to record it)."
                    : "Sharing projects: " + string.Join(", ", sharers.Select(Path.GetFileName)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), Foreground = System.Windows.Media.Brushes.DimGray,
            });
            if (results.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "No pool resource is over-allocated across the sharing projects (checked day by day).", Margin = new Thickness(0, 6, 0, 6) });
            }
            else
            {
                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    Height = 320,
                    ItemsSource = results.Select(r => new
                    {
                        r.Resource,
                        From = r.From.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture),
                        To = r.To.AddDays(-1).ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture),
                        Peak = $"{r.PeakPercent:0}%",
                        Projects = string.Join(", ", r.Projects),
                    }).ToList(),
                };
                foreach (var (header, path2, width) in new[] { ("Resource", "Resource", 130.0), ("From", "From", 120.0), ("To", "To", 120.0), ("Peak", "Peak", 60.0), ("Projects", "Projects", 260.0) })
                    grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path2), Width = width });
                panel.Children.Add(grid);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            panel.Children.Add(new TextBlock { Text = $"Could not read the pool or a sharing project: {ex.Message}", TextWrapping = TextWrapping.Wrap });
        }
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(close);
        Content = panel;
    }
}
