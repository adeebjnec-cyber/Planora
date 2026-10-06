using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Planora.App.Output;

/// <summary>Pages of the same size, ready to print or save as PDF.</summary>
public sealed record PageSet(Size PageSize, IReadOnlyList<Visual> Pages);

/// <summary>Printing, PDF and image output for rendered pages.</summary>
public static class PageOutput
{
    /// <summary>A4 landscape in device-independent pixels (1/96 inch).</summary>
    public static readonly Size A4Landscape = new(1122.5, 793.7);

    public static void SavePdf(PageSet pages, string path, string title, double dpi = 160)
    {
        var images = pages.Pages.Select(page => Render(page, pages.PageSize, dpi))
            .Select(bitmap => (Jpeg(bitmap, 90), bitmap.PixelWidth, bitmap.PixelHeight))
            .ToList();
        PdfWriter.Write(path, images, pages.PageSize.Width * 72 / 96, pages.PageSize.Height * 72 / 96, title);
    }

    /// <summary>Shows the print dialog, then builds pages at the printer's page size and prints them.</summary>
    public static bool Print(Func<Size, PageSet> buildPages, string description)
    {
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        if (dialog.ShowDialog() != true)
            return false;
        var pages = buildPages(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
        dialog.PrintDocument(new VisualPaginator(pages), description);
        return true;
    }

    public static void SavePng(Visual visual, Size size, string path, double dpi = 144)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(visual, size, dpi)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Renders a visual onto a white background at the given resolution.</summary>
    public static RenderTargetBitmap Render(Visual visual, Size size, double dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Round(size.Width * dpi / 96), (int)Math.Round(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(size));
        bitmap.Render(background);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Jpeg(BitmapSource bitmap, int quality)
    {
        // JPEG has no alpha: flatten to 24-bit RGB first.
        var rgb = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(rgb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Converts a flow document (e.g. a report) into fixed pages.</summary>
    public static PageSet Paginate(FlowDocument document, Size pageSize)
    {
        document.PageWidth = pageSize.Width;
        document.PageHeight = pageSize.Height;
        document.ColumnWidth = double.PositiveInfinity;
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = pageSize;
        paginator.ComputePageCount();
        var pages = new List<Visual>();
        for (int i = 0; i < paginator.PageCount; i++)
            pages.Add(paginator.GetPage(i).Visual);
        return new PageSet(pageSize, pages);
    }

    private sealed class VisualPaginator : DocumentPaginator
    {
        private readonly PageSet _pages;

        public VisualPaginator(PageSet pages) => _pages = pages;

        public override bool IsPageCountValid => true;
        public override int PageCount => _pages.Pages.Count;
        public override Size PageSize { get => _pages.PageSize; set { } }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var rect = new Rect(_pages.PageSize);
            return new DocumentPage(_pages.Pages[pageNumber], _pages.PageSize, rect, rect);
        }
    }
}
