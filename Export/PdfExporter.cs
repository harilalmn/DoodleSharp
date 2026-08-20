using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using DoodleSharp.Canvas;
using C2VGeometry;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DoodleSharp.Export;

/// <summary>
/// Exports shapes to PDF format using PdfSharp.
/// </summary>
public class PdfExporter
{
    private double _margin = 20;
    private const double DipToPoint = 72.0 / 96.0;

    // Compensates display-unit elements (line weights, point markers) for
    // the ScaleTransform so they stay at their intended physical size.
    private double _uiSizeScale = 1.0;

    /// <summary>
    /// Exports shapes to a PDF file with auto-sized page.
    /// </summary>
    public void Export(IReadOnlyList<IDrawable> shapes, string filePath)
    {
        if (shapes.Count == 0) return;

        // Snapshot to avoid "collection was modified" during enumeration
        shapes = shapes.ToList();

        _uiSizeScale = 1.0;

        // Calculate bounds
        var (minPt, maxPt) = GetBounds(shapes);
        var width = maxPt.X - minPt.X + 2 * _margin;
        var height = maxPt.Y - minPt.Y + 2 * _margin;

        // Create PDF document
        var document = new PdfDocument();
        document.Info.Title = "DoodleSharp Export";

        // Create a page with appropriate size
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(Math.Max(width, 100));
        page.Height = XUnit.FromPoint(Math.Max(height, 100));

        using var gfx = XGraphics.FromPdfPage(page);

        // Transform: flip Y axis and translate
        gfx.TranslateTransform(_margin - minPt.X, page.Height.Point - _margin + minPt.Y);
        gfx.ScaleTransform(1, -1);

        // Draw shapes
        foreach (var drawable in shapes)
        {
            if (drawable is Shape shape && shape.IsVisible)
            {
                DrawShape(gfx, shape);
            }
        }

        // Save
        document.Save(filePath);
    }

    /// <summary>
    /// Exports shapes to a PDF file with specified page size, scale, and margins.
    /// </summary>
    /// <param name="shapes">Shapes to export.</param>
    /// <param name="filePath">Output file path.</param>
    /// <param name="pageWidthMm">Page width in mm (0 = auto-size to content).</param>
    /// <param name="pageHeightMm">Page height in mm (0 = auto-size to content).</param>
    /// <param name="scaleMmPerUnit">Scale factor: 1 drawing unit = this many mm on paper.</param>
    /// <param name="marginMm">Page margin in mm.</param>
    public void Export(IReadOnlyList<IDrawable> shapes, string filePath,
        double pageWidthMm, double pageHeightMm, double scaleMmPerUnit, double marginMm)
    {
        if (shapes.Count == 0) return;

        // Snapshot to avoid "collection was modified" during enumeration
        shapes = shapes.ToList();

        const double mmToPoints = 72.0 / 25.4;

        // Calculate content bounds in drawing units
        var (minPt, maxPt) = GetBounds(shapes);
        double contentW = maxPt.X - minPt.X;
        double contentH = maxPt.Y - minPt.Y;

        // Content size in mm
        double contentWMm = contentW * scaleMmPerUnit;
        double contentHMm = contentH * scaleMmPerUnit;

        // Determine page size in mm
        double pageW, pageH;
        if (pageWidthMm <= 0 || pageHeightMm <= 0)
        {
            // Auto-size: content + margins
            pageW = contentWMm + 2 * marginMm;
            pageH = contentHMm + 2 * marginMm;
        }
        else
        {
            pageW = pageWidthMm;
            pageH = pageHeightMm;
        }

        // Convert to PDF points
        double pageWPt = pageW * mmToPoints;
        double pageHPt = pageH * mmToPoints;
        double marginPt = marginMm * mmToPoints;
        double scalePtPerUnit = scaleMmPerUnit * mmToPoints;

        // Keep text/point marker sizes visually consistent even when geometry is scaled.
        _uiSizeScale = 1.0 / scalePtPerUnit;

        // Create PDF document
        var document = new PdfDocument();
        document.Info.Title = "DoodleSharp Export";

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(Math.Max(pageWPt, 10));
        page.Height = XUnit.FromPoint(Math.Max(pageHPt, 10));

        using var gfx = XGraphics.FromPdfPage(page);

        // Printable area in points
        double printableWPt = pageWPt - 2 * marginPt;
        double printableHPt = pageHPt - 2 * marginPt;

        // Content size in points (at scale)
        double contentWPt = contentW * scalePtPerUnit;
        double contentHPt = contentH * scalePtPerUnit;

        // Center content in printable area
        double offsetXPt = marginPt + (printableWPt - contentWPt) / 2;
        double offsetYPt = marginPt + (printableHPt - contentHPt) / 2;

        // Transform: translate to position content, apply scale, flip Y
        gfx.TranslateTransform(offsetXPt - minPt.X * scalePtPerUnit,
            page.Height.Point - offsetYPt + minPt.Y * scalePtPerUnit);
        gfx.ScaleTransform(scalePtPerUnit, -scalePtPerUnit);

        // Draw shapes
        foreach (var drawable in shapes)
        {
            if (drawable is Shape shape && shape.IsVisible)
            {
                DrawShape(gfx, shape);
            }
        }

        // Save
        document.Save(filePath);
    }

