using System;
using System.Collections.Generic;

namespace C2VGeometry;

/// <summary>
/// Available font families for text rendering.
/// </summary>
public enum VFont
{
    /// <summary>Arial - clean sans-serif font (default).</summary>
    Arial,
    /// <summary>Times New Roman - classic serif font.</summary>
    TimesNewRoman,
    /// <summary>Courier New - monospace font.</summary>
    CourierNew,
    /// <summary>Verdana - wide sans-serif font.</summary>
    Verdana,
    /// <summary>Georgia - elegant serif font.</summary>
    Georgia,
    /// <summary>Tahoma - compact sans-serif font.</summary>
    Tahoma,
    /// <summary>Trebuchet MS - humanist sans-serif font.</summary>
    TrebuchetMS,
    /// <summary>Consolas - modern monospace font.</summary>
    Consolas,
    /// <summary>Calibri - default Office font.</summary>
    Calibri,
    /// <summary>Cambria - serif font for body text.</summary>
    Cambria,
    /// <summary>Segoe UI - Windows system font.</summary>
    SegoeUI,
    /// <summary>Comic Sans MS - casual script font.</summary>
    ComicSansMS,
    /// <summary>Impact - bold display font.</summary>
    Impact,
    /// <summary>Lucida Console - monospace font.</summary>
    LucidaConsole
}

/// <summary>
/// Font weight for text rendering.
/// </summary>
public enum VFontWeight
{
    /// <summary>Normal weight (400).</summary>
    Normal,
    /// <summary>Bold weight (700).</summary>
    Bold
}

public class VText : Shape
{
    public VXYZ Location { get; set; }
    public string Content { get; set; }
    public double Height { get; set; } = 12;
    public double Width { get; set; } = 0; // 0 = auto (measured)
    public VFont Font { get; set; } = VFont.Arial;
    public VFontWeight FontWeight { get; set; } = VFontWeight.Normal;
    public VTextAnchor Anchor { get; set; } = VTextAnchor.BottomLeft;

    /// <summary>
    /// How the lines of a multi-line label line up with each other inside the text block —
    /// <see cref="VTextJustify.Left"/> by default.
    ///
    /// <para>
    /// Composes with <see cref="Anchor"/> rather than competing with it: the anchor puts the block
    /// on the drawing, this decides the shape of the ragged edge inside it. Single-line text is
    /// unaffected, because the block is then exactly as wide as its only line.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var label = new VText(0, 0, "θ = 13\nRadius r = 1\nx = 0.9074");
    /// label.Anchor = VTextAnchor.MiddleCenter;   // the block is centred on (0, 0)
    /// label.Justify = VTextJustify.Center;       // and its lines are centred on each other
    /// </code>
    /// </example>
    public VTextJustify Justify { get; set; } = VTextJustify.Left;
    /// <summary>
    /// Rotation of the text block in degrees, counterclockwise around <see cref="Location"/>.
    /// Characters rotate with the block (Excel-style). 0 = horizontal, 90 = reads bottom-to-top.
    /// </summary>
    public double Angle { get; set; } = 0;

    /// <summary>
    /// When true, a solid rectangle is painted behind the text so it stays legible over whatever
    /// it crosses — the label "mask" a CAD package draws. <b>Default is true</b>, with
    /// <see cref="MaskColor"/> following the canvas background, so a label reads cleanly wherever
    /// it lands and looks no different from unmasked text over empty canvas.
    ///
    /// <para>
    /// The mask is part of the text, not a separate shape: it is drawn immediately before the
    /// glyphs, so it never hides them and never appears on its own in the shape list. Use
    /// <see cref="Shape.ZIndex"/> to decide what the masked text sits above — a mask only hides
    /// what the text is drawn over.
    /// </para>
    ///
    /// <para>
    /// Set it to false for a label that should let the drawing show through, which is the one thing
    /// this costs: over a filled shape, a masked label punches a canvas-coloured hole.
    /// </para>
    /// </summary>
    public bool Mask { get; set; } = true;

    /// <summary>
    /// The colour of the <see cref="Mask"/> rectangle — a colour name or hex string, exactly like
    /// <see cref="Shape.Color"/>, so <c>VColor.Black</c> and <c>"#202020"</c> both work.
    ///
    /// <para>
    /// <b>Null (the default) means "the canvas background"</b>, resolved when the text is drawn
    /// rather than captured when it is constructed — so a label keeps blending in after the
    /// background is changed, with nothing to re-run. Away from a canvas (the SVG and PDF
    /// exporters) it resolves against <see cref="CanvasBackgroundColor"/>, which the host keeps up
    /// to date.
    /// </para>
    /// </summary>
    public string? MaskColor { get; set; } = null;

