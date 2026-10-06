using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Planora.App.Controls;
using Drawing = Planora.App.Controls.Drawing;
using Planora.App.ViewModels;

namespace Planora.App.Output;

/// <summary>
/// Lays out the Gantt Chart view for printing and export: on each page, a title line, a task table
/// (ID, name, duration, start, finish) and the matching part of the chart, with the timescale repeated.
/// The timeline is scaled to fit the page width; rows continue onto further pages.
/// </summary>
public static class GanttPages
{
    private const double Margin = 36;
    private const double TitleHeight = 34;
    private const double RowHeight = 22;
    private const double HeaderHeight = 40;

    private static readonly (string Title, double Width)[] Columns =
    {
        ("ID", 34), ("Task Name", 230), ("Duration", 72), ("Start", 88), ("Finish", 88),
    };

    public static PageSet Build(MainViewModel vm, Size pageSize)
    {
        var rows = vm.Rows.Where(r => !r.IsPlaceholder).ToList();
        double tableWidth = Columns.Sum(c => c.Width);
        double chartWidth = Math.Max(200, pageSize.Width - 2 * Margin - tableWidth);
        double bodyHeight = pageSize.Height - 2 * Margin - TitleHeight;
        int rowsPerPage = Math.Max(1, (int)((bodyHeight - HeaderHeight) / RowHeight));
        int pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));

        var chart = CreateChart(vm, chartWidth, HeaderHeight + rowsPerPage * RowHeight);
        var pages = new List<Visual>();
        try
        {
            for (int page = 0; page < pageCount; page++)
            {
                int first = page * rowsPerPage;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    DrawTitle(dc, visual, vm, page + 1, pageCount, pageSize);
                    double top = Margin + TitleHeight;
                    DrawTable(dc, visual, rows.Skip(first).Take(rowsPerPage).ToList(), Margin, top);

                    chart.VerticalOffset = first * RowHeight;
                    chart.UpdateLayout();
                    var image = PageOutput.Render(chart, new Size(chartWidth, chart.Height), 192);
                    dc.DrawImage(image, new Rect(Margin + tableWidth, top, chartWidth, chart.Height));
                    dc.DrawRectangle(null, Drawing.GridPen, new Rect(Margin, top, tableWidth + chartWidth, chart.Height));
                }
                pages.Add(visual);
            }
        }
        finally
        {
            chart.ViewModel = null;
        }
        return new PageSet(pageSize, pages);
    }

    /// <summary>The whole Gantt (all rows) as a single image-sized visual, for PNG export.</summary>
    public static (Visual Visual, Size Size) BuildImage(MainViewModel vm)
    {
        var rows = vm.Rows.Where(r => !r.IsPlaceholder).ToList();
        double tableWidth = Columns.Sum(c => c.Width);
        var project = vm.Project;
        double days = Math.Max(1, (project.Finish - project.Start).TotalDays + 14);
        double chartWidth = Math.Clamp(days * vm.PixelsPerDay, 400, 6000);
        double height = HeaderHeight + rows.Count * RowHeight;
        var size = new Size(tableWidth + chartWidth + 2 * Margin, height + TitleHeight + 2 * Margin);

        var chart = CreateChart(vm, chartWidth, height);
        try
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawTitle(dc, visual, vm, 1, 1, size);
                DrawTable(dc, visual, rows, Margin, Margin + TitleHeight);
                chart.UpdateLayout();
                dc.DrawImage(PageOutput.Render(chart, new Size(chartWidth, height), 144), new Rect(Margin + tableWidth, Margin + TitleHeight, chartWidth, height));
            }
            return (visual, size);
        }
        finally
        {
            chart.ViewModel = null;
        }
    }

    private static GanttChart CreateChart(MainViewModel vm, double width, double height)
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
            FixedPixelsPerDay = width / Math.Max(1, (end - start).TotalDays),
            Width = width,
            Height = height,
        };
        chart.ViewModel = vm;
        chart.Measure(new Size(width, height));
        chart.Arrange(new Rect(0, 0, width, height));
        chart.UpdateLayout();
        return chart;
    }

    private static void DrawTitle(DrawingContext dc, Visual visual, MainViewModel vm, int page, int pages, Size pageSize)
    {
        dc.DrawText(Drawing.Text(visual, vm.Project.Name, 15, bold: true), new Point(Margin, Margin));
        var right = Drawing.Text(visual, $"{DateTime.Now.ToString("d", CultureInfo.CurrentCulture)}   Page {page} of {pages}", 10, Drawing.MutedText);
        dc.DrawText(right, new Point(pageSize.Width - Margin - right.Width, Margin + 4));
    }

    private static void DrawTable(DrawingContext dc, Visual visual, IReadOnlyList<TaskRowViewModel> rows, double left, double top)
    {
        double x = left;
        dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(left, top, Columns.Sum(c => c.Width), HeaderHeight));
        foreach (var (title, width) in Columns)
        {
            var text = Drawing.Text(visual, title, 10.5, bold: true);
            dc.DrawText(text, new Point(x + 4, top + HeaderHeight - text.Height - 4));
            dc.DrawLine(Drawing.GridPen, new Point(x, top), new Point(x, top + HeaderHeight + rows.Count * RowHeight));
            x += width;
        }
        dc.DrawLine(Drawing.GridPen, new Point(left, top + HeaderHeight), new Point(x, top + HeaderHeight));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            double y = top + HeaderHeight + i * RowHeight;
            if (row.IsGroupHeader)
                dc.DrawRectangle(Drawing.HeaderBackground, null, new Rect(left, y, Columns.Sum(c => c.Width), RowHeight));
            bool bold = row.IsSummary || row.IsGroupHeader;
            string[] values = { row.RowNumber, row.Name, row.Duration, row.Start, row.Finish };
            double cx = left;
            for (int c = 0; c < Columns.Length; c++)
            {
                double indent = c == 1 ? row.IndentWidth : 0;
                var text = Drawing.Text(visual, values[c], 10, bold: bold, maxWidth: Math.Max(1, Columns[c].Width - 8 - indent));
                dc.DrawText(text, new Point(cx + 4 + indent, y + (RowHeight - text.Height) / 2));
                cx += Columns[c].Width;
            }
            dc.DrawLine(Drawing.LightGridPen, new Point(left, y + RowHeight), new Point(cx, y + RowHeight));
        }
    }
}
