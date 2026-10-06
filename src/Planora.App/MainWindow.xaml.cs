using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Planora.App.Controls;
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
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView))
                ShowView(ViewModel.CurrentView);
        };
        ShowView(ViewModel.CurrentView);
    }

    public MainViewModel ViewModel { get; }

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

    private void OnExit(object sender, RoutedEventArgs e) => Close();

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