    /// <summary>One cell of a divided drawing: where it sits on the page, and the view it is showing.</summary>
    /// <param name="PageRect">The cell's rectangle within the on-screen container, in device pixels.</param>
    /// <param name="Scale">Screen pixels per world unit in that cell — that cell's own zoom.</param>
    /// <param name="PanX">The cell's horizontal pan, in pixels.</param>
    /// <param name="PanY">The cell's vertical pan, in pixels.</param>
    /// <param name="Shapes">The shapes placed on that cell.</param>
    public readonly record struct PdfTile(
        System.Windows.Rect PageRect,
        double Scale,
        double PanX,
        double PanY,
        IReadOnlyList<IDrawable> Shapes);

    /// <summary>
    /// Exports a divided drawing: every cell tiled onto one page exactly as it appears on screen,
    /// each at its own pan and zoom, fitted to the page as a whole.
    ///
    /// <para>
    /// The page keeps the container's aspect ratio and the cells keep their relative positions, so
    /// the result is the screen on paper. <c>ScaleMmPerUnit</c> has no meaning across cells at
    /// different zooms and is deliberately not offered here.
    /// </para>
    /// </summary>
    public void ExportTiled(IReadOnlyList<PdfTile> tiles, string filePath,
        double containerWidth, double containerHeight, double marginMm = 10)
    {
        if (tiles.Count == 0 || containerWidth <= 0 || containerHeight <= 0) return;

        const double mmToPoints = 72.0 / 25.4;
        var marginPt = marginMm * mmToPoints;

        // A4 landscape or portrait, whichever matches the container better — the drawing is a view,
        // not a measured plan, so fitting it to a familiar sheet beats inventing a page size.
        var landscape = containerWidth >= containerHeight;
        var pageWPt = (landscape ? 297.0 : 210.0) * mmToPoints;
        var pageHPt = (landscape ? 210.0 : 297.0) * mmToPoints;

        var document = new PdfDocument();
        document.Info.Title = "DoodleSharp Export";

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageWPt);
        page.Height = XUnit.FromPoint(pageHPt);

        using var gfx = XGraphics.FromPdfPage(page);

        // One fit for the whole container, so the cells keep their proportions to each other.
        var fit = Math.Min((pageWPt - 2 * marginPt) / containerWidth,
                           (pageHPt - 2 * marginPt) / containerHeight);
        var originX = (pageWPt - containerWidth * fit) / 2;
        var originY = (pageHPt - containerHeight * fit) / 2;

        foreach (var tile in tiles)
        {
            var r = tile.PageRect;

            var state = gfx.Save();

            gfx.IntersectClip(new XRect(
                originX + r.X * fit, originY + r.Y * fit, r.Width * fit, r.Height * fit));

            // world -> page, in one composition: the cell's own view, then the cell's position in
            // the container, then the container's fit to the sheet.
            gfx.TranslateTransform(
                originX + (r.X + r.Width / 2 + tile.PanX) * fit,
                originY + (r.Y + r.Height / 2 + tile.PanY) * fit);
            gfx.ScaleTransform(tile.Scale * fit, -tile.Scale * fit);

            // Line weights and point markers are device sizes, so they must be divided back out of
            // the transform or a zoomed-in cell would print with fat strokes. Recomputed per tile
            // because every cell has its own zoom — a single value would be wrong for all but one.
            _uiSizeScale = 1.0 / (tile.Scale * fit);

            foreach (var drawable in tile.Shapes)
            {
                if (drawable is Shape shape && shape.IsVisible) DrawShape(gfx, shape);
            }

            gfx.Restore(state);
        }

