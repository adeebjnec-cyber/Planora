using System.Globalization;
using System.IO;
using System.Text;

namespace Planora.App.Output;

/// <summary>
/// Writes a PDF whose pages are JPEG images (embedded with DCTDecode, which PDF supports natively).
/// Pages are rendered at high resolution, so print quality is good; text is not selectable.
/// </summary>
public static class PdfWriter
{
    /// <param name="pages">JPEG bytes and pixel size of each page.</param>
    /// <param name="pageWidthPoints">Page width in points (1/72 inch), e.g. 842 for A4 landscape.</param>
    public static void Write(string path, IReadOnlyList<(byte[] Jpeg, int PixelWidth, int PixelHeight)> pages, double pageWidthPoints, double pageHeightPoints, string title)
    {
        using var stream = File.Create(path);
        var offsets = new List<long>();
        void Text(string s) { var bytes = Encoding.ASCII.GetBytes(s); stream.Write(bytes); }
        void BeginObject() { offsets.Add(stream.Position); Text($"{offsets.Count} 0 obj\n"); }

        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        // Header plus a comment of high bytes, which marks the file as binary for transfer tools.
        Text("%PDF-1.4\n%");
        stream.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        // Object numbers: 1 catalog, 2 pages, 3 info, then per page: page, contents, image.
        int pageObject(int i) => 4 + i * 3;
        BeginObject();
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject();
        Text($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{pageObject(i)} 0 R"))}] >>\nendobj\n");
        BeginObject();
        Text($"<< /Title ({Escape(title)}) /Producer (Planora) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            var (jpeg, width, height) = pages[i];
            int page = pageObject(i), contents = page + 1, image = page + 2;
            BeginObject();
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidthPoints)} {F(pageHeightPoints)}] " +
                 $"/Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {contents} 0 R >>\nendobj\n");
            var drawing = $"q {F(pageWidthPoints)} 0 0 {F(pageHeightPoints)} 0 0 cm /Im0 Do Q\n";
            BeginObject();
            Text($"<< /Length {drawing.Length} >>\nstream\n{drawing}endstream\nendobj\n");
            BeginObject();
            Text($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB " +
                 $"/BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            stream.Write(jpeg);
            Text("\nendstream\nendobj\n");
        }

        long xref = stream.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Text($"{offset:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static string Escape(string text) =>
        new string(text.Select(c => c < 32 || c > 126 ? '?' : c).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
