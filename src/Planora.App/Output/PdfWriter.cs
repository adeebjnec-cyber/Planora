using System.Globalization;
using System.IO;
using System.Text;

namespace Planora.App.Output;

/// <summary>One PDF page: a JPEG image of the page plus the text drawn on it (in page units, 1/96 inch).</summary>
public sealed record PdfPage(byte[] Jpeg, int PixelWidth, int PixelHeight, IReadOnlyList<TextRun> Text);

/// <summary>
/// Writes a PDF whose pages are JPEG images (embedded with DCTDecode, which PDF supports natively),
/// with an invisible text layer on top so text can be selected, copied and searched, as in scanned
/// documents with OCR. No external libraries are needed.
/// </summary>
public static class PdfWriter
{
    /// <summary>Average Helvetica character width as a fraction of the font size, for fitting text runs.</summary>
    private const double AverageCharWidth = 0.52;

    public static void Write(string path, IReadOnlyList<PdfPage> pages, double pageWidthPoints, double pageHeightPoints, double pageWidthUnits, string title)
    {
        using var stream = File.Create(path);
        var offsets = new List<long>();
        void Text(string s) => stream.Write(Encoding.ASCII.GetBytes(s));
        void BeginObject() { offsets.Add(stream.Position); Text($"{offsets.Count} 0 obj\n"); }
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // Header plus a comment of high bytes, which marks the file as binary for transfer tools.
        Text("%PDF-1.4\n%");
        stream.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        // Objects: 1 catalog, 2 pages, 3 info, 4 font, then per page: page, contents, image.
        int PageObject(int i) => 5 + i * 3;
        BeginObject();
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject();
        Text($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{PageObject(i)} 0 R"))}] >>\nendobj\n");
        BeginObject();
        Text($"<< /Title ({Escape(title)}) /Producer (Planora) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\nendobj\n");
        BeginObject();
        Text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        double unitsToPoints = pageWidthPoints / pageWidthUnits;
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            int pageObject = PageObject(i), contents = pageObject + 1, image = pageObject + 2;
            BeginObject();
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidthPoints)} {F(pageHeightPoints)}] " +
                 $"/Resources << /XObject << /Im0 {image} 0 R >> /Font << /F1 4 0 R >> >> /Contents {contents} 0 R >>\nendobj\n");

            var content = new MemoryStream();
            void Add(string s) => content.Write(Encoding.ASCII.GetBytes(s));
            Add($"q {F(pageWidthPoints)} 0 0 {F(pageHeightPoints)} 0 0 cm /Im0 Do Q\n");
            if (page.Text.Count > 0)
            {
                // Invisible text (render mode 3) positioned over the image, stretched to the drawn width.
                Add("BT 3 Tr\n");
                foreach (var run in page.Text)
                {
                    double size = run.FontSize * unitsToPoints;
                    if (size <= 0.5)
                        continue;
                    double x = run.Origin.X * unitsToPoints, y = pageHeightPoints - run.Origin.Y * unitsToPoints;
                    double natural = AverageCharWidth * size * run.Text.Length;
                    double stretch = natural > 0 ? Math.Clamp(100 * run.Width * unitsToPoints / natural, 10, 400) : 100;
                    Add($"/F1 {F(size)} Tf {F(stretch)} Tz 1 0 0 1 {F(x)} {F(y)} Tm (");
                    content.Write(EncodeText(run.Text));
                    Add(") Tj\n");
                }
                Add("ET\n");
            }
            var bytes = content.ToArray();
            BeginObject();
            Text($"<< /Length {bytes.Length} >>\nstream\n");
            stream.Write(bytes);
            Text("\nendstream\nendobj\n");

            BeginObject();
            Text($"<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /DeviceRGB " +
                 $"/BitsPerComponent 8 /Filter /DCTDecode /Length {page.Jpeg.Length} >>\nstream\n");
            stream.Write(page.Jpeg);
            Text("\nendstream\nendobj\n");
        }

        long xref = stream.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Text($"{offset:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    /// <summary>Text as a PDF literal string in WinAnsi (Latin-1 range; other characters become '?').</summary>
    private static byte[] EncodeText(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (char ch in text)
        {
            byte b = ch <= 0xFF ? (byte)ch : (byte)'?';
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
                bytes.Add((byte)'\\');
            bytes.Add(b < 32 ? (byte)' ' : b);
        }
        return bytes.ToArray();
    }

    private static string Escape(string text) =>
        new string(text.Select(c => c < 32 || c > 126 ? '?' : c).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
