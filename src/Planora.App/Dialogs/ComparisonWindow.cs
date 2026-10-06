using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Planora.App.Output;
using Planora.Engine.Reports;

namespace Planora.App.Dialogs;

/// <summary>Shows a ready-made report (e.g. Compare Projects) with printing and PDF export.</summary>
public sealed class ComparisonWindow : Window
{
    private static readonly Size A4Portrait = new(793.7, 1122.5);

    public ComparisonWindow(Report report)
    {
        Title = report.Title;
        Width = 960;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var viewer = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false, Document = ReportDocument.Build(report) };
        var print = new Button { Content = "Print...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        var pdf = new Button { Content = "Export PDF...", Padding = new Thickness(12, 3, 12, 3) };
        print.Click += (_, _) => PageOutput.Print(size => PageOutput.Paginate(ReportDocument.Build(report), size), report.Title, landscape: false);
        pdf.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "PDF document (*.pdf)|*.pdf", FileName = report.Title + ".pdf" };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                PageOutput.SavePdf(PageOutput.Paginate(ReportDocument.Build(report), A4Portrait), dialog.FileName, report.Title);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Could not save the PDF: {ex.Message}", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), Children = { print, pdf } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(viewer);
        Content = root;
    }
}
