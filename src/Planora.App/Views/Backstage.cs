using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Views;

/// <summary>
/// The File tab's full-window page (Microsoft Project's Backstage): Info, New, Open (with recent files),
/// Save, Save As, Print, Export and Options. Esc or the back arrow returns to the project.
/// </summary>
public sealed class Backstage : UserControl
{
    private static readonly Brush NavBackground = new SolidColorBrush(Color.FromRgb(0x1B, 0x23, 0x33));
    private static readonly Brush NavSelected = new SolidColorBrush(Color.FromRgb(0x2E, 0x38, 0x4D));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x26));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA2));

    private readonly MainViewModel _vm;
    private readonly StackPanel _nav = new() { Background = NavBackground, Width = 200 };
    private readonly ContentControl _page = new() { Margin = new Thickness(40, 30, 40, 20) };
    private readonly List<(Button Button, string Page)> _navButtons = new();

    public Backstage(MainViewModel vm)
    {
        _vm = vm;
        Background = Brushes.White;
        Focusable = true;

        var back = NavButton("←  Back to project", Close);
        back.FontSize = 13;
        back.Margin = new Thickness(0, 14, 0, 14);
        _nav.Children.Add(back);
        AddPage("Info", ShowInfo);
        AddPage("New", ShowNew);
        AddPage("Open", ShowOpen);
        _nav.Children.Add(NavButton("Save", () => Run(vm.SaveCommand)));
        _nav.Children.Add(NavButton("Save As", () => Run(vm.SaveAsCommand)));
        AddPage("Print", ShowPrint);
        AddPage("Export", ShowExport);
        _nav.Children.Add(NavButton("Options", () => Run(vm.OptionsCommand)));
        AddPage("About", ShowAbout);

        var credit = new TextBlock
        {
            Text = "Planora · Developed by Adeeb",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA2)),
            FontSize = 11,
            Margin = new Thickness(24, 0, 12, 18),
        };
        var navHost = new DockPanel { Background = NavBackground, Width = 200 };
        DockPanel.SetDock(credit, Dock.Bottom);
        navHost.Children.Add(credit);
        navHost.Children.Add(_nav);

        var root = new DockPanel();
        DockPanel.SetDock(navHost, Dock.Left);
        root.Children.Add(navHost);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _page });
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Select(StartPage);
                StartPage = "Info";
                Focus();
            }
        };
    }

    /// <summary>The page shown when the Backstage next opens (Info by default).</summary>
    public string StartPage { get; set; } = "Info";

    /// <summary>Raised when the page should close (back, Esc, or after a command).</summary>
    public event EventHandler? CloseRequested;

    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Closes the page, then runs a command on the project.</summary>
    private void Run(ICommand command, object? parameter = null)
    {
        Close();
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private Button NavButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(24, 9, 10, 9),
            Margin = new Thickness(10, 1, 10, 1),
            FontSize = 13.5,
            Cursor = Cursors.Hand,
        };
        button.Style = NavStyle;
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Flat navigation buttons on the dark bar: a soft highlight on hover.</summary>
    private static readonly Style NavStyle = CreateNavStyle();

    private static Style CreateNavStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x44)), "Bd"));
        template.Triggers.Add(hover);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        return style;
    }

    private void AddPage(string name, Action show)
    {
        var button = NavButton(name, () => Select(name));
        button.Tag = show;
        _navButtons.Add((button, name));
        _nav.Children.Add(button);
    }

    private void Select(string page)
    {
        foreach (var (button, name) in _navButtons)
        {
            button.Background = name == page ? NavSelected : Brushes.Transparent;
            if (name == page)
                ((Action)button.Tag)();
        }
    }

    private static TextBlock Title(string text) => new() { Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Accent, Margin = new Thickness(0, 0, 0, 20) };

    private static TextBlock Section(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) };

    private Button Tile(string title, string detail, Action action, double width = 260)
    {
        var button = new Button
        {
            Width = width,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14, 10, 14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE6, 0xEB)),
            Cursor = Cursors.Hand,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = detail, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) },
                },
            },
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ShowInfo()
    {
        var p = _vm.Project;
        var leaves = p.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        long total = leaves.Sum(t => t.DurationMinutes);
        double percent = total > 0 ? leaves.Sum(t => t.DurationMinutes * t.PercentComplete) / total : 0;
        string Date(DateTime d) => d.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);

        var facts = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new (string, string)[]
        {
            ("File", _vm.Document.FilePath ?? "Not saved yet"),
            ("Start", p.Tasks.Count > 0 ? Date(p.Start) : Date(p.StartDate)),
            ("Finish", p.Tasks.Count > 0 ? Date(p.Finish) : "—"),
            ("Duration", DurationText.Format(p.Tasks.Count > 0 ? p.Calendar.WorkingMinutesBetween(p.Start, p.Finish) : 0, p.Options)),
            ("% Complete", percent.ToString("0", CultureInfo.CurrentCulture) + "%"),
            ("Work", DurationText.Format(p.WorkMinutes, p.Options, DurationUnit.Hours)),
            ("Cost", p.Cost.ToString("C", CultureInfo.CurrentCulture)),
            ("Tasks / Resources", $"{leaves.Count} / {p.Resources.Count}"),
            ("Status date", p.StatusDate is { } s ? Date(s) : "NA"),
            ("Calendar", p.Calendar.Name),
            ("Scheduled from", p.ScheduleFromFinish ? "Project finish date" : "Project start date"),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Item1, Foreground = Muted, Margin = new Thickness(0, 3, 0, 3) };
            var value = new TextBlock { Text = rows[i].Item2, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            facts.Children.Add(label);
            facts.Children.Add(value);
        }

        _page.Content = new StackPanel
        {
            MaxWidth = 900,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                Title("Info"),
                new TextBlock { Text = p.Name, FontSize = 20, Margin = new Thickness(0, 0, 0, 12) },
                facts,
                new WrapPanel
                {
                    Children =
                    {
                        Tile("Project Information", "Start or finish date, status date, calendar and scheduling options.", () => Run(_vm.ProjectInformationCommand)),
                        Tile("Change Working Time", "Working hours, holidays and work weeks.", () => Run(_vm.ChangeWorkingTimeCommand)),
                        Tile("Reports", "Dashboards, burndown, cost and resource reports.", () => Run(_vm.ReportsCommand, "Project Overview")),
                    },
                },
            },
        };
    }

    private void ShowNew()
    {
        var tiles = new WrapPanel();
        tiles.Children.Add(Tile("Blank project", "Start from an empty plan.", () => Run(_vm.NewCommand)));
        tiles.Children.Add(Tile("From a template...", "Built-in and your own templates, with a start date.", () => Run(_vm.NewFromTemplateCommand)));
        tiles.Children.Add(Tile("Sample project", "An office move, to explore the features.", () => Run(_vm.OpenSampleCommand)));
        tiles.Children.Add(Tile("From Excel or CSV...", "Import a task list with the Import Wizard.", () => Run(_vm.ImportCommand)));
        _page.Content = new StackPanel { Children = { Title("New"), tiles } };
    }

    private void ShowOpen()
    {
        var panel = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Title("Open"));
        panel.Children.Add(new WrapPanel
        {
            Children =
            {
                Tile("Browse...", "Planora, Microsoft Project (.mpp, .xml), Excel, CSV, Primavera and more.", () => Run(_vm.OpenCommand)),
            },
        });
        panel.Children.Add(Section("Recent projects"));
        var recent = _vm.RecentFiles;
        if (recent.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Projects you open or save appear here.", Foreground = Muted });
        foreach (var path in recent)
        {
            bool exists = File.Exists(path);
            var item = Tile(Path.GetFileName(path), exists ? Path.GetDirectoryName(path) ?? "" : "Not found: " + path, () =>
            {
                Close();
                _vm.OpenRecent(path);
            }, width: 640);
            item.IsEnabled = exists;
            panel.Children.Add(item);
        }
        _page.Content = panel;
    }

    private void ShowPrint()
    {
        var tiles = new StackPanel
        {
            Width = 280,
            Children =
            {
                Tile("Print...", "Choose a printer and print the current view.", () => Run(_vm.PrintCommand)),
                Tile("Page Setup...", "Paper, orientation, margins, header, footer and legend.", () =>
                {
                    if (_vm.PageSetupCommand.CanExecute(null))
                        _vm.PageSetupCommand.Execute(null);
                    ShowPrint();
                }),
                Tile("Save as PDF...", "The current view as a PDF document.", () => Run(_vm.ExportPdfCommand)),
                Tile("Save as image...", "The current view as a PNG picture.", () => Run(_vm.ExportImageCommand)),
                Tile("Reports...", "Print or save a report as PDF.", () => Run(_vm.ReportsCommand, "Project Overview")),
            },
        };

        // Print preview of the current view, page by page.
        var preview = new Image { Stretch = Stretch.Uniform, MaxHeight = 540, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        var pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        var previous = new Button { Content = "◀", Padding = new Thickness(8, 2, 8, 2) };
        var next = new Button { Content = "▶", Padding = new Thickness(8, 2, 8, 2) };
        Output.PageSet? pages = null;
        int index = 0;
        void Show()
        {
            if (pages is null || pages.Pages.Count == 0)
                return;
            index = Math.Clamp(index, 0, pages.Pages.Count - 1);
            preview.Source = Output.PageOutput.Render(pages.Pages[index], pages.PageSize, 72);
            pageLabel.Text = $"Page {index + 1} of {pages.Pages.Count}";
            previous.IsEnabled = index > 0;
            next.IsEnabled = index < pages.Pages.Count - 1;
        }
        previous.Click += (_, _) => { index--; Show(); };
        next.Click += (_, _) => { index++; Show(); };
        try
        {
            pages = _vm.BuildPages(Output.GanttPages.PageSize(_vm));
            Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            pageLabel.Text = "The preview is not available: " + ex.Message;
        }
        var previewPanel = new StackPanel
        {
            Margin = new Thickness(24, 0, 0, 0),
            Children =
            {
                new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = preview, HorizontalAlignment = HorizontalAlignment.Left },
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { previous, pageLabel, next } },
            },
        };
        _page.Content = new StackPanel
        {
            Children =
            {
                Title("Print"),
                new StackPanel { Orientation = Orientation.Horizontal, Children = { tiles, previewPanel } },
            },
        };
    }

    private void ShowAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);
        _page.Content = new StackPanel
        {
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                Title("About"),
                new TextBlock { Text = "Planora", FontSize = 20, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = "Project scheduling and control software", Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) },
                new TextBlock { Text = $"Version {version.Major}.{version.Minor}.{version.Build}", Foreground = Muted, Margin = new Thickness(0, 12, 0, 0) },
                new Border
                {
                    Margin = new Thickness(0, 16, 0, 0),
                    Padding = new Thickness(14, 10, 14, 10),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xFC)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = "Developed by Adeeb", FontWeight = FontWeights.SemiBold, FontSize = 14 },
                },
                new TextBlock
                {
                    Text = "© 2026 Adeeb. Planora is free software. It uses MPXJ and MPXJ.Net (LGPL 2.1) to read Microsoft Project files, " +
                           "IKVM (MIT), and the Inter typeface (SIL Open Font License 1.1).",
                    Foreground = Muted,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 16, 0, 0),
                },
            },
        };
    }

    private void ShowExport()
    {
        _page.Content = new StackPanel
        {
            Children =
            {
                Title("Export"),
                new WrapPanel
                {
                    Children =
                    {
                        Tile("Export...", "Microsoft Project XML, Excel, CSV, MPX, Primavera P6 XML and XER.", () => Run(_vm.ExportCommand)),
                        Tile("Save as Template...", "Reuse this plan's structure for new projects.", () => Run(_vm.SaveAsTemplateCommand)),
                        Tile("PDF...", "The current view as a PDF document.", () => Run(_vm.ExportPdfCommand)),
                    },
                },
            },
        };
    }
}
