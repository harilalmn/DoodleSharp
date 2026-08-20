using System.Globalization;
using System.Text;
using C2VGeometry;

namespace DoodleSharp.Canvas;

/// <summary>
/// Exports shapes to SVG format.
/// </summary>
public static class SvgExporter
{
    /// <summary>
    /// Exports shapes to an SVG string.
    /// </summary>
    public static string Export(IEnumerable<IDrawable> shapes, double width = 800, double height = 600, double padding = 20)
    {
        var shapeList = shapes.ToList();
        
        // Calculate bounds
        double minX = 0, minY = 0, maxX = width, maxY = height;
        if (shapeList.Any())
        {
            minX = double.MaxValue; minY = double.MaxValue;
            maxX = double.MinValue; maxY = double.MinValue;
            
            foreach (var shape in shapeList.OfType<Shape>())
            {
                var bounds = shape.GetBounds();
                minX = Math.Min(minX, bounds.Min.X);
                minY = Math.Min(minY, bounds.Min.Y);
                maxX = Math.Max(maxX, bounds.Max.X);
                maxY = Math.Max(maxY, bounds.Max.Y);
            }
        }
        
        // Add padding
        minX -= padding;
        minY -= padding;
        maxX += padding;
        maxY += padding;
        
        var viewWidth = maxX - minX;
        var viewHeight = maxY - minY;
        
        var sb = new StringBuilder();
        sb.AppendLine($"<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"{F(minX)} {F(-maxY)} {F(viewWidth)} {F(viewHeight)}\">");
        sb.AppendLine("  <g transform=\"scale(1, -1)\">");  // Flip Y for math coordinates
        
        foreach (var shape in shapeList)
        {
            var svgElement = ShapeToSvg(shape);
            if (!string.IsNullOrEmpty(svgElement))
                sb.AppendLine("    " + svgElement);
        }
        
        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");
        
        return sb.ToString();
    }

    /// <summary>
    /// The stroke attributes that have to accompany every <c>stroke-width</c> we emit.
    ///
    /// <para>
    /// <b><c>vector-effect="non-scaling-stroke"</c> is the fix for a real unit bug.</b> The document's
    /// <c>viewBox</c> is in world coordinates and the group is drawn at 1:1, so a raw
    /// <c>stroke-width</c> was interpreted in <i>world units</i> — the default <c>LineWeight = 2</c>
    /// became two world units wide, which makes a 10,000-unit drawing export with strokes too thin to
    /// see and a 10-unit one export as a blob. Non-scaling-stroke pins the width to device pixels,
    /// which is what <c>LineWeight</c> means everywhere else in the app.
    /// </para>
    ///
    /// <para>
    /// It is emitted per element because <c>vector-effect</c> is not an inherited property, so
    /// hoisting it onto the enclosing group would do nothing. <c>stroke-dasharray</c> would inherit,
    /// but it varies per shape, so it belongs here too. Dash runs come from
    /// <see cref="C2VGeometry.Rendering.LineTypePatterns"/> — the same definition the screen backends
    /// use — and are already in device pixels, which is exactly the space non-scaling-stroke puts
    /// them in. SVG output used to be solid regardless of LineType.
    /// </para>
    /// </summary>
    private static string StrokeExtras(Shape shape)
        => StrokeExtras(shape.LineType, shape.LineTypeScale);

    /// <summary>Same, for the tessellator's <see cref="C2VGeometry.Rendering.PenSpec"/> path.</summary>
    private static string StrokeExtras(C2VGeometry.Rendering.PenSpec pen)
        => StrokeExtras(pen.LineType, pen.LineTypeScale);

