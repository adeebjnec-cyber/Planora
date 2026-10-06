using System.Globalization;
using Planora.Engine.Model;

namespace Planora.Engine.Views;

public enum PaperSize
{
    A4,
    Letter,
    Legal,
    A3,
}

public enum LegendPlacement
{
    None,
    EveryPage,
}

/// <summary>
/// Page Setup (File > Print > Page Setup), saved with the project: paper, orientation, margins, header
/// and footer text, the legend, and how many sheet columns to print. Header and footer text can use
/// codes: &amp;[Page], &amp;[Pages], &amp;[Date], &amp;[Time], &amp;[Project Title], &amp;[Project Start],
/// &amp;[Project Finish], &amp;[View], &amp;[Status Date].
/// </summary>
public sealed class PageSetup
{
    public PaperSize Paper { get; set; } = PaperSize.A4;
    public bool Landscape { get; set; } = true;

    /// <summary>Margins in millimetres.</summary>
    public double MarginMillimetres { get; set; } = 10;

    public string HeaderLeft { get; set; } = "&[Project Title]";
    public string HeaderCenter { get; set; } = "";
    public string HeaderRight { get; set; } = "&[Date]   Page &[Page] of &[Pages]";
    public string FooterLeft { get; set; } = "";
    public string FooterCenter { get; set; } = "";
    public string FooterRight { get; set; } = "";

    public LegendPlacement Legend { get; set; } = LegendPlacement.None;

    /// <summary>Print this many of the current table's columns (0 = as many as fit, at most half the page).</summary>
    public int SheetColumns { get; set; }

    /// <summary>Page size in device-independent pixels (1/96 inch), oriented.</summary>
    public (double Width, double Height) PageSizeDips()
    {
        var (w, h) = Paper switch
        {
            PaperSize.Letter => (215.9, 279.4),
            PaperSize.Legal => (215.9, 355.6),
            PaperSize.A3 => (297.0, 420.0),
            _ => (210.0, 297.0),
        };
        double Dips(double mm) => mm * 96 / 25.4;
        return Landscape ? (Dips(h), Dips(w)) : (Dips(w), Dips(h));
    }

    public double MarginDips => Math.Clamp(MarginMillimetres, 0, 60) * 96 / 25.4;

    /// <summary>Replaces the header/footer codes for one page.</summary>
    public static string Expand(string text, Project project, int page, int pages, string view)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var c = CultureInfo.CurrentCulture;
        string Date(DateTime d) => d.ToString("d", c);
        return text
            .Replace("&[Page]", page.ToString(c), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", pages.ToString(c), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", Date(DateTime.Now), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", DateTime.Now.ToString("t", c), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Project Title]", project.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Project Start]", Date(project.Tasks.Count > 0 ? project.Start : project.StartDate), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Project Finish]", project.Tasks.Count > 0 ? Date(project.Finish) : "", StringComparison.OrdinalIgnoreCase)
            .Replace("&[Status Date]", project.StatusDate is { } s ? Date(s) : "NA", StringComparison.OrdinalIgnoreCase)
            .Replace("&[View]", view, StringComparison.OrdinalIgnoreCase);
    }

    public PageSetup Copy() => (PageSetup)MemberwiseClone();
}
