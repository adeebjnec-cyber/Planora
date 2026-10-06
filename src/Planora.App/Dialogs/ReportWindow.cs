using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Planora.App.Output;
using Planora.App.ViewModels;
using Planora.Engine.Reports;

namespace Planora.App.Dialogs;

/// <summary>Shows a report, with printing and PDF export.</summary>
public sealed class ReportWindow : Window
{
    /// <summary>A4 portrait in device-independent pixels.</summary>
    private static readonly Size A4Portrait = new(793.7, 1122.5);

    private readonly MainViewModel _owner;
    private readonly ComboBox _picker;
    private readonly FlowDocumentScrollViewer _viewer;

    public ReportWindow(MainViewModel owner, string reportName)
    {
        _owner = owner;
        Title = "Reports";
        Width = 960;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        _picker = new ComboBox { ItemsSource = ReportCatalog.AllNames(owner.Project), SelectedItem = reportName, Width = 220, Margin = new Thickness(0, 0, 10, 0) };
        var print = new Button { Content = "Print...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        var pdf = new Button { Content = "Export PDF...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 18, 0) };
        var newReport = new Button { Content = "New Report...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        _edit = new Button { Content = "Edit Report...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
        _delete = new Button { Content = "Delete", Padding = new Thickness(12, 3, 12, 3) };
        newReport.Click += (_, _) => Design(null);
        _edit.Click += (_, _) => Design(CurrentCustom);
        _delete.Click += (_, _) => DeleteReport();
        _viewer = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false };

        _picker.SelectionChanged += (_, _) => ShowReport();
        print.Click += (_, _) => PageOutput.Print(size => PageOutput.Paginate(ReportDocument.Build(Build()), size), CurrentName);
        pdf.Click += (_, _) => ExportPdf();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), Children = { new TextBlock { Text = "Report:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _picker, print, pdf, newReport, _edit, _delete } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_viewer);
        Content = root;
        ShowReport();
    }

    private readonly Button _edit;
    private readonly Button _delete;

    private string CurrentName => _picker.SelectedItem as string ?? ReportCatalog.Names[0];

    /// <summary>The selected report if it is one of the project's own.</summary>
    private CustomReport? CurrentCustom =>
        ReportCatalog.Names.Contains(CurrentName) ? null : _owner.Project.CustomReports.FirstOrDefault(r => r.Name == CurrentName);

    private void Design(CustomReport? report)
    {
        var dialog = new ReportDesignerDialog(_owner, report) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _picker.ItemsSource = ReportCatalog.AllNames(_owner.Project);
        _picker.SelectedItem = dialog.ReportName;
        ShowReport();
    }

    private void DeleteReport()
    {
        if (CurrentCustom is not { } report || MessageBox.Show(this, $"Delete the report '{report.Name}'?", "Planora", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        var name = report.Name;
        if (_owner.TryEdit("Delete report", p => p.CustomReports.RemoveAll(r => r.Name == name)))
        {
            _picker.ItemsSource = ReportCatalog.AllNames(_owner.Project);
            _picker.SelectedIndex = 0;
        }
    }

    private Report Build() => ReportCatalog.Build(_owner.Project, CurrentName);

    private void ShowReport()
    {
        _viewer.Document = ReportDocument.Build(Build());
        _edit.IsEnabled = _delete.IsEnabled = CurrentCustom is not null;
    }

    private void ExportPdf()
    {
        var dialog = new SaveFileDialog { Filter = "PDF document (*.pdf)|*.pdf", FileName = $"{_owner.Project.Name} - {CurrentName}.pdf" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            PageOutput.SavePdf(PageOutput.Paginate(ReportDocument.Build(Build()), A4Portrait), dialog.FileName, CurrentName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the PDF: {ex.Message}", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
