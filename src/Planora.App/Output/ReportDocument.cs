using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Planora.App.Controls;
using Drawing = Planora.App.Controls.Drawing;
using Planora.Engine.Reports;

namespace Planora.App.Output;

/// <summary>Lays out an engine <see cref="Report"/> as a flow document for viewing, printing and PDF.</summary>
public static class ReportDocument
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
    private static readonly Brush AlertBackground = new SolidColorBrush(Color.FromRgb(0xFD, 0xE8, 0xE6));
    private static readonly Brush HeaderBackground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));

    public static FlowDocument Build(Report report)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.White,
            TextAlignment = TextAlignment.Left,
        };
        doc.Blocks.Add(new Paragraph(new Run(report.Title)) { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Accent, Margin = new Thickness(0) });
        doc.Blocks.Add(new Paragraph(new Run(report.Subtitle)) { Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 16) });

        foreach (var block in report.Blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                    doc.Blocks.Add(new Paragraph(new Run(h.Text)) { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Accent, Margin = new Thickness(0, 14, 0, 6), KeepWithNext = true });
                    break;
                case ParagraphBlock p:
                    doc.Blocks.Add(new Paragraph(new Run(p.Text)) { Margin = new Thickness(0, 0, 0, 8) });
                    break;
                case FiguresBlock f:
                    doc.Blocks.Add(Figures(f));
                    break;
                case TableBlock t:
                    doc.Blocks.Add(Table(t));
                    break;
                case BarChartBlock c:
                    doc.Blocks.Add(new BlockUIContainer(new BarChart(c)) { Margin = new Thickness(0, 6, 0, 12) });
                    break;
            }
        }
        return doc;
    }

    private static Table Figures(FiguresBlock block)
    {
        const int PerRow = 4;
        var table = new Table { CellSpacing = 6, Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < PerRow; i++)
            table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (int i = 0; i < block.Items.Count; i += PerRow)
        {
            var row = new TableRow();
            foreach (var (label, value) in block.Items.Skip(i).Take(PerRow))
            {
                var cell = new TableCell { Background = HeaderBackground, Padding = new Thickness(10, 6, 10, 8) };
                cell.Blocks.Add(new Paragraph(new Run(label)) { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0) });
                cell.Blocks.Add(new Paragraph(new Run(value)) { FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        return table;
    }

    private static Table Table(TableBlock block)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < block.Columns.Count; i++)
            table.Columns.Add(new TableColumn { Width = i == 0 ? new GridLength(2.2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });

        var header = new TableRowGroup();
        var headerRow = new TableRow { Background = HeaderBackground, FontWeight = FontWeights.SemiBold };
        foreach (var column in block.Columns)
            headerRow.Cells.Add(Cell(column));
        header.Rows.Add(headerRow);
        table.RowGroups.Add(header);

        var body = new TableRowGroup();
        for (int r = 0; r < block.Rows.Count; r++)
        {
            var row = new TableRow();
            if (block.IsAlert(r))
                row.Background = AlertBackground;
            foreach (var value in block.Rows[r])
                row.Cells.Add(Cell(value));
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        return table;

        static TableCell Cell(string text) => new(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>Horizontal bar chart with value labels; the optional second series is a thin bar underneath.</summary>
    private sealed class BarChart : FrameworkElement
    {
        private const double BarHeight = 18;
        private const double Gap = 10;
        private const double LabelWidth = 190;
        private readonly BarChartBlock _block;

        public BarChart(BarChartBlock block)
        {
            _block = block;
            Height = 34 + block.Bars.Count * (BarHeight + Gap) + (block.SecondSeriesName is null ? 0 : 20);
            MinWidth = 400;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawText(Drawing.Text(this, _block.Title, 13, bold: true), new Point(0, 0));
            double max = _block.Maximum ?? Math.Max(1e-9, _block.Bars.Select(b => Math.Max(b.Value, b.Second ?? 0)).DefaultIfEmpty(1).Max());
            double plotWidth = Math.Max(50, ActualWidth - LabelWidth - 90);
            double y = 26;
            foreach (var bar in _block.Bars)
            {
                dc.DrawText(Drawing.Text(this, bar.Label, 11, maxWidth: LabelWidth - 8), new Point(0, y + 1));
                double width = plotWidth * Math.Max(0, bar.Value) / max;
                dc.DrawRectangle(Accent, null, new Rect(LabelWidth, y, Math.Max(1, width), BarHeight - (bar.Second is null ? 0 : 6)));
                if (bar.Second is { } second)
                    dc.DrawRectangle(Brushes.DarkGray, null, new Rect(LabelWidth, y + BarHeight - 5, Math.Max(1, plotWidth * Math.Max(0, second) / max), 4));
                dc.DrawText(Drawing.Text(this, bar.ValueText, 11, Drawing.MutedText), new Point(LabelWidth + width + 6, y + 1));
                y += BarHeight + Gap;
            }
            if (_block.SecondSeriesName is { } secondName)
            {
                dc.DrawRectangle(Accent, null, new Rect(LabelWidth, y + 4, 12, 10));
                dc.DrawText(Drawing.Text(this, _block.SeriesName, 10.5), new Point(LabelWidth + 16, y + 2));
                dc.DrawRectangle(Brushes.DarkGray, null, new Rect(LabelWidth + 120, y + 7, 12, 4));
                dc.DrawText(Drawing.Text(this, secondName, 10.5), new Point(LabelWidth + 136, y + 2));
            }
        }
    }
}
