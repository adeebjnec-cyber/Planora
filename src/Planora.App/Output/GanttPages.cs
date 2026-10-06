using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Planora.App.Controls;
using Drawing = Planora.App.Controls.Drawing;
using Planora.App.ViewModels;
using Planora.Engine.Fields;
using Planora.Engine.Views;

namespace Planora.App.Output;

/// <summary>
/// Lays out the Gantt Chart view for printing and export, following the project's Page Setup: header and
/// footer text, the current table's columns, the matching part of the chart with the timescale repeated,
/// and an optional legend. The timeline is scaled to fit the page width; rows continue onto further pages.
/// </summary>
public static class GanttPages
{
    private const double RowHeight = 22;
    private const double HeaderHeight = 40;
    private const double BandHeight = 24;
    private const double LegendHeight = 40;

    /// <summary>A printed column: its field key, title and width.</summary>
    private sealed record PrintColumn(string Key, string Title, double Width);

    /// <summary>The page size from Page Setup (paper and orientation), in device-independent pixels.</summary>
    public static Size PageSize(MainViewModel vm)
    {
        var (w, h) = vm.Project.PageSetup.PageSizeDips();
        return new Size(w, h);
    }

    public static PageSet Build(MainViewModel vm, Size pageSize)
    {
        var setup = vm.Project.PageSetup;
        double margin = setup.MarginDips;
        var rows = vm.Rows.Where(r => !r.IsPlaceholder).ToList();
        bool hasHeader = new[] { setup.HeaderLeft, setup.HeaderCenter, setup.HeaderRight }.Any(t => !string.IsNullOrWhiteSpace(t));
        bool hasFooter = new[] { setup.FooterLeft, setup.FooterCenter, setup.FooterRight }.Any(t => !string.IsNullOrWhiteSpace(t));
        double headerBand = hasHeader ? BandHeight + 10 : 0;
        double footerBand = hasFooter ? BandHeight : 0;
        double legendBand = setup.Legend == LegendPlacement.EveryPage ? LegendHeight + 6 : 0;

        var columns = Columns(vm, pageSize.Width - 2 * margin);
        double tableWidth = columns.Sum(c => c.Width);
        double chartWidth = Math.Max(200, pageSize.Width - 2 * margin - tableWidth);
        double bodyHeight = pageSize.Height - 2 * margin - headerBand - footerBand - legendBand;
        int rowsPerPage = Math.Max(1, (int)((bodyHeight - HeaderHeight) / RowHeight));
        int pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));

        var pages = new List<Visual>();
        for (int page = 0; page < pageCount; page++)
        {
            int first = page * rowsPerPage;
            double top = margin + headerBand;
            var pageRows = rows.Skip(first).Take(rowsPerPage).ToList();
            double chartHeight = HeaderHeight + rowsPerPage * RowHeight;

            // The chart is part of the page (not a picture of it), so its text stays selectable in PDFs.
            var container = new ContainerVisual();
            var frame = new DrawingVisual();
            using (var dc = frame.RenderOpen())
            {
                if (hasHeader)
                    DrawBand(dc, frame, vm, setup.HeaderLeft, setup.HeaderCenter, setup.HeaderRight, page + 1, pageCount, pageSize, margin, margin, title: true);
                DrawTable(dc, frame, vm, columns, pageRows, margin, top);
                dc.DrawRectangle(null, Drawing.GridPen, new Rect(margin, top, tableWidth + chartWidth, chartHeight));
                if (legendBand > 0)
                    DrawLegend(dc, frame, vm, new Rect(margin, top + chartHeight + 6, pageSize.Width - 2 * margin, LegendHeight));
                if (hasFooter)
                    DrawBand(dc, frame, vm, setup.FooterLeft, setup.FooterCenter, setup.FooterRight, page + 1, pageCount, pageSize, margin, pageSize.Height - margin - BandHeight + 4, title: false);
            }
            container.Children.Add(frame);
            container.Children.Add(CreateChart(vm, new Rect(margin + tableWidth, top, chartWidth, chartHeight), first * RowHeight));
            pages.Add(container);
        }
        return new PageSet(pageSize, pages);
    }

    /// <summary>The whole Gantt (all rows) as a single image-sized visual, for PNG export.</summary>
    public static (Visual Visual, Size Size) BuildImage(MainViewModel vm)
    {
        const double margin = 36;
        var rows = vm.Rows.Where(r => !r.IsPlaceholder).ToList();
        var columns = Columns(vm, 1600);
        double tableWidth = columns.Sum(c => c.Width);
        var project = vm.Project;
        double days = Math.Max(1, (project.Finish - project.Start).TotalDays + 14);
        double chartWidth = Math.Clamp(days * vm.PixelsPerDay, 400, 6000);
        double height = HeaderHeight + rows.Count * RowHeight;
        var size = new Size(tableWidth + chartWidth + 2 * margin, height + BandHeight + 10 + 2 * margin);

        var container = new ContainerVisual();
        var frame = new DrawingVisual();
        using (var dc = frame.RenderOpen())
        {
            var setup = project.PageSetup;
            DrawBand(dc, frame, vm, setup.HeaderLeft, setup.HeaderCenter, setup.HeaderRight, 1, 1, size, margin, margin, title: true);
            DrawTable(dc, frame, vm, columns, rows, margin, margin + BandHeight + 10);
        }
        container.Children.Add(frame);
        container.Children.Add(CreateChart(vm, new Rect(margin + tableWidth, margin + BandHeight + 10, chartWidth, height), 0));
        return (container, size);
    }

    /// <summary>
    /// The current table's columns (as many as Page Setup asks for, or as fit in half the page), with the
    /// row number first.
    /// </summary>
    private static List<PrintColumn> Columns(MainViewModel vm, double pageWidth)
    {
        var project = vm.Project;
        var result = new List<PrintColumn> { new("#", "ID", 34) };
        int wanted = project.PageSetup.SheetColumns;
        double limit = wanted > 0 ? double.MaxValue : Math.Max(300, pageWidth * 0.5);
        foreach (var column in vm.CurrentTable.Columns)
        {
            if (TaskFields.Find(project, column.FieldKey) is not { } field)
                continue;
            if (wanted > 0 && result.Count - 1 >= wanted)
                break;
            double width = Math.Clamp(column.Width, 40, field.Key == "Name" ? 260 : 140);
            if (wanted == 0 && result.Sum(c => c.Width) + width > limit && result.Count > 1)
                break;
            result.Add(new PrintColumn(field.Key, column.Title ?? field.Name, width));
        }
        if (result.All(c => c.Key != "Name"))
            result.Insert(1, new PrintColumn("Name", "Task Name", 230));
        return result;
    }

    /// <summary>A Gantt chart laid out at a position on a page, showing rows from <paramref name="verticalOffset"/>.</summary>
    private static GanttChart CreateChart(MainViewModel vm, Rect bounds, double verticalOffset)
    {
        var project = vm.Project;
        var start = (project.Tasks.Count > 0 ? project.Start : project.StartDate).Date.AddDays(-2);
        // Leave room after the finish for labels (resource names, milestone dates) beside the last bars.
        var finish = (project.Tasks.Count > 0 ? project.Finish : project.StartDate).Date;
        var end = finish.AddDays(Math.Max(5, (finish - start).TotalDays * 0.18));
        var chart = new GanttChart
        {
            RowHeight = RowHeight,
            HeaderHeight = HeaderHeight,
            ShowSelection = false,
            FixedRange = (start, end),
            FixedPixelsPerDay = bounds.Width / Math.Max(1, (end - start).TotalDays),
            Width = bounds.Width,
            Height = bounds.Height,
        };
        chart.ViewModel = vm;
        chart.VerticalOffset = verticalOffset;
        chart.Measure(bounds.Size);
        chart.Arrange(bounds);
        chart.UpdateLayout();
        // A printed page is a snapshot: stop following later edits.
        chart.DetachFromViewModel();
        return chart;
    }

    /// <summary>A header or footer line: left, centred and right text, with codes filled in.</summary>
    private static void DrawBand(DrawingContext dc, Visual visual, MainViewModel vm, string left, string center, string right,
        int page, int pages, Size pageSize, double margin, double y, bool title)
    {
        string Expand(string text) => PageSetup.Expand(text, vm.Project, page, pages, vm.CurrentViewOption.Title);
        double width = pageSize.Width - 2 * margin;
        var l = Drawing.Text(visual, Expand(left), title ? 15 : 10, title ? Drawing.TextBrush : Drawing.MutedText, bold: title, maxWidth: width / 2);
        dc.DrawText(l, new Point(margin, y));
        var c = Drawing.Text(visual, Expand(center), 10, Drawing.MutedText, maxWidth: width / 3);
        dc.DrawText(c, new Point(margin + (width - c.Width) / 2, y + (title ? 4 : 0)));
        var r = Drawing.Text(visual, Expand(right), 10, Drawing.MutedText, maxWidth: width / 2);
        dc.DrawText(r, new Point(pageSize.Width - margin - r.Width, y + (title ? 4 : 0)));
    }

    /// <summary>The bar legend: a sample of each bar style used on the chart.</summary>
    private static void DrawLegend(DrawingContext dc, Visual visual, MainViewModel vm, Rect area)
    {
        dc.DrawRectangle(null, Drawing.GridPen, area);
        var format = vm.Project.GanttFormat;
        var kinds = new List<BarKind> { BarKind.Task, BarKind.CriticalTask, BarKind.Progress, BarKind.Milestone, BarKind.Summary, BarKind.ManualTask };
        if (vm.ShowBaseline)
            kinds.Add(BarKind.Baseline);
        double x = area.X + 10, centerY = area.Y + area.Height / 2;
        foreach (var kind in kinds)
        {
            var style = format.BarStyles.LastOrDefault(s => s.Kind == kind && s.FlagField is null);
            if (style is null || style.Shape == BarShape.Hidden)
                continue;
            var fill = Brush(style.Fill);
            var border = new Pen(Brush(style.Border), 1);
            if (kind == BarKind.Milestone)
                Drawing.Diamond(dc, new Point(x + 15, centerY), 6, fill);
            else if (kind is BarKind.Summary or BarKind.Progress or BarKind.Baseline)
                dc.DrawRectangle(fill, null, new Rect(x, centerY - 2.5, 30, 5));
            else
                dc.DrawRectangle(fill, border, new Rect(x, centerY - 6, 30, 12));
            var label = Drawing.Text(visual, style.Name, 10);
            dc.DrawText(label, new Point(x + 36, centerY - label.Height / 2));
            x += 50 + label.Width;
            if (x > area.Right - 80)
                break;
        }
    }

    private static Brush Brush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Drawing.TaskFill;
        }
    }

    private static void DrawTable(DrawingContext dc, Visual visual, MainViewModel vm, IReadOnlyList<PrintColumn> columns, IReadOnlyList<TaskRowViewModel> rows, double left, double top)
    {
        double x = left;
        double total = columns.Sum(c => c.Width);
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(left, top, total, HeaderHeight));
        foreach (var column in columns)
        {
            var text = Drawing.Text(visual, column.Title, 10.5, bold: true, maxWidth: Math.Max(1, column.Width - 6));
            dc.DrawText(text, new Point(x + 4, top + HeaderHeight - text.Height - 4));
            dc.DrawLine(Drawing.GridPen, new Point(x, top), new Point(x, top + HeaderHeight + rows.Count * RowHeight));
            x += column.Width;
        }
        dc.DrawLine(Drawing.GridPen, new Point(left, top + HeaderHeight), new Point(x, top + HeaderHeight));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            double y = top + HeaderHeight + i * RowHeight;
            if (row.IsGroupHeader)
                dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(left, y, total, RowHeight));
            bool bold = row.IsSummary || row.IsGroupHeader;
            double cx = left;
            foreach (var column in columns)
            {
                string value = column.Key switch
                {
                    "#" => row.RowNumber,
                    "Name" => row.Name,
                    _ => row[column.Key],
                };
                double indent = column.Key == "Name" ? row.IndentWidth : 0;
                var text = Drawing.Text(visual, value, 10, bold: bold, maxWidth: Math.Max(1, column.Width - 8 - indent));
                dc.DrawText(text, new Point(cx + 4 + indent, y + (RowHeight - text.Height) / 2));
                cx += column.Width;
            }
            dc.DrawLine(Drawing.LightGridPen, new Point(left, y + RowHeight), new Point(cx, y + RowHeight));
        }
    }
}