    private static string StrokeExtras(LineType lineType, double lineTypeScale)
    {
        var extras = " vector-effect=\"non-scaling-stroke\"";

        if (C2VGeometry.Rendering.LineTypePatterns.IsSolid(lineType, lineTypeScale))
            return extras;

        var scale = C2VGeometry.Rendering.LineTypePatterns.ClampScale(lineTypeScale);
        var pattern = C2VGeometry.Rendering.LineTypePatterns.DevicePixels(lineType);

        var runs = new string[pattern.Length];
        for (int i = 0; i < pattern.Length; i++) runs[i] = F(pattern[i] * scale);

        return extras + $" stroke-dasharray=\"{string.Join(",", runs)}\"";
    }

    private static string ShapeToSvg(IDrawable drawable)
    {
        return drawable switch
        {
            VPoint p => $"<circle cx=\"{F(p.X)}\" cy=\"{F(p.Y)}\" r=\"5\" fill=\"{p.FillColor}\" stroke=\"{p.Color}\" stroke-width=\"{F(p.LineWeight)}\"{StrokeExtras(p)} />",
            
            VLine l => $"<line x1=\"{F(l.Start.X)}\" y1=\"{F(l.Start.Y)}\" x2=\"{F(l.End.X)}\" y2=\"{F(l.End.Y)}\" stroke=\"{l.Color}\" stroke-width=\"{F(l.LineWeight)}\"{StrokeExtras(l)} />",
            
            VCircle c => $"<circle cx=\"{F(c.Center.X)}\" cy=\"{F(c.Center.Y)}\" r=\"{F(c.Radius)}\" fill=\"{c.FillColor}\" stroke=\"{c.Color}\" stroke-width=\"{F(c.LineWeight)}\"{StrokeExtras(c)} />",
            
            VEllipse e => EllipseToSvg(e),

            // Before VRectangle, because a rectangle IS a polygon and its Points already carry its
            // rotation. This arm used to rebuild an axis-aligned box from Corner/Width/Height, so a
            // rotated rectangle exported flat while the canvas drew it turned.
            VRectangle r => PolygonToSvg(r),
            
            VArc a => ArcToSvg(a),
            VPolygon pg => PolygonToSvg(pg),
            VPolyline pl => PolylineToSvg(pl),
            VBezier b => BezierToSvg(b),
            VSpline s => SplineToSvg(s),
            VArrow ar => ArrowToSvg(ar),
            VDimension d => DimensionToSvg(d),
            VText t => TextToSvg(t),
            VGroup g => GroupToSvg(g),

            // Anything with no native SVG element is flattened rather than dropped. This arm used
            // to return "" and the shape simply vanished from the file, with no error and nothing
            // to notice -- and each exporter's switch covered a different subset, so the same
            // drawing could survive one format and lose shapes in another.
            Shape other => TessellatedToSvg(other),
            _ => ""
        };
    }

    [ThreadStatic] private static C2VGeometry.Rendering.ShapeTessellator? _fallbackTessellator;

