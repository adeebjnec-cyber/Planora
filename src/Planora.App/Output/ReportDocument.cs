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
                case TimeChartBlock tc:
                    doc.Blocks.Add(new BlockUIContainer(new TimeChart(tc)) { Margin = new Thickness(0, 6, 0, 12) });
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

    /// <summary>
    /// A chart over time: column series drawn as bars, other series as lines (gaps where a value is
    /// unknown, e.g. actuals after the status date), with a dashed status-date line and a legend.
    /// </summary>
    private sealed class TimeChart : FrameworkElement
    {
        private static readonly Brush[] Palette =
        {
            new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)), new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x39)),
            new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0x5B)), new SolidColorBrush(Color.FromRgb(0x9A, 0x4F, 0xA8)),
        };

        private const double Left = 70, Right = 16, Top = 28, Bottom = 46;
        private readonly TimeChartBlock _block;

        public TimeChart(TimeChartBlock block)
        {
            _block = block;
            Height = 300;
            MinWidth = 420;
        }

        private double _max;

        private string Format(double value) => _block.ValueFormat switch
        {
            "money" => value.ToString(_max >= 1000 ? "C0" : "C", System.Globalization.CultureInfo.CurrentCulture),
            "hours" => value.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "h",
            _ => value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture),
        };

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawText(Drawing.Text(this, _block.Title, 13, bold: true), new Point(0, 0));
            int n = _block.Periods.Count;
            if (n == 0)
                return;
            double plotWidth = Math.Max(50, ActualWidth - Left - Right);
            double plotHeight = Height - Top - Bottom;
            double max = _block.Series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
            max = max <= 0 ? 1 : NiceCeiling(max);
            _max = max;
            double slot = plotWidth / n;
            double X(int i) => Left + slot * (i + 0.5);
            double Y(double v) => Top + plotHeight * (1 - v / max);

            for (int t = 0; t <= 4; t++)
            {
                double v = max * t / 4, y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(Drawing.LightGridPen, new Point(Left, y), new Point(Left + plotWidth, y));
                var label = Drawing.Text(this, Format(v), 10, Drawing.MutedText);
                dc.DrawText(label, new Point(Left - 6 - label.Width, y - label.Height / 2));
            }
            dc.DrawLine(Drawing.GridPen, new Point(Left, Top + plotHeight + 0.5), new Point(Left + plotWidth, Top + plotHeight + 0.5));

            int every = Math.Max(1, (int)Math.Ceiling(n / (plotWidth / 52)));
            for (int i = 0; i < n; i += every)
            {
                var label = Drawing.Text(this, _block.PeriodLabels[i], 10, Drawing.MutedText);
                dc.DrawText(label, new Point(X(i) - label.Width / 2, Top + plotHeight + 4));
            }

            var columns = _block.Series.Where(s => s.AsColumns).ToList();
            for (int c = 0; c < columns.Count; c++)
            {
                var brush = Palette[_block.Series.ToList().IndexOf(columns[c]) % Palette.Length];
                double width = Math.Max(1, slot * 0.7 / columns.Count);
                for (int i = 0; i < n && i < columns[c].Values.Count; i++)
                {
                    double v = columns[c].Values[i];
                    if (double.IsNaN(v) || v <= 0)
                        continue;
                    double x = X(i) - slot * 0.35 + c * width;
                    dc.DrawRectangle(brush, null, new Rect(x, Y(v), width, Top + plotHeight - Y(v)));
                }
            }
            for (int s = 0; s < _block.Series.Count; s++)
            {
                var series = _block.Series[s];
                if (series.AsColumns)
                    continue;
                var pen = new Pen(Palette[s % Palette.Length], 2);
                Point? previous = null;
                for (int i = 0; i < n && i < series.Values.Count; i++)
                {
                    double v = series.Values[i];
                    if (double.IsNaN(v))
                    {
                        previous = null;
                        continue;
                    }
                    var point = new Point(X(i), Y(v));
                    if (previous is { } p)
                        dc.DrawLine(pen, p, point);
                    else
                        dc.DrawEllipse(pen.Brush, null, point, 2, 2);
                    previous = point;
                }
            }

            if (_block.StatusDate is { } status && status >= _block.Periods[0])
            {
                int index = _block.Periods.Count(p => p <= status) - 1;
                if (index >= 0 && index < n)
                {
                    double x = Left + slot * index + slot * Math.Clamp((status - _block.Periods[index]).TotalDays / Math.Max(1, ((index + 1 < n ? _block.Periods[index + 1] : _block.Periods[index].AddDays(7)) - _block.Periods[index]).TotalDays), 0, 1);
                    var dashed = new Pen(Brushes.MediumPurple, 1) { DashStyle = DashStyles.Dash };
                    dc.DrawLine(dashed, new Point(x, Top), new Point(x, Top + plotHeight));
                    dc.DrawText(Drawing.Text(this, "Status date", 9.5, Brushes.MediumPurple), new Point(x + 3, Top));
                }
            }

            double lx = Left;
            for (int s = 0; s < _block.Series.Count; s++)
            {
                var brush = Palette[s % Palette.Length];
                double ly = Height - 16;
                if (_block.Series[s].AsColumns)
                    dc.DrawRectangle(brush, null, new Rect(lx, ly + 1, 12, 10));
                else
                    dc.DrawRectangle(brush, null, new Rect(lx, ly + 5, 14, 2.5));
                var label = Drawing.Text(this, _block.Series[s].Name, 10.5);
                dc.DrawText(label, new Point(lx + 18, ly - 1));
                lx += 30 + label.Width;
            }
        }

        /// <summary>A round axis maximum just above the value (1, 2, 2.5 or 5 times a power of ten).</summary>
        private static double NiceCeiling(double value)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (double step in new[] { 1, 2, 2.5, 5, 10 })
            {
                if (step * magnitude >= value)
                    return step * magnitude;
            }
            return 10 * magnitude;
        }
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
