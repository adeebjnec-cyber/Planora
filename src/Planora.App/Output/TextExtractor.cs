using System.Windows;
using System.Windows.Media;

namespace Planora.App.Output;

/// <summary>A run of text as drawn on a page: baseline origin and size in page units (1/96 inch), and its drawn width.</summary>
public readonly record struct TextRun(string Text, Point Origin, double FontSize, double Width);

/// <summary>
/// Finds the text drawn by a visual tree (from its glyph runs), with page positions. Used to put an
/// invisible, selectable text layer over image-based PDF pages.
/// </summary>
public static class TextExtractor
{
    public static List<TextRun> Extract(Visual root)
    {
        var runs = new List<TextRun>();
        Walk(root, Matrix.Identity, runs);
        return runs;
    }

    private static void Walk(Visual visual, Matrix parent, List<TextRun> runs)
    {
        // A visual's own transform and offset apply to its drawing and its children.
        var matrix = Matrix.Identity;
        if (VisualTreeHelper.GetTransform(visual) is { } transform)
            matrix.Append(transform.Value);
        var offset = VisualTreeHelper.GetOffset(visual);
        matrix.Translate(offset.X, offset.Y);
        matrix.Append(parent);

        if (VisualTreeHelper.GetDrawing(visual) is { } drawing)
            WalkDrawing(drawing, matrix, runs);

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                Walk(child, matrix, runs);
        }
    }

    private static void WalkDrawing(Drawing drawing, Matrix parent, List<TextRun> runs)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var matrix = group.Transform?.Value ?? Matrix.Identity;
                matrix.Append(parent);
                foreach (var child in group.Children)
                    WalkDrawing(child, matrix, runs);
                break;
            case GlyphRunDrawing { GlyphRun: { Characters: { Count: > 0 } } glyphs }:
                var text = new string(glyphs.Characters.ToArray());
                if (string.IsNullOrWhiteSpace(text))
                    break;
                double scale = Math.Sqrt(Math.Abs(parent.M11 * parent.M22 - parent.M12 * parent.M21));
                runs.Add(new TextRun(
                    text,
                    parent.Transform(glyphs.BaselineOrigin),
                    glyphs.FontRenderingEmSize * scale,
                    glyphs.AdvanceWidths.Sum() * scale));
                break;
        }
    }
}