    /// <summary>
    /// The canvas background colour, as the host last set it (<c>"#RRGGBB"</c>). It is how a
    /// surface with no canvas of its own — the SVG and PDF exporters — resolves a null
    /// <see cref="MaskColor"/>. Mirrors <see cref="Shape.DefaultRegistry"/> and
    /// <see cref="GlyphOutlineProvider"/>: C2VGeometry has no UI and cannot know this by itself.
    ///
    /// <para>
    /// The canvas renderer does <b>not</b> read this — it resolves a null mask against its own live
    /// background brush, which cannot go stale. This is the fallback for everything else, and
    /// defaults to the app's own canvas colour so an export from a headless run still looks right.
    /// </para>
    /// </summary>
    public static string CanvasBackgroundColor { get; set; } = "#1E1E1E";

    /// <summary>
    /// How far the <see cref="Mask"/> extends beyond the text's bounding box, as a fraction of the
    /// text height: 0 hugs the box exactly, 1 pads it by a full text height on every side. Default
    /// is 0.15. Values are clamped to [0, 1].
    /// </summary>
    /// <remarks>
    /// Expressed as a fraction rather than in drawing units so a label keeps the same visual
    /// breathing room whatever its height — a 2-unit label and a 200-unit one look alike.
    /// </remarks>
    public double MaskOffset
    {
        get => _maskOffset;
        set => _maskOffset = Math.Clamp(value, 0.0, 1.0);
    }

    private double _maskOffset = 0.15;

    /// <summary>
    /// Optional provider that converts this text's glyphs into vector outlines.
    /// Set by the host application at startup (C2VGeometry is WPF-free and cannot
    /// rasterize fonts itself). Mirrors <see cref="Shape.DefaultRegistry"/>.
    /// </summary>
    public static IGlyphOutlineProvider? GlyphOutlineProvider { get; set; }

    public VText(VXYZ location, string content)
    {
        Location = new VXYZ(location.X, location.Y);
        Content = content;
        Color = ShapeDefaults.GlobalColor ?? "White";
        FillColor = ShapeDefaults.GlobalFillColor ?? "Transparent";
    }

    public VText(VXYZ location, string content, double height)
    {
        Location = new VXYZ(location.X, location.Y);
        Content = content;
        Height = height;
        Color = ShapeDefaults.GlobalColor ?? "White";
        FillColor = ShapeDefaults.GlobalFillColor ?? "Transparent";
    }

    public VText(double x, double y, string content)
    {
        Location = new VXYZ(x, y);
        Content = content;
        Color = ShapeDefaults.GlobalColor ?? "White";
        FillColor = ShapeDefaults.GlobalFillColor ?? "Transparent";
    }

    public VText(double x, double y, string content, double height)
    {
        Location = new VXYZ(x, y);
        Content = content;
        Height = height;
        Color = ShapeDefaults.GlobalColor ?? "White";
        FillColor = ShapeDefaults.GlobalFillColor ?? "Transparent";
    }



