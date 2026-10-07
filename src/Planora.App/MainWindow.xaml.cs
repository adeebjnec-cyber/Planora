using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Planora.App.Controls;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.App.Views;

namespace Planora.App;

public partial class MainWindow : Window
{
    /// <summary>Views are created on first use and kept, so they keep their scroll position and layout.</summary>
    private readonly Dictionary<ViewKind, FrameworkElement> _views = new();

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ShowView(ViewModel.CurrentView);
        ViewModel.CurrentViewElement = () => ViewHost.Content as FrameworkElement;
        TimelinePane.Child = new DiagramView(new TimelineCanvas(), scroll: false) { DataContext = ViewModel };
        BuildBaselineMenu();
        UpdateIndicators();
        SizeChanged += (_, _) => FitCommandBar();
        PreviewKeyDown += OnWindowKeyDown;

        // The File menu opens a full-window page over everything.
        var main = (UIElement)Content;
        Content = null;
        _backstage = new Backstage(ViewModel) { Visibility = Visibility.Collapsed };
        _backstage.CloseRequested += (_, _) => _backstage.Visibility = Visibility.Collapsed;
        Content = new Grid { Children = { main, _backstage } };
    }

    private readonly Backstage _backstage;

    public MainViewModel ViewModel { get; }

    /// <summary>Shows the File page (also used by the --backstage screenshot option).</summary>
    public void ShowBackstage(string page = "Info")
    {
        _backstage.StartPage = page;
        _backstage.Visibility = Visibility.Visible;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentView):
                ShowView(ViewModel.CurrentView);
                break;
            case nameof(MainViewModel.IsDirty):
            case nameof(MainViewModel.WarningCount):
            case nameof(MainViewModel.ProjectStatusLevel):
            case nameof(MainViewModel.GanttBaselineIndex):
                UpdateIndicators();
                break;
        }
    }

    /// <summary>Save-state dots, the warnings badge and the project status dot.</summary>
    private void UpdateIndicators()
    {
        var saved = (Brush)FindResource(ViewModel.IsDirty ? "Warning" : "Success");
        SaveDot.Fill = SideSaveDot.Fill = saved;
        ProjectDot.Fill = (Brush)FindResource(ViewModel.ProjectStatusLevel == "green" ? "Success" : "TextFaint");
        BellBadge.Visibility = ViewModel.WarningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoWarnings.Visibility = ViewModel.WarningCount > 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in BaselineMenu.Items.OfType<MenuItem>())
            item.IsChecked = (int)item.Tag == ViewModel.GanttBaselineIndex;
    }

    /// <summary>Hides secondary button labels on narrower windows so the command bar stays on one line.</summary>
    private void FitCommandBar()
    {
        double width = ActualWidth;
        UpdateLabel.Visibility = MilestoneLabel.Visibility = width >= 1580 ? Visibility.Visible : Visibility.Collapsed;
        FilterLabel.Visibility = width >= 1660 ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Width = width >= 1500 ? 280 : 200;
    }

    private void BuildBaselineMenu()
    {
        for (int i = 0; i < MainViewModel.BaselineNames.Count; i++)
        {
            int index = i;
            var item = new MenuItem { Header = MainViewModel.BaselineNames[i], Tag = i };
            item.Click += (_, _) => ViewModel.GanttBaselineIndex = index;
            BaselineMenu.Items.Add(item);
        }
    }

    private void ShowView(ViewKind kind)
    {
        // The Tracking Gantt shares the Gantt Chart view (with baselines shown).
        var key = kind == ViewKind.TrackingGantt ? ViewKind.Gantt : kind;
        if (!_views.TryGetValue(key, out var view))
        {
            view = CreateView(key);
            _views[key] = view;
        }
        ViewHost.Content = view;
    }

    private static FrameworkElement CreateView(ViewKind kind) => kind switch
    {
        ViewKind.Gantt => new GanttView(),
        ViewKind.ResourceSheet => new ResourceSheetView(),
        ViewKind.TaskUsage => new UsageView(byResource: false),
        ViewKind.ResourceUsage => new UsageView(byResource: true),
        ViewKind.NetworkDiagram => new DiagramView(new NetworkDiagram()),
        ViewKind.Calendar => new DiagramView(new CalendarCanvas(), scroll: false),
        ViewKind.TaskBoard => new DiagramView(new TaskBoard()),
        ViewKind.Timeline => new DiagramView(new TimelineCanvas(), scroll: false),
        ViewKind.TaskSheet => new GanttView { ShowChart = false },
        ViewKind.ResourceGraph => new TimelineView(new ResourceGraph()),
        ViewKind.TeamPlanner => new TimelineView(new TeamPlanner()),
        ViewKind.RelationshipDiagram => new DiagramView(new RelationshipDiagram()),
        _ => new TextBlock
        {
            Text = "This view is being built.",
            Foreground = Brushes.Gray,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!ViewModel.ConfirmDiscard())
            e.Cancel = true;
    }

    // ------------------------------------------------------------------ command bar and menus

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenPalette();
            e.Handled = true;
        }
    }

    private void OnOpenPalette(object sender, RoutedEventArgs e) => OpenPalette();

    private void OpenPalette() => new CommandPalette(this, MainMenu).Show();

    private void OnFileMenu(object sender, RoutedEventArgs e) => ShowBackstage();

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutDialog { Owner = this }.ShowDialog();

    private void OnShowResourceSheet(object sender, RoutedEventArgs e) => ViewModel.CurrentView = ViewKind.ResourceSheet;

    private void OnShowResourceUsage(object sender, RoutedEventArgs e) => ViewModel.CurrentView = ViewKind.ResourceUsage;

    private void OnShowTeamPlanner(object sender, RoutedEventArgs e) => ViewModel.CurrentView = ViewKind.TeamPlanner;

    private void OnFilterMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FilterButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var name in ViewModel.FilterNames)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = name == ViewModel.CurrentFilterName };
            item.Click += (_, _) => ViewModel.CurrentFilterName = name;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Highlight matches instead of hiding others", IsCheckable = true, IsChecked = ViewModel.HighlightFilter });
        ((MenuItem)menu.Items[^1]).Click += (s, _) => ViewModel.HighlightFilter = ((MenuItem)s).IsChecked;
        menu.Items.Add(new MenuItem { Header = "New Filter...", Command = ViewModel.EditFilterCommand });
        menu.IsOpen = true;
    }

    private void OnAccountMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AccountButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Planora · Local workspace", IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Options...", Command = ViewModel.OptionsCommand });
        var themes = new MenuItem { Header = "Accent colour" };
        foreach (var theme in Themes.Names)
        {
            var item = new MenuItem { Header = theme, IsCheckable = true, IsChecked = theme == ViewModel.Theme || theme == Themes.Default && !Themes.Names.Contains(ViewModel.Theme) };
            item.Click += (_, _) => ViewModel.Theme = theme;
            themes.Items.Add(item);
        }
        menu.Items.Add(themes);
        var shortcuts = new MenuItem { Header = "Search Commands", InputGestureText = "Ctrl+K" };
        shortcuts.Click += (_, _) => OpenPalette();
        menu.Items.Add(shortcuts);
        menu.Items.Add(new Separator());
        var about = new MenuItem { Header = "About Planora" };
        about.Click += OnAbout;
        menu.Items.Add(about);
        menu.Items.Add(new MenuItem { Header = "Developed by Adeeb", IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>Opens the command search or a top-level menu (developer aid for screen captures).</summary>
    public void ShowForCapture(string what)
    {
        if (what.Equals("palette", StringComparison.OrdinalIgnoreCase))
        {
            OpenPalette();
            return;
        }
        if (MainMenu.Items.OfType<MenuItem>().FirstOrDefault(m => string.Equals(m.Header as string, what, StringComparison.OrdinalIgnoreCase)) is { } menu)
        {
            menu.Focus();
            menu.IsSubmenuOpen = true;
        }
    }

    /// <summary>Renders the window's content to a PNG (used for documentation and UI checks).</summary>
    public void SaveScreenshot(string path)
    {
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}
