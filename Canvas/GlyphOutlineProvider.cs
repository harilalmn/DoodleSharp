using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using C2VGeometry;

namespace DoodleSharp.Canvas;

/// <summary>
/// WPF-backed <see cref="IGlyphOutlineProvider"/>. Converts a <see cref="VText"/> character
/// into world-space vector contours via <c>FormattedText.BuildGeometry</c>, so a glyph can be
/// morphed (e.g. <c>new TransformAnimation(text[0], circle, 2)</c>). Assigned to
/// <see cref="VText.GlyphOutlineProvider"/> at startup.
/// </summary>
/// <remarks>
/// Geometry is produced in WORLD units (em size = <see cref="VText.Height"/>, no DPI/zoom scaling)
/// and positioned to match <c>RenderCanvas.DrawText</c>: the text box top-left is
/// <c>Location + anchorOffset</c>, FormattedText's y-down coordinates are flipped to world Y-up,
/// and the text's CCW <see cref="VText.Angle"/> is applied around <see cref="VText.Location"/>.
/// </remarks>
public sealed class GlyphOutlineProvider : IGlyphOutlineProvider
{
    public List<List<VXYZ>>? GetCharContours(VText text, int charIndex)
    {
        if (text == null || string.IsNullOrEmpty(text.Content)) return null;
        if (charIndex < 0 || charIndex >= text.Content.Length) return null;

        char c = text.Content[charIndex];
        if (char.IsWhiteSpace(c)) return null;

        double em = text.Height;
        if (em <= 0) return null;

        var typeface = new Typeface(
            new FontFamily(FontFamilyName(text.Font)),
            FontStyles.Normal,
            text.FontWeight == VFontWeight.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        const double dpi = 1.0; // world units; the canvas applies zoom separately

        // Full string measures the line box (for anchor placement) and total width.
        var full = MakeText(text.Content, typeface, em, dpi);
        double boxW = full.Width;
        double boxH = full.Height;

        // Horizontal advance up to the target character.
        double advanceX = charIndex > 0
            ? MakeText(text.Content.Substring(0, charIndex), typeface, em, dpi).Width
            : 0;

        // Outline of just this character, placed at its horizontal offset within the box.
        var glyph = MakeText(c.ToString(), typeface, em, dpi);
        Geometry geo = glyph.BuildGeometry(new Point(advanceX, 0));
        if (geo == null || geo.IsEmpty()) return null;
        PathGeometry path = geo.GetFlattenedPathGeometry(
            Math.Max(em * 0.01, 1e-3), ToleranceType.Absolute);

        // World top-left of the text box (matches RenderCanvas.DrawText).
        var (anchorOffsetX, anchorOffsetY) = text.GetAnchorOffset(boxW, boxH);
        double topLeftX = text.Location.X + anchorOffsetX;
        double topEdgeY = text.Location.Y + anchorOffsetY + boxH; // world Y-up: top of box

        double rad = text.Angle * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        bool rotated = text.Angle != 0;

        VXYZ ToWorld(Point p)
        {
            // p: text coords (x right, y DOWN, origin at box top-left).
            double wx = topLeftX + p.X;
            double wy = topEdgeY - p.Y; // flip to world Y-up
            if (rotated)
            {
                double dx = wx - text.Location.X, dy = wy - text.Location.Y;
                wx = text.Location.X + dx * cos - dy * sin;
                wy = text.Location.Y + dx * sin + dy * cos;
            }
            return new VXYZ(wx, wy);
        }

        var contours = new List<List<VXYZ>>();
        foreach (var fig in path.Figures)
        {
            var loop = new List<VXYZ> { ToWorld(fig.StartPoint) };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case PolyLineSegment pls:
                        foreach (var pt in pls.Points) loop.Add(ToWorld(pt));
                        break;
                    case LineSegment ls:
                        loop.Add(ToWorld(ls.Point));
                        break;
                }
            }
            if (loop.Count >= 2) contours.Add(loop);
        }
        return contours.Count > 0 ? contours : null;
    }

    private static FormattedText MakeText(string s, Typeface typeface, double em, double dpi) =>
        new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, em, Brushes.Black, dpi);

    private static string FontFamilyName(VFont font) => font switch
    {
        VFont.Arial => "Arial",
        VFont.TimesNewRoman => "Times New Roman",
        VFont.CourierNew => "Courier New",
        VFont.Verdana => "Verdana",
        VFont.Georgia => "Georgia",
        VFont.Tahoma => "Tahoma",
        VFont.TrebuchetMS => "Trebuchet MS",
        VFont.Consolas => "Consolas",
        VFont.Calibri => "Calibri",
        VFont.Cambria => "Cambria",
        VFont.SegoeUI => "Segoe UI",
        VFont.ComicSansMS => "Comic Sans MS",
        VFont.Impact => "Impact",
        VFont.LucidaConsole => "Lucida Console",
        _ => "Arial"
    };
}