    private static string TessellatedToSvg(Shape shape)
    {
        _fallbackTessellator ??= new C2VGeometry.Rendering.ShapeTessellator();

        var sb = new StringBuilder();
        var sink = new C2VGeometry.Rendering.PolylineFallbackSink
        {
            OnPolyline = (pts, closed, pen) =>
            {
                if (pts.Count < 2) return;
                var d = new StringBuilder("M ");
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) d.Append(" L ");
                    d.Append(F(pts[i].X)).Append(' ').Append(F(pts[i].Y));
                }
                if (closed) d.Append(" Z");
                sb.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{pen.Color}\" stroke-width=\"{F(pen.LineWeight)}\"{StrokeExtras(pen)} />");
            },
            OnPoint = (pt, pen) =>
                sb.Append($"<circle cx=\"{F(pt.X)}\" cy=\"{F(pt.Y)}\" r=\"1\" fill=\"{pen.Color}\" />"),
            OnText = t => sb.Append(TextToSvg(t)),
        };

        _fallbackTessellator.Tessellate(shape, sink);
        return sb.ToString();
    }

    /// <summary>
    /// A label as SVG: one <c>&lt;tspan&gt;</c> per line, positioned against the label's own layout
    /// box so that <see cref="VText.Anchor"/>, <see cref="VText.Justify"/> and
    /// <see cref="VText.Angle"/> all survive the export.
    /// </summary>
    /// <remarks>
    /// The whole of <c>Content</c> used to go into a single <c>&lt;text&gt;</c> element at
    /// <c>Location</c>. SVG treats a newline inside a text element as ordinary whitespace, so a
    /// multi-line label collapsed onto one line; and because the element was placed at
    /// <c>Location</c> directly, <c>Anchor</c> was ignored, so every label that was not
    /// <c>BottomLeft</c> exported somewhere the canvas had not drawn it.
    /// </remarks>
    private static string TextToSvg(VText t)
    {
        var lines = SplitLines(t.Content);
        var (blockWidth, blockHeight) = t.MeasureBlock();
        var (anchorX, anchorY) = t.GetAnchorOffset(blockWidth, blockHeight);

        var originX = t.Location.X + anchorX;
        var originY = t.Location.Y + anchorY;

        // SVG's own text-anchor does the per-line alignment, which is the same job Justify names.
        var textAnchor = t.Justify switch
        {
            VTextJustify.Center => "middle",
            VTextJustify.Right => "end",
            _ => "start"
        };
        var alignX = t.Justify switch
        {
            VTextJustify.Center => originX + blockWidth / 2,
            VTextJustify.Right => originX + blockWidth,
            _ => originX
        };

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            // The block's origin is its bottom-left, so the first line's baseline sits (n-1) line
            // heights above it.
            //
            // The element carries its own scale(1,-1) so the glyphs read the right way up, and the
            // baseline is ALSO negated. Both, not one or the other: SVG transforms compose rather
            // than replace, so the element's flip and the enclosing group's flip multiply out to
            // the identity, leaving x/y to be read in document space — where y grows downward.
            // Emitting the un-negated world Y (as this did) therefore placed every label at the
            // reflection of its position through the X axis, while its own mask rect — which has no
            // counter-flip and so was always negated — stayed correctly under where the label
            // should have been. A masked label, the default, exported as a plate with no text on it
            // and the text somewhere else entirely.
            var baseline = -(originY + (lines.Length - 1 - i) * t.Height * VText.LineSpacing);
            sb.Append($"<text x=\"{F(alignX)}\" y=\"{F(baseline)}\" fill=\"{t.Color}\" font-size=\"{F(t.Height)}\" text-anchor=\"{textAnchor}\" transform=\"scale(1,-1)\">{EscapeXml(lines[i])}</text>");
        }

        var inner = sb.ToString();
        if (t.Mask) inner = MaskToSvg(t) + inner;
        if (t.Angle == 0) return inner;
        // World Angle is CCW (Y-up); parent group's scale(1,-1) flips Y, so we negate to keep CCW visually.
        return $"<g transform=\"rotate({F(-t.Angle)}, {F(t.Location.X)}, {F(t.Location.Y)})\">{inner}</g>";
    }

    /// <summary>
    /// Splits a label into its lines, tolerating any of the three line-ending conventions. Never
    /// empty: a label with no content is still one (empty) line.
    /// </summary>
    private static string[] SplitLines(string? content)
    {
        if (string.IsNullOrEmpty(content)) return new[] { string.Empty };
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>
    /// The rectangle behind a masked <see cref="VText"/>, emitted before the glyphs so it renders
    /// underneath them.
    /// </summary>
    /// <remarks>
    /// SVG has no way to measure a string, so the width is the same estimate <c>VText.GetBounds</c>
    /// uses (0.6 em per character) rather than a real measurement — a mask exported to SVG is a
    /// close fit, not an exact one. It is positioned against the text's <b>drawn</b> box, anchor
    /// offset included, so the plate and the glyphs stay glued together whatever the anchor — both
    /// now honour it, where previously neither did. The rect is written in the document's own flipped-Y
    /// space (the enclosing group applies <c>scale(1,-1)</c>), which is why its Y is negated here.
    /// The text element negates too — its own <c>scale(1,-1)</c> is what keeps the glyphs upright,
    /// not a substitute for the negation, because SVG transforms compose.
    /// </remarks>
    private static string MaskToSvg(VText t)
    {
        var (width, blockHeight) = t.MeasureBlock();
        var pad = t.MaskOffset * t.Height;
        var (anchorX, anchorY) = t.GetAnchorOffset(width, blockHeight);

        // A null MaskColor means "the canvas background". There is no canvas here, so it resolves
        // against the colour the host publishes (VText.CanvasBackgroundColor) — otherwise a default
        // masked label would export with no plate at all and the SVG would not match the screen.
        var fill = string.IsNullOrEmpty(t.MaskColor) ? VText.CanvasBackgroundColor : t.MaskColor;

        var minX = t.Location.X + anchorX - pad;
        var minY = t.Location.Y + anchorY - pad;
        var w = width + 2 * pad;
        var h = blockHeight + 2 * pad;

        // y is negated (and the top edge used) because the parent group flips Y.
        return $"<rect x=\"{F(minX)}\" y=\"{F(-(minY + h))}\" width=\"{F(w)}\" height=\"{F(h)}\" fill=\"{fill}\" stroke=\"none\" />";
    }

    /// <summary>
    /// A <see cref="VEllipse"/> as SVG: the native <c>&lt;ellipse&gt;</c> element for a whole one,
    /// and a sampled path for a partial sweep, which SVG has no element for.
    /// </summary>
    /// <remarks>
    /// <see cref="VEllipse.Rotation"/> becomes an SVG <c>rotate</c> about the centre. It is negated
    /// because the enclosing group applies <c>scale(1,-1)</c>, the same reason
    /// <see cref="TextToSvg"/> negates <c>VText.Angle</c>.
    ///
    /// <para>
    /// This used to be a bare <c>&lt;ellipse&gt;</c> on the centre and radii, so a half ellipse
    /// exported as a whole one and a turned ellipse exported flat — a silent disagreement with what
    /// the canvas had drawn.
    /// </para>
    /// </remarks>
    private static string EllipseToSvg(VEllipse e)
    {
        var sweep = e.EndAngle - e.StartAngle;
        var whole = Math.Abs(Math.Abs(sweep) - 360.0) < 1e-9 || Math.Abs(sweep) < 1e-9;

        string inner;
        if (whole)
        {
            inner = $"<ellipse cx=\"{F(e.Center.X)}\" cy=\"{F(e.Center.Y)}\" rx=\"{F(e.RadiusX)}\" ry=\"{F(e.RadiusY)}\" fill=\"{e.FillColor}\" stroke=\"{e.Color}\" stroke-width=\"{F(e.LineWeight)}\"{StrokeExtras(e)} />";
        }
        else
        {
            const int segments = 72;
            var d = new StringBuilder("M ");
            for (int i = 0; i <= segments; i++)
            {
                // Sampled in the ellipse's own frame; the rotate transform below turns the result,
                // so the path and the whole-ellipse element are oriented the same way.
                var angle = e.StartAngle + sweep * (i / (double)segments);
                var rad = angle * Math.PI / 180.0;
                var x = e.Center.X + e.RadiusX * Math.Cos(rad);
                var y = e.Center.Y + e.RadiusY * Math.Sin(rad);
                if (i > 0) d.Append(" L ");
                d.Append(F(x)).Append(' ').Append(F(y));
            }
            inner = $"<path d=\"{d}\" fill=\"none\" stroke=\"{e.Color}\" stroke-width=\"{F(e.LineWeight)}\"{StrokeExtras(e)} />";
        }

        if (e.Rotation == 0) return inner;
        return $"<g transform=\"rotate({F(-e.Rotation)}, {F(e.Center.X)}, {F(e.Center.Y)})\">{inner}</g>";
    }

    private static string ArcToSvg(VArc arc)
    {
        var startRad = arc.StartAngle * Math.PI / 180;
        var endRad = arc.EndAngle * Math.PI / 180;
        var startX = arc.Center.X + arc.Radius * Math.Cos(startRad);
        var startY = arc.Center.Y + arc.Radius * Math.Sin(startRad);
        var endX = arc.Center.X + arc.Radius * Math.Cos(endRad);
        var endY = arc.Center.Y + arc.Radius * Math.Sin(endRad);
        
        var angleDiff = arc.EndAngle - arc.StartAngle;
        if (angleDiff < 0) angleDiff += 360;
        var largeArc = angleDiff > 180 ? 1 : 0;
        
        return $"<path d=\"M {F(startX)} {F(startY)} A {F(arc.Radius)} {F(arc.Radius)} 0 {largeArc} 0 {F(endX)} {F(endY)}\" fill=\"none\" stroke=\"{arc.Color}\" stroke-width=\"{F(arc.LineWeight)}\"{StrokeExtras(arc)} />";
    }

    private static string PolygonToSvg(VPolygon polygon)
    {
        if (polygon.Points.Count < 3) return "";
        var points = string.Join(" ", polygon.Points.Select(p => $"{F(p.X)},{F(p.Y)}"));
        return $"<polygon points=\"{points}\" fill=\"{polygon.FillColor}\" stroke=\"{polygon.Color}\" stroke-width=\"{F(polygon.LineWeight)}\"{StrokeExtras(polygon)} />";
    }

    private static string PolylineToSvg(VPolyline polyline)
    {
        if (polyline.Points.Count < 2) return "";
        var points = string.Join(" ", polyline.Points.Select(p => $"{F(p.X)},{F(p.Y)}"));
        return $"<polyline points=\"{points}\" fill=\"none\" stroke=\"{polyline.Color}\" stroke-width=\"{F(polyline.LineWeight)}\"{StrokeExtras(polyline)} />";
    }

    private static string BezierToSvg(VBezier bezier)
    {
        return $"<path d=\"M {F(bezier.P0.X)} {F(bezier.P0.Y)} C {F(bezier.P1.X)} {F(bezier.P1.Y)}, {F(bezier.P2.X)} {F(bezier.P2.Y)}, {F(bezier.P3.X)} {F(bezier.P3.Y)}\" fill=\"none\" stroke=\"{bezier.Color}\" stroke-width=\"{F(bezier.LineWeight)}\"{StrokeExtras(bezier)} />";
    }

    private static string SplineToSvg(VSpline spline)
    {
        var points = spline.GetRenderPoints();
        if (points.Count < 2) return "";
        
        var pathData = $"M {F(points[0].X)} {F(points[0].Y)}";
        for (int i = 1; i < points.Count; i++)
            pathData += $" L {F(points[i].X)} {F(points[i].Y)}";
        
        return $"<path d=\"{pathData}\" fill=\"none\" stroke=\"{spline.Color}\" stroke-width=\"{F(spline.LineWeight)}\"{StrokeExtras(spline)} />";
    }

    private static string ArrowToSvg(VArrow arrow)
    {
        var sb = new StringBuilder();
        // Main line
        sb.Append($"<line x1=\"{F(arrow.Start.X)}\" y1=\"{F(arrow.Start.Y)}\" x2=\"{F(arrow.End.X)}\" y2=\"{F(arrow.End.Y)}\" stroke=\"{arrow.Color}\" stroke-width=\"{F(arrow.LineWeight)}\"{StrokeExtras(arrow)} />");

        // Filled arrowhead polygons. DoubleEnded was ignored here, so the start head was dropped.
        var (w1, w2) = arrow.GetEndArrowhead();
        sb.Append($"<polygon points=\"{F(arrow.End.X)},{F(arrow.End.Y)} {F(w1.X)},{F(w1.Y)} {F(w2.X)},{F(w2.Y)}\" fill=\"{arrow.Color}\" stroke=\"{arrow.Color}\" stroke-width=\"{F(arrow.LineWeight)}\"{StrokeExtras(arrow)} />");

        if (arrow.DoubleEnded)
        {
            var (s1, s2) = arrow.GetStartArrowhead();
            sb.Append($"<polygon points=\"{F(arrow.Start.X)},{F(arrow.Start.Y)} {F(s1.X)},{F(s1.Y)} {F(s2.X)},{F(s2.Y)}\" fill=\"{arrow.Color}\" stroke=\"{arrow.Color}\" stroke-width=\"{F(arrow.LineWeight)}\"{StrokeExtras(arrow)} />");
        }

        return $"<g>{sb}</g>";
    }

    private static string DimensionToSvg(VDimension dim)
    {
        var (ds, de, tp, e1s, e1e, e2s, e2e) = dim.GetDimensionGeometry();
        var sb = new StringBuilder();
        // Dimension line
        sb.Append($"<line x1=\"{F(ds.X)}\" y1=\"{F(ds.Y)}\" x2=\"{F(de.X)}\" y2=\"{F(de.Y)}\" stroke=\"{dim.Color}\" stroke-width=\"{F(dim.LineWeight)}\"{StrokeExtras(dim)} />");
        // Extension lines (respecting suppress flags)
        if (!dim.SuppressExtLine1)
            sb.Append($"<line x1=\"{F(e1s.X)}\" y1=\"{F(e1s.Y)}\" x2=\"{F(e1e.X)}\" y2=\"{F(e1e.Y)}\" stroke=\"{dim.Color}\" stroke-width=\"{F(dim.LineWeight)}\"{StrokeExtras(dim)} />");
        if (!dim.SuppressExtLine2)
            sb.Append($"<line x1=\"{F(e2s.X)}\" y1=\"{F(e2s.Y)}\" x2=\"{F(e2e.X)}\" y2=\"{F(e2e.Y)}\" stroke=\"{dim.Color}\" stroke-width=\"{F(dim.LineWeight)}\"{StrokeExtras(dim)} />");
        // Arrowheads
        sb.Append(DimensionArrowheadSvg(ds, de, dim.ArrowSize, dim.Color, dim.LineWeight));
        sb.Append(DimensionArrowheadSvg(de, ds, dim.ArrowSize, dim.Color, dim.LineWeight));
        // Text
        // Negated for the same reason the label text above is: the element's scale(1,-1) cancels
        // the group's, so these coordinates are read in document space, where Y grows downward.
        sb.Append($"<text x=\"{F(tp.X)}\" y=\"{F(-tp.Y)}\" fill=\"{dim.Color}\" font-size=\"{F(dim.TextHeight)}\" text-anchor=\"middle\" transform=\"scale(1,-1)\">{dim.DisplayText}</text>");
        return $"<g>{sb}</g>";
    }

    private static string DimensionArrowheadSvg(VXYZ tip, VXYZ tail, double arrowSize, string color, double lineWeight)
    {
        // Shared geometry — this used to compute its own arrowSize/6 half-width (≈9.5°) while the
        // tessellator used 20°, so an SVG's dimension arrowheads did not match the drawing.
        var (w1, w2) = VArrow.ArrowheadWings(
            tip, tail, arrowSize, VDimension.DimensionArrowAngleDegrees);
        if (w1.IsAlmostEqualTo(tip) && w2.IsAlmostEqualTo(tip)) return "";

        return $"<polygon points=\"{F(tip.X)},{F(tip.Y)} {F(w1.X)},{F(w1.Y)} {F(w2.X)},{F(w2.Y)}\" fill=\"{color}\" stroke=\"{color}\" stroke-width=\"{F(lineWeight)}\"{StrokeExtras(LineType.Continuous, 1.0)} />";
    }

    private static string GroupToSvg(VGroup group)
    {
        var sb = new StringBuilder("<g>");
        foreach (var shape in group.Shapes)
        {
            var svgElement = ShapeToSvg(shape);
            if (!string.IsNullOrEmpty(svgElement))
                sb.Append(svgElement);
        }
        sb.Append("</g>");
        return sb.ToString();
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    
    private static string EscapeXml(string text) => 
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Saves shapes to an SVG file.
    /// </summary>
    public static void SaveToFile(string filePath, IEnumerable<IDrawable> shapes, double width = 800, double height = 600)
    {
        var svg = Export(shapes, width, height);
        System.IO.File.WriteAllText(filePath, svg);
    }

    /// <summary>One cell of a divided drawing: where it sits on the page, and the view it is showing.</summary>
    /// <param name="PageRect">The cell's rectangle on the page, in device pixels.</param>
    /// <param name="Scale">Screen pixels per world unit in that cell — that cell's own zoom.</param>
    /// <param name="PanX">The cell's horizontal pan, in pixels.</param>
    /// <param name="PanY">The cell's vertical pan, in pixels.</param>
    /// <param name="Shapes">The shapes placed on that cell.</param>
    public readonly record struct SvgTile(
        System.Windows.Rect PageRect,
        double Scale,
        double PanX,
        double PanY,
        IReadOnlyList<IDrawable> Shapes);

    /// <summary>
    /// Exports a divided drawing: every cell tiled onto one page exactly as it appears on screen,
    /// each at its own pan and zoom.
    ///
    /// <para>
    /// The transform per tile is derived from that cell's own view rather than re-computed here,
    /// which is what makes "as it appears on screen" literal instead of approximate. Shapes are
    /// still emitted in world coordinates; the matrix carries the cell's scale, its pan, its
    /// position on the page, and the Y flip from mathematical to screen coordinates all at once.
    /// </para>
    ///
    /// <para>
    /// This is deliberately <b>not</b> what an undivided drawing exports.
    /// <see cref="Export(IEnumerable{IDrawable}, double, double, double)"/> fits the <i>shapes</i>
    /// with padding and ignores the screen entirely — it exports the drawing, not the view. Those are
    /// different pictures, and switching the single-cell case to this one would silently change the
    /// output of every export that has ever been made.
    /// </para>
    /// </summary>
    public static string ExportTiled(IReadOnlyList<SvgTile> tiles, double width, double height)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\">");

        sb.AppendLine("  <defs>");
        for (var i = 0; i < tiles.Count; i++)
        {
            var r = tiles[i].PageRect;
            sb.AppendLine($"    <clipPath id=\"viewport{i}\"><rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" /></clipPath>");
        }
        sb.AppendLine("  </defs>");

        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var r = tile.PageRect;

            // world -> page:  x = scale*wx + (left + width/2 + panX)
            //                 y = -scale*wy + (top + height/2 + panY)
            var e = r.X + r.Width / 2 + tile.PanX;
            var f = r.Y + r.Height / 2 + tile.PanY;

            sb.AppendLine($"  <g clip-path=\"url(#viewport{i})\" transform=\"matrix({F(tile.Scale)} 0 0 {F(-tile.Scale)} {F(e)} {F(f)})\">");
            foreach (var shape in tile.Shapes)
            {
                var element = ShapeToSvg(shape);
                if (!string.IsNullOrEmpty(element)) sb.AppendLine("    " + element);
            }
            sb.AppendLine("  </g>");
        }

        // The cell separators, so the tiling is as legible in the file as it is on screen. Drawn last
        // so geometry cannot paint over them, and pinned to device pixels like every other stroke.
        if (tiles.Count > 1)
        {
            foreach (var tile in tiles)
            {
                var r = tile.PageRect;
                sb.AppendLine($"  <rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" fill=\"none\" stroke=\"#333333\" stroke-width=\"1\"{StrokeExtras(LineType.Continuous, 1.0)} />");
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>Saves a divided drawing, tiled as it appears on screen.</summary>
    public static void SaveTiledToFile(string filePath, IReadOnlyList<SvgTile> tiles, double width, double height)
        => System.IO.File.WriteAllText(filePath, ExportTiled(tiles, width, height));
}