    public override List<ControlPoint> GetControlPoints()
    {
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, Location.X, Location.Y, "Location")
        };
    }

    public override void MoveControlPoint(int index, VXYZ newPosition)
    {
        if (index == 0)
        {
            Location = new VXYZ(newPosition.X, newPosition.Y);
        }
    }

    public override VText Clone()
    {
        var clone = new VText(Location.Clone(), Content)
        {
            Height = Height,
            Width = Width,
            Font = Font,
            FontWeight = FontWeight,
            Anchor = Anchor,
            Justify = Justify,
            Angle = Angle,
            Mask = Mask,
            MaskColor = MaskColor,
            MaskOffset = MaskOffset
        };
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        Location = Location + vector;
    }

    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        Location = GeometryHelper.RotatePoint(Location, pivot, angleDegrees);
        Angle += angleDegrees;
    }

    public override void Flip(VLine mirrorLine)
    {
        Location = GeometryHelper.FlipPoint(Location, mirrorLine);
    }

    public override void Scale(VXYZ center, double factor)
    {
        Location = GeometryHelper.ScalePoint(Location, center, factor);
        Height *= Math.Abs(factor);
        if (Width > 0)
            Width *= Math.Abs(factor);
    }

    /// <summary>
    /// The layout box of the whole label, honouring <see cref="Anchor"/> and <see cref="Angle"/>.
    /// For <see cref="VText"/> this box <i>is</i> the shape — the type is deliberately exempt from
    /// exact <c>Contains</c>/<c>DistanceTo</c> because a glyph run has no other outline — so
    /// everything from hit testing to zoom-to-fit reads it.
    /// </summary>
    public override BoundingBox GetBounds()
    {
        var (textWidth, textHeight) = MeasureBlock();
        var (offsetX, offsetY) = GetAnchorOffset(textWidth, textHeight);

        if (Angle == 0)
        {
            var bottomLeft = new VXYZ(Location.X + offsetX, Location.Y + offsetY);
            return new BoundingBox(bottomLeft, new VXYZ(bottomLeft.X + textWidth, bottomLeft.Y + textHeight));
        }

        var rad = Angle * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        double rx0 = offsetX, ry0 = offsetY;
        double rx1 = offsetX + textWidth, ry1 = offsetY;
        double rx2 = offsetX + textWidth, ry2 = offsetY + textHeight;
        double rx3 = offsetX, ry3 = offsetY + textHeight;

        VXYZ Rotate(double rx, double ry) =>
            new VXYZ(Location.X + rx * cos - ry * sin, Location.Y + rx * sin + ry * cos);

        var p0 = Rotate(rx0, ry0);
        var p1 = Rotate(rx1, ry1);
        var p2 = Rotate(rx2, ry2);
        var p3 = Rotate(rx3, ry3);

        var minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }

    /// <summary>
    /// Baseline-to-baseline distance as a multiple of <see cref="Height"/>. A font's line box is
    /// taller than its em size — the ascender, descender and leading all sit outside it — so
    /// stacking lines at exactly <c>Height</c> apart measures a multi-line block noticeably shorter
    /// than it renders, and the bounding box then clips its own first line. 1.2 is the usual figure
    /// for the sans faces <see cref="VFont"/> offers; it is an estimate for the same reason the
    /// character width is, since C2VGeometry has no font metrics of its own.
    ///
    /// <para>
    /// <b>Internal, and shared with the exporters.</b> The DXF, SVG and PDF writers each stack the
    /// lines of a label themselves, and they had drifted: DXF used 1.2 while SVG and PDF stacked at
    /// exactly <c>Height</c>, so the same label came out a different height in each format and none
    /// of them matched the box <see cref="GetBounds"/> reserves for it. One constant, four readers.
    /// </para>
    /// </summary>
    internal const double LineSpacing = 1.2;

    /// <summary>
    /// The estimated size of the whole text block: the width of its <b>widest line</b> by the
    /// height of all its lines together. Both are estimates — C2VGeometry cannot measure a font —
    /// but they are the estimates every geometry-side consumer shares, so the box is at least
    /// self-consistent everywhere.
    /// </summary>
    /// <remarks>
    /// Written per line rather than over the whole string because <see cref="Content"/> may be
    /// multi-line, and a single-line measure is wrong on both axes at once: the width summed every
    /// line end to end (counting the newline characters themselves), while the height stayed at one
    /// line. A three-line label 18 units wide and 30 tall reported itself as 66 by 10, so a
    /// selection click landed in the wrong place, zoom-to-fit framed empty canvas beside the label
    /// and clipped the top of it, and the cull index dropped the label whenever only its upper
    /// lines were on screen. An explicit <see cref="Width"/> still wins, exactly as before, and
    /// single-line text measures identically to the way it always has.
    /// </remarks>
    internal (double width, double height) MeasureBlock()
    {
        int lineCount = 1;
        int longest = 0;
        int current = 0;

        foreach (var c in Content)
        {
            if (c == '\n')
            {
                if (current > longest) longest = current;
                current = 0;
                lineCount++;
            }
            else if (c != '\r')
            {
                current++;
            }
        }
        if (current > longest) longest = current;

        var width = Width > 0 ? Width : Height * longest * 0.6;

        // Only the GAPS between lines are scaled by the line spacing, not the block as a whole, so
        // single-line text still measures exactly Height and nothing that existed before this moves.
        var height = Height * (1 + (lineCount - 1) * LineSpacing);
        return (width, height);
    }

    public (double offsetX, double offsetY) GetAnchorOffset(double textWidth, double textHeight)
    {
        double offsetX = Anchor switch
        {
            VTextAnchor.BottomLeft or VTextAnchor.MiddleLeft or VTextAnchor.TopLeft => 0,
            VTextAnchor.BottomCenter or VTextAnchor.MiddleCenter or VTextAnchor.TopCenter => -textWidth / 2,
            _ => -textWidth
        };
        double offsetY = Anchor switch
        {
            VTextAnchor.BottomLeft or VTextAnchor.BottomCenter or VTextAnchor.BottomRight => 0,
            VTextAnchor.MiddleLeft or VTextAnchor.MiddleCenter or VTextAnchor.MiddleRight => -textHeight / 2,
            _ => -textHeight
        };
        return (offsetX, offsetY);
    }

    /// <summary>
    /// Returns true if the text's (possibly rotated) bounding quad overlaps the other shape's
    /// bounding box. Symmetric for axis-aligned text; uses an OBB-vs-AABB SAT test when rotated.
    /// </summary>
    public override bool DoesIntersect(Shape other)
    {
        if (other == null) return false;

        GetCornerCoords(out var ax, out var ay);
        var b = other.GetBounds();
        var bx = new[] { b.Min.X, b.Max.X, b.Max.X, b.Min.X };
        var by = new[] { b.Min.Y, b.Min.Y, b.Max.Y, b.Max.Y };

        return ConvexQuadsOverlap(ax, ay, bx, by);
    }

    private void GetCornerCoords(out double[] xs, out double[] ys)
    {
        var (textWidth, textHeight) = MeasureBlock();
        var (offsetX, offsetY) = GetAnchorOffset(textWidth, textHeight);

        if (Angle == 0)
        {
            xs = new[] { Location.X + offsetX, Location.X + offsetX + textWidth, Location.X + offsetX + textWidth, Location.X + offsetX };
            ys = new[] { Location.Y + offsetY, Location.Y + offsetY, Location.Y + offsetY + textHeight, Location.Y + offsetY + textHeight };
            return;
        }

        var rad = Angle * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        double rx0 = offsetX, ry0 = offsetY;
        double rx1 = offsetX + textWidth, ry1 = offsetY;
        double rx2 = offsetX + textWidth, ry2 = offsetY + textHeight;
        double rx3 = offsetX, ry3 = offsetY + textHeight;
        xs = new[]
        {
            Location.X + rx0 * cos - ry0 * sin,
            Location.X + rx1 * cos - ry1 * sin,
            Location.X + rx2 * cos - ry2 * sin,
            Location.X + rx3 * cos - ry3 * sin,
        };
        ys = new[]
        {
            Location.Y + rx0 * sin + ry0 * cos,
            Location.Y + rx1 * sin + ry1 * cos,
            Location.Y + rx2 * sin + ry2 * cos,
            Location.Y + rx3 * sin + ry3 * cos,
        };
    }

    private static bool ConvexQuadsOverlap(double[] ax, double[] ay, double[] bx, double[] by)
    {
        const double eps = 1e-9;
        for (int side = 0; side < 2; side++)
        {
            var qx = side == 0 ? ax : bx;
            var qy = side == 0 ? ay : by;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) & 3;
                double axisX = -(qy[j] - qy[i]);
                double axisY = qx[j] - qx[i];
                double len = Math.Sqrt(axisX * axisX + axisY * axisY);
                if (len < 1e-12) continue;
                axisX /= len; axisY /= len;

                Project(ax, ay, axisX, axisY, out var minA, out var maxA);
                Project(bx, by, axisX, axisY, out var minB, out var maxB);
                if (maxA < minB - eps || maxB < minA - eps) return false;
            }
        }
        return true;
    }

    private static void Project(double[] xs, double[] ys, double axisX, double axisY, out double min, out double max)
    {
        min = double.PositiveInfinity;
        max = double.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            double d = xs[i] * axisX + ys[i] * axisY;
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    #region Glyph extraction

    /// <summary>
    /// Builds a shape from the outline of the character at <paramref name="index"/>,
    /// positioned in world space exactly where the character is rendered. Does NOT
    /// modify this text. Returns a closed <see cref="VPolyline"/> for a single-contour
    /// glyph, or a <see cref="VGroup"/> of closed polylines for glyphs with holes
    /// (e.g. 'O', 'A', 'B'). Returns null for whitespace, an out-of-range index, or
    /// when no <see cref="GlyphOutlineProvider"/> is set.
    /// </summary>
    public Shape? ToCharShape(int index)
    {
        var provider = GlyphOutlineProvider;
        if (provider == null) return null;
        if (index < 0 || index >= Content.Length) return null;

        var contours = provider.GetCharContours(this, index);
        if (contours == null || contours.Count == 0) return null;

        // Build the contour polylines without registering each one on the canvas —
        // only the returned shape should register (same anti-pollution rule the Chart
        // helper uses). The VGroup renderer still draws the children.
        bool prevAuto = Shape.AutoRegister;
        Shape.AutoRegister = false;
        Shape? result;
        try
        {
            var loops = new List<Shape>();
            foreach (var contour in contours)
            {
                if (contour == null || contour.Count < 2) continue;
                var pts = new List<VXYZ>(contour);
                // Close the loop so it reads as a glyph outline and samples as a closed curve.
                if (pts[0].Subtract(pts[^1]).GetLength() > GeometryTolerance.Epsilon)
                    pts.Add(pts[0].Clone());
                loops.Add(new VPolyline(pts) { Color = Color });
            }
            if (loops.Count == 0) return null;

            result = loops.Count == 1 ? loops[0] : new VGroup(loops);
            result.Color = Color;
            char c = Content[index];
            result.Name = $"glyph_{(char.IsLetterOrDigit(c) ? c.ToString() : "char")}_{result.Id}";
        }
        finally
        {
            Shape.AutoRegister = prevAuto;
        }

        // Register the assembled shape itself (respecting the auto-draw setting).
        if (Shape.AutoRegister)
            Shape.DefaultRegistry?.Register(result);
        return result;
    }

    /// <summary>
    /// Extracts the character at <paramref name="index"/> as a shape (see
    /// <see cref="ToCharShape"/>) AND replaces it with a space in this text, so the glyph
    /// appears to lift out of the word. Returns the extracted shape, or null if there is
    /// nothing to lift (whitespace / out of range / no provider).
    /// </summary>
    public Shape? LiftChar(int index)
    {
        var shape = ToCharShape(index);
        if (shape != null) BlankChar(index);
        return shape;
    }

    /// <summary>
    /// Lifts a run of <paramref name="count"/> characters starting at <paramref name="start"/>
    /// into a single <see cref="VGroup"/> (blanking each in this text). Useful for morphing a
    /// selection. Returns null if no characters in the range produced an outline.
    /// </summary>
    public VGroup? LiftChars(int start, int count)
    {
        if (start < 0 || count <= 0 || start >= Content.Length) return null;
        int end = System.Math.Min(start + count, Content.Length);

        bool prevAuto = Shape.AutoRegister;
        Shape.AutoRegister = false;
        VGroup? group = null;
        try
        {
            var members = new List<Shape>();
            for (int i = start; i < end; i++)
            {
                var s = ToCharShape(i);
                if (s != null) { members.Add(s); BlankChar(i); }
            }
            if (members.Count == 0) return null;
            group = new VGroup(members) { Color = Color };
            group.Name = $"glyphs_{group.Id}";
        }
        finally
        {
            Shape.AutoRegister = prevAuto;
        }
        if (Shape.AutoRegister)
            Shape.DefaultRegistry?.Register(group);
        return group;
    }

    /// <summary>
    /// Convenience indexer: <c>text[i]</c> lifts the character at <paramref name="index"/>
    /// out as a shape and replaces it with a space (see <see cref="LiftChar"/>). This is the
    /// ergonomic form for <c>new TransformAnimation(text[0], circle, 2)</c>.
    /// Note: reading the indexer mutates this text — it is not a pure accessor.
    /// </summary>
    public Shape? this[int index] => LiftChar(index);

    /// <summary>
    /// Replaces the character at <paramref name="index"/> with a space, preserving the
    /// length and the positions of the other characters. Used to "remove" a character that
    /// has been lifted out as a shape. No-op for an out-of-range index.
    /// </summary>
    public void BlankChar(int index)
    {
        if (index < 0 || index >= Content.Length) return;
        Content = Content.Substring(0, index) + " " + Content.Substring(index + 1);
    }

    #endregion

    public override string ToString() => $"VText(\"{Content}\" at {Location})";
}
