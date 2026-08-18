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
            
            VEllipse e => $"<ellipse cx=\"{F(e.Center.X)}\" cy=\"{F(e.Center.Y)}\" rx=\"{F(e.RadiusX)}\" ry=\"{F(e.RadiusY)}\" fill=\"{e.FillColor}\" stroke=\"{e.Color}\" stroke-width=\"{F(e.LineWeight)}\"{StrokeExtras(e)} />",
            
            VRectangle r => $"<rect x=\"{F(r.Corner.X)}\" y=\"{F(r.Corner.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" fill=\"{r.FillColor}\" stroke=\"{r.Color}\" stroke-width=\"{F(r.LineWeight)}\"{StrokeExtras(r)} />",
            
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

    private static string TextToSvg(VText t)
    {
        var inner = $"<text x=\"{F(t.Location.X)}\" y=\"{F(t.Location.Y)}\" fill=\"{t.Color}\" font-size=\"{F(t.Height)}\" transform=\"scale(1,-1)\">{EscapeXml(t.Content)}</text>";
        if (t.Angle == 0) return inner;
        // World Angle is CCW (Y-up); parent group's scale(1,-1) flips Y, so we negate to keep CCW visually.
        return $"<g transform=\"rotate({F(-t.Angle)}, {F(t.Location.X)}, {F(t.Location.Y)})\">{inner}</g>";
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
        sb.Append($"<text x=\"{F(tp.X)}\" y=\"{F(tp.Y)}\" fill=\"{dim.Color}\" font-size=\"{F(dim.TextHeight)}\" text-anchor=\"middle\" transform=\"scale(1,-1)\">{dim.DisplayText}</text>");
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
}
