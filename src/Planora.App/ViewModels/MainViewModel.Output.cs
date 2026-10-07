using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Planora.App.Dialogs;
using Planora.App.Documents;
using Planora.App.Infrastructure;
using Planora.App.Output;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.App.ViewModels;

/// <summary>Import/export, printing, PDF and image output, clipboard, templates and recovery.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Everything Planora can import: its own files, Microsoft Project (.mpp and XML), spreadsheets and other schedulers.</summary>
    internal static readonly string ImportFilter =
        "All supported files|*.planora;*.mpp;*.mpt;*.xml;*.xlsx;*.csv;*.mpx;*.xer;*.pmxml;*.pp;*.ppx;*.gan;*.sdef;*.fts;*.pod;*.cdpx;*.schedule_grid" +
        "|Microsoft Project (*.mpp;*.mpt)|*.mpp;*.mpt|Microsoft Project XML (*.xml)|*.xml|Excel workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv" +
        "|Planora project (*.planora)|*.planora|" +
        string.Join("|", ForeignFormats.ReadableFormats.Skip(1).Select(f => $"{f.Name} ({f.Pattern})|{f.Pattern}"));

    private const string ExportFilter =
        "Microsoft Project XML (*.xml)|*.xml|Excel workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|MPX (*.mpx)|*.mpx" +
        "|Primavera P6 XML (*.xml)|*.xml|Primavera P6 XER (*.xer)|*.xer";

    /// <summary>Reads any supported file into a project (used by Open and Import).</summary>
    internal static Project LoadAnyProject(string path, ICollection<string>? warnings = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".planora" => ProjectFile.Load(path),
            ".xlsx" => SpreadsheetFile.Import(SpreadsheetFile.LoadXlsx(path), name),
            ".csv" => SpreadsheetFile.Import(SpreadsheetFile.LoadCsv(path), name),
            ".xml" => LoadXml(path, warnings),
            _ => ForeignFormats.Load(path, warnings),
        };

        // Microsoft Project XML is read directly; other XML formats (e.g. Primavera P6) go through MPXJ.
        static Project LoadXml(string path, ICollection<string>? warnings)
        {
            try
            {
                return MspdiFile.Load(path, warnings);
            }
            catch (InvalidDataException)
            {
                return ForeignFormats.Load(path, warnings);
            }
        }
    }

    /// <summary>Tells the user what an import repaired or skipped (shown once, after the file opens).</summary>
    internal void ShowImportNotes(string path, IReadOnlyCollection<string> notes)
    {
        if (notes.Count == 0)
            return;
        Status += $"  ·  {notes.Count} import note(s)";
        if (IsHeadless)
            return;
        var shown = notes.Take(20).Select(n => "• " + n).ToList();
        if (notes.Count > 20)
            shown.Add($"... and {notes.Count - 20} more.");
        MessageBox.Show(Application.Current.MainWindow,
            $"{Path.GetFileName(path)} opened, with these notes:{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, shown)}",
            "Import notes", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Supplies the element showing the current view (set by the main window), for printing non-Gantt views.</summary>
    public Func<FrameworkElement?>? CurrentViewElement { get; set; }

    public ICommand ImportCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    public ICommand PrintCommand { get; private set; } = null!;
    public ICommand ExportPdfCommand { get; private set; } = null!;
    public ICommand ExportImageCommand { get; private set; } = null!;
    public ICommand ReportsCommand { get; private set; } = null!;
    public ICommand CopyCommand { get; private set; } = null!;
    public ICommand CutCommand { get; private set; } = null!;
    public ICommand PasteCommand { get; private set; } = null!;
    public ICommand SaveAsTemplateCommand { get; private set; } = null!;
    public ICommand NewFromTemplateCommand { get; private set; } = null!;

    private bool IsGanttView => CurrentView is ViewKind.Gantt or ViewKind.TrackingGantt;

    private void InitializeOutputCommands()
    {
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export);
        PrintCommand = new RelayCommand(Print);
        ExportPdfCommand = new RelayCommand(ExportPdf);
        ExportImageCommand = new RelayCommand(ExportImage);
        ReportsCommand = new RelayCommand(name => new ReportWindow(this, name as string ?? "Project Overview") { Owner = Application.Current.MainWindow }.Show());
        CopyCommand = new RelayCommand(Copy, () => SelectedTasks.Count > 0);
        CutCommand = new RelayCommand(Cut, () => SelectedTasks.Count > 0);
        PasteCommand = new RelayCommand(Paste, () => Clipboard.ContainsData(TaskClipboard.Format) || Clipboard.ContainsText());
        SaveAsTemplateCommand = new RelayCommand(SaveAsTemplate);
        NewFromTemplateCommand = new RelayCommand(NewFromTemplate);
    }

    // ------------------------------------------------------------------ import / export

    private void Import()
    {
        if (!ConfirmDiscard())
            return;
        var dialog = new OpenFileDialog { Filter = ImportFilter };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var path = dialog.FileName;
            if (IsSpreadsheet(path))
            {
                ImportSpreadsheet(path);
                return;
            }
            var notes = new List<string>();
            var project = LoadAnyProject(path, notes);

            Document = new ProjectDocument(project);
            Status = $"Imported {Path.GetFileName(path)}";
            ShowImportNotes(path, notes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        {
            ShowError($"Could not import the file: {ex.Message}");
        }
    }

    internal static bool IsSpreadsheet(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".csv";

    /// <summary>Opens an Excel or CSV file through the Import Wizard (column-to-field mapping).</summary>
    internal void ImportSpreadsheet(string path)
    {
        try
        {
            var rows = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? SpreadsheetFile.LoadCsv(path) : SpreadsheetFile.LoadXlsx(path);
            if (rows.Count == 0)
            {
                ShowError("The file is empty.");
                return;
            }
            var wizard = new ImportMapDialog(rows, Path.GetFileName(path));
            if (wizard.ShowDialog() != true)
                return;
            var project = SpreadsheetFile.Import(wizard.MappedRows, Path.GetFileNameWithoutExtension(path));
            foreach (var (key, alias) in wizard.Aliases)
            {
                project.CustomFields.RemoveAll(c => c.Key == key);
                project.CustomFields.Add(new Engine.Fields.CustomField(key, alias));
            }
            Document = new ProjectDocument(project);
            DefinitionsChanged();
            Status = $"Imported {Path.GetFileName(path)}: {project.Tasks.Count} tasks";
            AddRecentFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        {
            ShowError($"Could not import the file: {ex.Message}");
        }
    }

    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = ExportFilter, FileName = Project.Name };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var path = dialog.FileName;
            var tasks = Rows.Where(r => r.Task is not null).Select(r => r.Task!).ToList();
            switch (dialog.FilterIndex)
            {
                case 4:
                    ForeignFormats.Save(Project, path, ForeignFormats.ExportFormat.Mpx);
                    break;
                case 5:
                    ForeignFormats.Save(Project, path, ForeignFormats.ExportFormat.PrimaveraXml);
                    break;
                case 6:
                    ForeignFormats.Save(Project, path, ForeignFormats.ExportFormat.PrimaveraXer);
                    break;
                case 2:
                    SpreadsheetFile.SaveXlsx(SpreadsheetFile.Rows(Project, CurrentTable, tasks), path);
                    break;
                case 3:
                    SpreadsheetFile.SaveCsv(SpreadsheetFile.Rows(Project, CurrentTable, tasks), path);
                    break;
                default:
                    MspdiFile.Save(Project, path);
                    break;
            }
            Status = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not export: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ print, PDF, image

    /// <summary>Pages for the current view: the Gantt layout, or the visible view scaled onto one page.</summary>
    /// <summary>Pages of the current view (the print preview uses the Page Setup paper size).</summary>
    internal PageSet BuildPages(Size pageSize)
    {
        if (IsGanttView || CurrentViewElement?.Invoke() is not { ActualWidth: > 0 } element)
            return GanttPages.Build(this, pageSize);

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            const double margin = 36;
            var title = Controls.Drawing.Text(visual, $"{Project.Name} - {CurrentViewOption.Title}", 15, bold: true);
            dc.DrawText(title, new Point(margin, margin));
            var area = new Rect(margin, margin + 34, pageSize.Width - 2 * margin, pageSize.Height - 2 * margin - 34);
            double scale = Math.Min(1, Math.Min(area.Width / element.ActualWidth, area.Height / element.ActualHeight));
            var image = PageOutput.Render(element, new Size(element.ActualWidth, element.ActualHeight), 192);
            dc.DrawImage(image, new Rect(area.X, area.Y, element.ActualWidth * scale, element.ActualHeight * scale));
        }
        return new PageSet(pageSize, new[] { visual });
    }

    private void Print()
    {
        try
        {
            PageOutput.Print(BuildPages, $"{Project.Name} - {CurrentViewOption.Title}", Project.PageSetup.Landscape);
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            ShowError($"Could not print: {ex.Message}");
        }
    }

    private void ExportPdf()
    {
        var dialog = new SaveFileDialog { Filter = "PDF document (*.pdf)|*.pdf", FileName = $"{Project.Name} - {CurrentViewOption.Title}.pdf" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            PageOutput.SavePdf(BuildPages(GanttPages.PageSize(this)), dialog.FileName, Project.Name);
            Status = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the PDF: {ex.Message}");
        }
    }

    private void ExportImage()
    {
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = $"{Project.Name} - {CurrentViewOption.Title}.png" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            if (IsGanttView)
            {
                var (visual, size) = GanttPages.BuildImage(this);
                PageOutput.SavePng(visual, size, dialog.FileName);
            }
            else if (CurrentViewElement?.Invoke() is { } element)
            {
                PageOutput.SavePng(element, new Size(element.ActualWidth, element.ActualHeight), dialog.FileName);
            }
            Status = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the image: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ clipboard

    private void Copy()
    {
        try
        {
            var data = new DataObject();
            data.SetData(TaskClipboard.Format, TaskClipboard.Copy(Project, SelectedTasks));
            data.SetText(TaskClipboard.CopyAsText(Project, SelectedTasks));
            Clipboard.SetDataObject(data, copy: true);
            Status = SelectedTasks.Count == 1 ? "Copied 1 task" : $"Copied {SelectedTasks.Count} tasks";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ShowError($"Could not use the clipboard: {ex.Message}");
        }
    }

    private void Cut()
    {
        Copy();
        DeleteTaskCommand.Execute(null);
    }

    private void Paste()
    {
        int index = SelectedTasks.Count > 0 ? Project.IndexOf(SelectedTasks[0]) : Project.Tasks.Count;
        try
        {
            if (Clipboard.GetData(TaskClipboard.Format) is string data)
                TryEdit("Paste", p => TaskClipboard.Paste(p, data, index));
            else if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                TryEdit("Paste", p => TaskClipboard.PasteText(p, text, index));
            }
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ShowError($"Could not use the clipboard: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ templates and recovery

    private void SaveAsTemplate()
    {
        Directory.CreateDirectory(TemplatePickerDialog.TemplatesFolder);
        var dialog = new SaveFileDialog
        {
            Filter = $"Planora template (*{ProjectTemplate.Extension})|*{ProjectTemplate.Extension}",
            InitialDirectory = TemplatePickerDialog.TemplatesFolder,
            FileName = Project.Name,
        };
        if (dialog.ShowDialog() != true)
            return;
        var keepBaselines = MessageBox.Show(Application.Current.MainWindow,
            "Templates normally leave out progress, actuals and baselines. Also remove resource rates?",
            "Save As Template", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (keepBaselines == MessageBoxResult.Cancel)
            return;
        try
        {
            ProjectTemplate.Save(Project, dialog.FileName, removeRates: keepBaselines == MessageBoxResult.Yes);
            Status = $"Saved template {Path.GetFileNameWithoutExtension(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the template: {ex.Message}");
        }
    }

    private void NewFromTemplate()
    {
        if (!ConfirmDiscard())
            return;
        var dialog = new TemplatePickerDialog();
        if (dialog.ShowDialog() == true && dialog.Result is { } project)
        {
            Document = new ProjectDocument(project);
            Status = $"New project from template: {project.Name}";
        }
    }

    /// <summary>Offers to reopen work autosaved before Planora last closed unexpectedly.</summary>
    public void OfferRecovery()
    {
        if (!Directory.Exists(ProjectDocument.AutosaveFolder))
            return;
        var files = Directory.GetFiles(ProjectDocument.AutosaveFolder, "*" + ProjectFile.Extension)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).ToList();
        if (files.Count == 0)
            return;
        var newest = files[0];
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"Planora found unsaved work from {newest.LastWriteTime:g}. Open it now?\n\n(Choose No to discard it.)",
            "Recover unsaved work", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return;
        try
        {
            if (answer == MessageBoxResult.Yes)
                Document = ProjectDocument.Recover(newest.FullName);
            foreach (var file in files)
                file.Delete();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowError($"Could not recover the file: {ex.Message}");
        }
    }

    /// <summary>When opening a file that has a newer autosave next to it, offers the recovered version.</summary>
    private string ChooseRecoveredVersion(string path)
    {
        var autosave = Path.Combine(Path.GetDirectoryName(path)!, "~" + Path.GetFileName(path) + ".autosave");
        if (!File.Exists(autosave) || File.GetLastWriteTime(autosave) <= File.GetLastWriteTime(path))
            return path;
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"There is a newer autosaved copy of {Path.GetFileName(path)} from {File.GetLastWriteTime(autosave):g}, probably from a session that did not close normally. Open the autosaved copy?",
            "Recover unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return answer == MessageBoxResult.Yes ? autosave : path;
    }
}
