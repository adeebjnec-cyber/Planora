using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Planora.App.ViewModels;

namespace Planora.App.Dialogs;

/// <summary>Creates dialogs by name for the --screenshot developer mode.</summary>
internal static class DialogFactory
{
    public static Window ForScreenshot(MainViewModel vm, string name)
    {
        var task = vm.Project.Tasks.FirstOrDefault(t => !t.IsSummary && t.Assignments.Count > 0 && t.Predecessors.Count > 0)
                   ?? vm.Project.Tasks.First();
        var resource = vm.Project.Resources.First();
        return name.ToLowerInvariant() switch
        {
            "task" => new TaskInformationDialog(vm, task),
            "resource" => new ResourceInformationDialog(vm, resource),
            "project" => new ProjectInformationDialog(vm),
            "calendar" => new ChangeWorkingTimeDialog(vm),
            "baseline" => new BaselineDialog(vm, clear: false),
            "level" => new LevelingDialog(vm),
            "sort" => new SortDialog(vm),
            "fields" => new CustomFieldsDialog(vm),
            "tableedit" => new TableEditorDialog(vm),
            "filteredit" => new FilterEditorDialog(vm),
            "groupedit" => new GroupEditorDialog(vm),
            "recurring" => new RecurringTaskDialog(vm),
            "split" => new SplitTaskDialog(vm, task),
            "options" => new OptionsDialog(vm),
            "budgets" => new BudgetDialog(vm),
            "assignment" => new AssignmentInformationDialog(vm, task.Assignments.First()),
            "updatetasks" => new UpdateTasksDialog(vm),
            "updateproject" => new UpdateProjectDialog(vm),
            "replace" => new FindReplaceDialog(vm, replace: true),
            "spelling" => new SpellingDialog(vm),
            "barstyles" => new BarStylesDialog(vm),
            "textstyles" => new TextStylesDialog(vm),
            "timescale" => new TimescaleDialog(vm),
            "gridlines" => new GridlinesDialog(vm),
            "reportdesigner" => new ReportDesignerDialog(vm, null),
            "macros" => new MacrosDialog(vm),
            "pagesetup" => new PageSetupDialog(vm),
            "importmap" => new ImportMapDialog(new List<string[]>
            {
                new[] { "Activity", "Days", "Assigned To", "Department", "Depends On", "Region" },
                new[] { "Survey site", "3", "Asha", "Facilities", "", "North" },
                new[] { "Draw plans", "5", "Ravi", "Design", "1", "North" },
                new[] { "Approve plans", "1", "Asha", "Facilities", "2", "South" },
            }, "plan.xlsx"),
            _ => throw new ArgumentException($"Unknown dialog '{name}'."),
        };
    }

    public static void SaveScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
