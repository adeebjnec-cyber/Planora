namespace Planora.Engine.Reports;

/// <summary>A report: a title and a sequence of blocks, laid out by the application for screen, print and PDF.</summary>
public sealed record Report(string Title, string Subtitle, IReadOnlyList<ReportBlock> Blocks);

public abstract record ReportBlock;

public sealed record HeadingBlock(string Text) : ReportBlock;

public sealed record ParagraphBlock(string Text) : ReportBlock;

/// <summary>Key figures shown as label/value pairs (e.g. Start, Finish, Cost).</summary>
public sealed record FiguresBlock(IReadOnlyList<(string Label, string Value)> Items) : ReportBlock;

/// <summary>A table. Rows flagged in <see cref="Alerts"/> are highlighted (late, over budget, over-allocated).</summary>
public sealed record TableBlock(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<bool>? Alerts = null) : ReportBlock
{
    public bool IsAlert(int row) => Alerts is not null && row < Alerts.Count && Alerts[row];
}

/// <summary>A horizontal bar chart; an optional second series is drawn as a thinner bar (e.g. baseline).</summary>
public sealed record BarChartBlock(
    string Title,
    IReadOnlyList<ChartBar> Bars,
    string SeriesName,
    string? SecondSeriesName = null,
    double? Maximum = null) : ReportBlock;

public sealed record ChartBar(string Label, double Value, string ValueText, double? Second = null);