        // The cell separators, so the tiling is as legible on paper as on screen.
        if (tiles.Count > 1)
        {
            var pen = new XPen(XColors.Gray, 0.5);
            foreach (var tile in tiles)
            {
                var r = tile.PageRect;
                gfx.DrawRectangle(pen, new XRect(
                    originX + r.X * fit, originY + r.Y * fit, r.Width * fit, r.Height * fit));
            }
        }

        document.Save(filePath);
    }

    private BoundingBox GetBounds(IReadOnlyList<IDrawable> shapes)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var drawable in shapes)
        {
            if (drawable is Shape shape)
            {
                var bounds = shape.GetBounds();
                minX = Math.Min(minX, bounds.Min.X);
                minY = Math.Min(minY, bounds.Min.Y);
                maxX = Math.Max(maxX, bounds.Max.X);
                maxY = Math.Max(maxY, bounds.Max.Y);
            }
        }

        if (minX == double.MaxValue)
        {
            return new BoundingBox(new VXYZ(0, 0), new VXYZ(100, 100));
        }

        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }

    private void DrawShape(XGraphics gfx, Shape shape)
    {
        var pen = CreatePen(shape);
        var brush = CreateBrush(shape);

        switch (shape)
        {
            case VRadialDimension radDim:
                DrawRadialDimension(gfx, radDim);
                break;
            case VDimension dim:
                DrawDimension(gfx, dim);
                break;
            case VPoint point:
                if (ShouldExportPoint(point))
                {
                    DrawPoint(gfx, point, pen, brush);
                }
                break;
            case VLine line:
                DrawLine(gfx, line, pen);
                break;
            case VCircle circle:
                DrawCircle(gfx, circle, pen, brush);
                break;
            case VArc arc:
                DrawArc(gfx, arc, pen);
                break;
            case VEllipse ellipse:
                DrawEllipse(gfx, ellipse, pen, brush);
                break;
            case VRectangle rect:
                DrawRectangle(gfx, rect, pen, brush);
                break;
            case VPolygon polygon:
                DrawPolygon(gfx, polygon, pen, brush);
                break;
            case VPolyline polyline:
                DrawPolyline(gfx, polyline, pen);
                break;
            case VBezier bezier:
                DrawBezier(gfx, bezier, pen);
                break;
            case VSpline spline:
                DrawSpline(gfx, spline, pen);
                break;
            case VArrow arrow:
                DrawArrow(gfx, arrow, pen);
                break;
            case VText text:
                DrawText(gfx, text);
                break;
            case VHatch hatch:
                DrawHatch(gfx, hatch, pen);
                break;

            // No native PDF form: flatten rather than drop. See the note in DxfExporter.
            default:
                DrawTessellated(gfx, shape, pen);
                break;
        }
    }

    private C2VGeometry.Rendering.ShapeTessellator? _fallbackTessellator;

    private void DrawTessellated(XGraphics gfx, Shape shape, XPen pen)
    {
        _fallbackTessellator ??= new C2VGeometry.Rendering.ShapeTessellator();

        var sink = new C2VGeometry.Rendering.PolylineFallbackSink
        {
            OnPolyline = (pts, closed, _) =>
            {
                if (pts.Count < 2) return;
                var last = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < last; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    gfx.DrawLine(pen, new XPoint(a.X, a.Y), new XPoint(b.X, b.Y));
                }
            },
            OnPoint = (p, _) => gfx.DrawLine(pen, new XPoint(p.X, p.Y), new XPoint(p.X, p.Y)),
            OnText = t => DrawText(gfx, t),
        };

        _fallbackTessellator.Tessellate(shape, sink);
    }

    private void DrawHatch(XGraphics gfx, VHatch hatch, XPen pen)
    {
        if (hatch.Boundary.Count < 3) return;
        var lines = hatch.GenerateLines();
        foreach (var (start, end) in lines)
        {
            gfx.DrawLine(pen,
                new XPoint(start.X, -start.Y),
                new XPoint(end.X, -end.Y));
        }
    }

    private XPen CreatePen(Shape shape)
    {
        var color = ParseColor(shape.Color);
        // LineWeight is in WPF DIPs (display pixels); convert to points and
        // compensate for the geometry ScaleTransform so strokes stay a fixed
        // physical width on paper.
        return new XPen(color, Math.Max(shape.LineWeight * DipToPoint * _uiSizeScale, 0.001));
    }

    private XPen CreatePen(string colorName, double lineWeight)
    {
        var color = ParseColor(colorName);
        return new XPen(color, Math.Max(lineWeight * DipToPoint * _uiSizeScale, 0.001));
    }

    private XBrush? CreateBrush(Shape shape)
    {
        if (string.IsNullOrEmpty(shape.FillColor) ||
            shape.FillColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var color = ParseColor(shape.FillColor);
        return new XSolidBrush(color);
    }

    /// <summary>
    /// Parses a color string using WPF's ColorConverter for exact color matching
    /// with the canvas rendering, then converts to PdfSharp XColor.
    /// </summary>
    private XColor ParseColor(string colorName)
    {
        if (string.IsNullOrEmpty(colorName))
            return XColors.Black;

        // Use WPF's ColorConverter (same parser the canvas uses),
        // so named colors and hex values resolve identically.
        try
        {
            var wpfColor = (Color)ColorConverter.ConvertFromString(colorName);
            return XColor.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
        }
        catch
        {
            // Fallback: should rarely happen since WPF ColorConverter
            // handles all named colors and hex formats.
            return XColors.Black;
        }
    }

    private void DrawPoint(XGraphics gfx, VPoint point, XPen pen, XBrush? brush)
    {
        double r = 2 * _uiSizeScale;
        if (brush != null)
        {
            gfx.DrawEllipse(brush, point.X - r, point.Y - r, r * 2, r * 2);
        }
        gfx.DrawEllipse(pen, point.X - r, point.Y - r, r * 2, r * 2);
    }

    private void DrawLine(XGraphics gfx, VLine line, XPen pen)
    {
        gfx.DrawLine(pen, line.Start.X, line.Start.Y, line.End.X, line.End.Y);
    }

    private void DrawCircle(XGraphics gfx, VCircle circle, XPen pen, XBrush? brush)
    {
        var x = circle.Center.X - circle.Radius;
        var y = circle.Center.Y - circle.Radius;
        var size = circle.Radius * 2;

        if (brush != null)
        {
            gfx.DrawEllipse(brush, x, y, size, size);
        }
        gfx.DrawEllipse(pen, x, y, size, size);
    }

    private void DrawArc(XGraphics gfx, VArc arc, XPen pen)
    {
        var x = arc.Center.X - arc.Radius;
        var y = arc.Center.Y - arc.Radius;
        var size = arc.Radius * 2;

        // PdfSharp uses clockwise angles, so we may need to adjust
        double startAngle = -arc.StartAngle; // Negate for Y-flip
        double sweepAngle = -(arc.EndAngle - arc.StartAngle);

        gfx.DrawArc(pen, x, y, size, size, startAngle, sweepAngle);
    }

    /// <summary>
    /// A <see cref="VEllipse"/>: PDFsharp's own ellipse for a whole, unrotated one, and a sampled
    /// path for anything else.
    /// </summary>
    /// <remarks>
    /// <c>XGraphics.DrawEllipse</c> takes an axis-aligned box, so it can express neither a sweep
    /// nor an orientation. Passing one anyway — which is what this did — exported a half ellipse as
    /// a whole one and a turned ellipse flat, silently disagreeing with the canvas.
    /// </remarks>
    private void DrawEllipse(XGraphics gfx, VEllipse ellipse, XPen pen, XBrush? brush)
    {
        var sweep = ellipse.EndAngle - ellipse.StartAngle;
        var whole = Math.Abs(Math.Abs(sweep) - 360.0) < 1e-9 || Math.Abs(sweep) < 1e-9;

        if (whole && ellipse.Rotation == 0)
        {
            var x = ellipse.Center.X - ellipse.RadiusX;
            var y = ellipse.Center.Y - ellipse.RadiusY;

            if (brush != null)
            {
                gfx.DrawEllipse(brush, x, y, ellipse.RadiusX * 2, ellipse.RadiusY * 2);
            }
            gfx.DrawEllipse(pen, x, y, ellipse.RadiusX * 2, ellipse.RadiusY * 2);
            return;
        }

        const int segments = 72;
        var effective = Math.Abs(sweep) < 1e-9 ? 360.0 : sweep;
        var points = new XPoint[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            var pt = ellipse.PointAtAngle(ellipse.StartAngle + effective * (i / (double)segments));
            points[i] = new XPoint(pt.X, pt.Y);
        }

        if (whole)
        {
            if (brush != null) gfx.DrawPolygon(brush, points, XFillMode.Alternate);
            gfx.DrawPolygon(pen, points);
        }
        else
        {
            gfx.DrawLines(pen, points);
        }
    }

    /// <summary>
    /// A <see cref="VRectangle"/>, drawn from its four <see cref="VPolygon.Points"/>.
    /// </summary>
    /// <remarks>
    /// Those points already carry the rectangle's <c>RotationAngle</c>. Rebuilding the box from
    /// <c>Corner</c>, <c>Width</c> and <c>Height</c> instead — which is what this did — threw the
    /// rotation away, so a rectangle drawn at an angle on the canvas exported square to the page.
    /// </remarks>
    private void DrawRectangle(XGraphics gfx, VRectangle rect, XPen pen, XBrush? brush)
    {
        if (Math.Abs(rect.RotationAngle) < 1e-9)
        {
            if (brush != null)
            {
                gfx.DrawRectangle(brush, rect.Corner.X, rect.Corner.Y, rect.Width, rect.Height);
            }
            gfx.DrawRectangle(pen, rect.Corner.X, rect.Corner.Y, rect.Width, rect.Height);
            return;
        }

        DrawPolygon(gfx, rect, pen, brush);
    }

    private void DrawPolygon(XGraphics gfx, VPolygon polygon, XPen pen, XBrush? brush)
    {
        if (polygon.Points.Count < 2) return;

        var points = new XPoint[polygon.Points.Count];
        for (int i = 0; i < polygon.Points.Count; i++)
        {
            points[i] = new XPoint(polygon.Points[i].X, polygon.Points[i].Y);
        }

        if (brush != null)
        {
            gfx.DrawPolygon(brush, points, XFillMode.Winding);
        }
        gfx.DrawPolygon(pen, points);
    }

    private void DrawPolyline(XGraphics gfx, VPolyline polyline, XPen pen)
    {
        if (polyline.Points.Count < 2) return;

        for (int i = 0; i < polyline.Points.Count - 1; i++)
        {
            gfx.DrawLine(pen,
                polyline.Points[i].X, polyline.Points[i].Y,
                polyline.Points[i + 1].X, polyline.Points[i + 1].Y);
        }
    }

    private void DrawBezier(XGraphics gfx, VBezier bezier, XPen pen)
    {
        gfx.DrawBezier(pen,
            bezier.P0.X, bezier.P0.Y,
            bezier.P1.X, bezier.P1.Y,
            bezier.P2.X, bezier.P2.Y,
            bezier.P3.X, bezier.P3.Y);
    }

    private void DrawSpline(XGraphics gfx, VSpline spline, XPen pen)
    {
        if (spline.ControlPoints.Count < 2) return;

        // Draw as polyline through control points (approximate)
        for (int i = 0; i < spline.ControlPoints.Count - 1; i++)
        {
            gfx.DrawLine(pen,
                spline.ControlPoints[i].X, spline.ControlPoints[i].Y,
                spline.ControlPoints[i + 1].X, spline.ControlPoints[i + 1].Y);
        }
    }

    private void DrawArrow(XGraphics gfx, VArrow arrow, XPen pen)
    {
        // Draw main line
        gfx.DrawLine(pen, arrow.Start.X, arrow.Start.Y, arrow.End.X, arrow.End.Y);

        // Arrowhead geometry comes from VArrow, the one place it is defined, so a PDF matches what
        // was on screen. This used to compute its own wings AND clamp the head to 20% of the shaft
        // length, so a short arrow exported with a smaller head than it was drawn with; it also
        // ignored DoubleEnded, silently dropping the second head.
        DrawArrowHead(gfx, arrow, arrow.End, arrow.Start, pen);
        if (arrow.DoubleEnded)
            DrawArrowHead(gfx, arrow, arrow.Start, arrow.End, pen);
    }

    private void DrawArrowHead(XGraphics gfx, VArrow arrow, VXYZ tip, VXYZ from, XPen pen)
    {
        var (wing1, wing2) = arrow.GetArrowheadPoints(tip, from);
        if (wing1.IsAlmostEqualTo(tip) && wing2.IsAlmostEqualTo(tip)) return;

        // Filled in the STROKE colour, matching RenderCanvas.DrawArrow, SvgExporter and this
        // exporter's own DrawDimensionArrowhead. Stroking the three edges instead would make an
        // arrow's head hollow in a PDF while a dimension's head on the same page was solid.
        // XGraphicsPath rather than DrawPolygon: reliable under the Y-flipped transform.
        var path = new XGraphicsPath();
        path.AddPolygon([
            new XPoint(tip.X, tip.Y),
            new XPoint(wing1.X, wing1.Y),
            new XPoint(wing2.X, wing2.Y),
        ]);
        gfx.DrawPath(new XSolidBrush(ParseColor(arrow.Color)), path);
    }

    private void DrawText(XGraphics gfx, VText text)
    {
        var color = ParseColor(text.Color);
        var brush = new XSolidBrush(color);
        var fontFamily = text.Font switch
        {
            VFont.TimesNewRoman => "Times New Roman",
            VFont.CourierNew => "Courier New",
            VFont.Consolas => "Consolas",
            _ => "Arial"
        };
        var fontStyle = text.FontWeight == VFontWeight.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
        var font = new XFont(fontFamily, Math.Max(text.Height, 0.1), fontStyle);

        // Laid out line by line, because PDF has no concept of a line break inside a run:
        // DrawString would have put a two-line label onto a single line, so a multi-line
        // label -- exactly what VText.Justify exists to encourage -- exported as one
        // long unreadable row.
        var lines = SplitLines(text.Content);
        var lineWidths = new double[lines.Length];
        var measuredWidth = 0.0;
        for (int i = 0; i < lines.Length; i++)
        {
            lineWidths[i] = gfx.MeasureString(lines[i], font).Width;
            if (lineWidths[i] > measuredWidth) measuredWidth = lineWidths[i];
        }

        // Matches VText.MeasureBlock exactly -- only the GAPS are scaled -- so the plate, the
        // anchor offset and the box GetBounds reserves all describe the same rectangle.
        var measuredHeight = text.Height * (1 + (lines.Length - 1) * VText.LineSpacing);
        var (anchorOffsetX, anchorOffsetY) = text.GetAnchorOffset(measuredWidth, measuredHeight);

        // Text drawing with Y-flip correction. Angle rotates around Location (CCW in world Y-up).
        gfx.Save();
        gfx.TranslateTransform(text.Location.X, text.Location.Y);
        if (text.Angle != 0)
            gfx.RotateTransform(text.Angle); // Outer scale(1,-1) makes RotateTransform CCW in world coords.
        gfx.TranslateTransform(anchorOffsetX, anchorOffsetY);

        // The mask is drawn before the un-flip, so its rectangle is in world (Y-up) coordinates
        // running from the text baseline upward, and before the glyphs so it renders underneath
        // them. Padding is a fraction of the text height, matching the canvas.
        if (text.Mask)
        {
            // A fraction of the TEXT height, not of the block's: MaskOffset's contract is "as a
            // fraction of the text height", and the canvas reads it that way, so measuring it
            // against the whole block gave a three-line label three times the intended padding.
            var pad = text.MaskOffset * text.Height;
            // Null means "the canvas background"; with no canvas here, that is whatever the host
            // last published (see VText.CanvasBackgroundColor).
            var maskColour = string.IsNullOrEmpty(text.MaskColor)
                ? VText.CanvasBackgroundColor
                : text.MaskColor;
            gfx.DrawRectangle(new XSolidBrush(ParseColor(maskColour)),
                -pad, -pad, measuredWidth + 2 * pad, measuredHeight + 2 * pad);
        }

        gfx.ScaleTransform(1, -1); // Un-flip for text

        // Y grows downward inside the un-flipped frame, and the block's origin is its bottom-left,
        // so the first line sits (n-1) line heights above the baseline. Justify shifts each line
        // inside the block's own width -- the same rule the canvas applies via TextAlignment.
        for (int i = 0; i < lines.Length; i++)
        {
            var slack = measuredWidth - lineWidths[i];
            var justifyOffset = text.Justify switch
            {
                VTextJustify.Center => slack / 2,
                VTextJustify.Right => slack,
                _ => 0.0
            };
            var baseline = -(lines.Length - 1 - i) * text.Height * VText.LineSpacing;
            gfx.DrawString(lines[i], font, brush, justifyOffset, baseline);
        }

        gfx.Restore();
    }

    /// <summary>
    /// Splits a label into its lines, tolerating any of the three line-ending conventions and
    /// never returning an empty array — a label with no content is still one (empty) line, which
    /// keeps the height arithmetic above from collapsing to zero.
    /// </summary>
    private static string[] SplitLines(string? content)
    {
        if (string.IsNullOrEmpty(content)) return new[] { string.Empty };
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private void DrawRadialDimension(XGraphics gfx, VRadialDimension dim)
    {
        var (leaderStart, leaderEnd, textPos) = dim.GetDimensionGeometry();

        string dimLineColor = dim.DimensionLineColor ?? dim.Color;
        string textColorName = dim.TextColor ?? dim.Color;

        var dimPen = CreatePen(dimLineColor, dim.LineWeight);
        var dimBrush = new XSolidBrush(ParseColor(dimLineColor));
        string displayText = dim.DisplayText;

        // Leader line with text gap
        var dimDx = leaderEnd.X - leaderStart.X;
        var dimDy = leaderEnd.Y - leaderStart.Y;
        var dimLength = Math.Sqrt(dimDx * dimDx + dimDy * dimDy);
        if (dimLength > 1e-10)
        {
            var gapFont = new XFont("Arial", Math.Max(dim.TextHeight, 0.1), XFontStyleEx.Regular);
            var textSizeForGap = gfx.MeasureString(displayText, gapFont);
            var textWorldWidth = textSizeForGap.Width;
            var padding = textWorldWidth * 0.15;
            var halfGap = textWorldWidth / 2 + padding;

            var dirX = dimDx / dimLength;
            var dirY = dimDy / dimLength;
            var midX = (leaderStart.X + leaderEnd.X) / 2;
            var midY = (leaderStart.Y + leaderEnd.Y) / 2;

            gfx.DrawLine(dimPen, leaderStart.X, leaderStart.Y,
                midX - dirX * halfGap, midY - dirY * halfGap);
            gfx.DrawLine(dimPen, midX + dirX * halfGap, midY + dirY * halfGap,
                leaderEnd.X, leaderEnd.Y);
        }
        else
        {
            gfx.DrawLine(dimPen, leaderStart.X, leaderStart.Y, leaderEnd.X, leaderEnd.Y);
        }

        // Arrowhead at circumference
        DrawDimensionArrowhead(gfx, dimBrush, leaderEnd, leaderStart, dim.ArrowSize);
        if (dim.ShowDiameter)
            DrawDimensionArrowhead(gfx, dimBrush, leaderStart, leaderEnd, dim.ArrowSize);

        // Text
        var textColor = ParseColor(textColorName);
        var textBrush = new XSolidBrush(textColor);
        var fontSize = dim.TextHeight;
        var font = new XFont("Arial", Math.Max(fontSize, 0.1), XFontStyleEx.Regular);
        var textSize = gfx.MeasureString(displayText, font);

        gfx.Save();
        gfx.TranslateTransform(textPos.X, textPos.Y);
        gfx.ScaleTransform(1, -1);

        if (dim.TextBackgroundOpaque)
        {
            gfx.DrawRectangle(XBrushes.White,
                -textSize.Width / 2, -textSize.Height / 2, textSize.Width, textSize.Height);
        }

        gfx.DrawString(displayText, font, textBrush, 0, -textSize.Height / 2,
            XStringFormats.TopCenter);
        gfx.Restore();
    }

    private void DrawDimension(XGraphics gfx, VDimension dim)
    {
        var geom = dim.GetDimensionGeometry();

        string extColor = dim.ExtensionLineColor ?? dim.Color;
        string dimLineColor = dim.DimensionLineColor ?? dim.Color;
        string textColorName = dim.TextColor ?? dim.Color;

        var extPen = CreatePen(extColor, dim.LineWeight);
        var dimPen = CreatePen(dimLineColor, dim.LineWeight);
        var dimBrush = new XSolidBrush(ParseColor(dimLineColor));
        string displayText = dim.DisplayText;

        // Extension lines
        if (!dim.SuppressExtLine1)
            gfx.DrawLine(extPen, geom.ext1Start.X, geom.ext1Start.Y, geom.ext1End.X, geom.ext1End.Y);
        if (!dim.SuppressExtLine2)
            gfx.DrawLine(extPen, geom.ext2Start.X, geom.ext2Start.Y, geom.ext2End.X, geom.ext2End.Y);

        // Dimension line and arrowheads
        if (!dim.SuppressDimensionLine)
        {
            // Mirror canvas behavior: split the dimension line around the text gap.
            var dimDx = geom.dimEnd.X - geom.dimStart.X;
            var dimDy = geom.dimEnd.Y - geom.dimStart.Y;
            var dimLength = Math.Sqrt(dimDx * dimDx + dimDy * dimDy);
            if (dimLength > 1e-10)
            {
                var gapFont = new XFont("Arial", Math.Max(dim.TextHeight, 0.1), XFontStyleEx.Regular);
                var textSizeForGap = gfx.MeasureString(displayText, gapFont);
                var textWorldWidth = textSizeForGap.Width;
                var padding = textWorldWidth * 0.15;
                var halfGap = textWorldWidth / 2 + padding;

                var dirX = dimDx / dimLength;
                var dirY = dimDy / dimLength;
                var midX = (geom.dimStart.X + geom.dimEnd.X) / 2;
                var midY = (geom.dimStart.Y + geom.dimEnd.Y) / 2;

                var gapStartX = midX - dirX * halfGap;
                var gapStartY = midY - dirY * halfGap;
                var gapEndX = midX + dirX * halfGap;
                var gapEndY = midY + dirY * halfGap;

                gfx.DrawLine(dimPen, geom.dimStart.X, geom.dimStart.Y, gapStartX, gapStartY);
                gfx.DrawLine(dimPen, gapEndX, gapEndY, geom.dimEnd.X, geom.dimEnd.Y);
            }
            else
            {
                gfx.DrawLine(dimPen, geom.dimStart.X, geom.dimStart.Y, geom.dimEnd.X, geom.dimEnd.Y);
            }

            // Filled arrowheads — in drawing units, scale with geometry
            // (matches canvas WorldToScreen behavior).
            DrawDimensionArrowhead(gfx, dimBrush, geom.dimStart, geom.dimEnd, dim.ArrowSize);
            DrawDimensionArrowhead(gfx, dimBrush, geom.dimEnd, geom.dimStart, dim.ArrowSize);
        }

        // Text – font size in drawing units; the global ScaleTransform scales it to paper size.
        var textColor = ParseColor(textColorName);
        var textBrush = new XSolidBrush(textColor);
        var fontSize = dim.TextHeight;
        var font = new XFont("Arial", Math.Max(fontSize, 0.1), XFontStyleEx.Regular);
        var textSize = gfx.MeasureString(displayText, font);

        gfx.Save();
        gfx.TranslateTransform(geom.textPos.X, geom.textPos.Y);
        gfx.ScaleTransform(1, -1); // Un-flip for text

        if (dim.TextBackgroundOpaque)
        {
            gfx.DrawRectangle(XBrushes.White,
                -textSize.Width / 2, -textSize.Height / 2, textSize.Width, textSize.Height);
        }

        gfx.DrawString(displayText, font, textBrush, 0, -textSize.Height / 2,
            XStringFormats.TopCenter);
        gfx.Restore();
    }

    private static void DrawDimensionArrowhead(XGraphics gfx, XBrush brush, VXYZ tipPoint, VXYZ tailPoint, double arrowSize)
    {
        // Shared geometry — this used to use a fixed arrowSize/6 half-width (≈9.5°) while the
        // tessellator drew dimension heads at 20°, so a PDF did not match the drawing.
        var (w1, w2) = VArrow.ArrowheadWings(
            tipPoint, tailPoint, arrowSize, VDimension.DimensionArrowAngleDegrees);
        if (w1.IsAlmostEqualTo(tipPoint) && w2.IsAlmostEqualTo(tipPoint)) return;

        var tip = new XPoint(tipPoint.X, tipPoint.Y);
        var wing1 = new XPoint(w1.X, w1.Y);
        var wing2 = new XPoint(w2.X, w2.Y);

        // Use XGraphicsPath for reliable filled rendering under Y-flipped transforms.
        var path = new XGraphicsPath();
        path.AddPolygon([tip, wing1, wing2]);
        gfx.DrawPath(brush, path);
    }

    private static bool ShouldExportPoint(VPoint point)
    {
        // Most leaked helper points are auto-registered with default point styling.
        // Keep explicit points, and keep any styled points likely intended by the user.
        if (point.IsExplicitlyDrawn)
            return true;

        string defaultPointColor = ShapeDefaults.GlobalColor ?? "White";
        string defaultPointFill = ShapeDefaults.GlobalFillColor ?? "LimeGreen";
        double defaultPointWeight = ShapeDefaults.GlobalLineWeight ?? 2.0;

        bool hasDefaultAppearance =
            string.Equals(point.Color, defaultPointColor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(point.FillColor, defaultPointFill, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(point.LineWeight - defaultPointWeight) < 1e-9;

        return !hasDefaultAppearance;
    }
}

