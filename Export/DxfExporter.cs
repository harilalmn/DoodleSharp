using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DoodleSharp.Canvas;
using C2VGeometry;

namespace DoodleSharp.Export;

/// <summary>
/// Exports shapes to AutoCAD DXF format (R12 ASCII).
/// </summary>
public class DxfExporter
{
    private readonly StringBuilder _sb = new();
    private int _handleCounter = 1;

    /// <summary>
    /// Exports shapes to a DXF file.
    /// </summary>
    public void Export(IReadOnlyList<IDrawable> shapes, string filePath)
    {
        _sb.Clear();
        _handleCounter = 1;

        WriteHeader();
        WriteTables();
        WriteBlocks();
        WriteEntities(shapes);
        WriteObjects();
        WriteEof();

        File.WriteAllText(filePath, _sb.ToString(), Encoding.ASCII);
    }

    /// <summary>
    /// Exports shapes to a DXF string.
    /// </summary>
    public string ExportToString(IReadOnlyList<IDrawable> shapes)
    {
        _sb.Clear();
        _handleCounter = 1;

        WriteHeader();
        WriteTables();
        WriteBlocks();
        WriteEntities(shapes);
        WriteObjects();
        WriteEof();

        return _sb.ToString();
    }

    private void WriteHeader()
    {
        WriteLine(0, "SECTION");
        WriteLine(2, "HEADER");
        WriteLine(9, "$ACADVER");
        WriteLine(1, "AC1009"); // R12 format
        WriteLine(9, "$INSUNITS");
        WriteLine(70, "0"); // Unitless
        WriteLine(0, "ENDSEC");
    }

    private void WriteTables()
    {
        WriteLine(0, "SECTION");
        WriteLine(2, "TABLES");

        // Layer table
        WriteLine(0, "TABLE");
        WriteLine(2, "LAYER");
        WriteLine(70, "1");
        WriteLine(0, "LAYER");
        WriteLine(2, "0");
        WriteLine(70, "0");
        WriteLine(62, "7"); // White color
        WriteLine(6, "CONTINUOUS");
        WriteLine(0, "ENDTAB");

        WriteLine(0, "ENDSEC");
    }

    private void WriteBlocks()
    {
        WriteLine(0, "SECTION");
        WriteLine(2, "BLOCKS");
        WriteLine(0, "ENDSEC");
    }

    private void WriteEntities(IReadOnlyList<IDrawable> shapes)
    {
        WriteLine(0, "SECTION");
        WriteLine(2, "ENTITIES");

        foreach (var drawable in shapes)
        {
            if (drawable is Shape shape)
            {
                WriteShape(shape);
            }
        }

        WriteLine(0, "ENDSEC");
    }

    private void WriteObjects()
    {
        WriteLine(0, "SECTION");
        WriteLine(2, "OBJECTS");
        WriteLine(0, "ENDSEC");
    }

    private void WriteEof()
    {
        WriteLine(0, "EOF");
    }

    private void WriteShape(Shape shape)
    {
        switch (shape)
        {
            case VPoint point:
                WritePoint(point);
                break;
            case VLine line:
                WriteLine(line);
                break;
            case VCircle circle:
                WriteCircle(circle);
                break;
            case VArc arc:
                WriteArc(arc);
                break;
            case VEllipse ellipse:
                WriteEllipse(ellipse);
                break;
            case VRectangle rect:
                WriteRectangle(rect);
                break;
            case VPolygon polygon:
                WritePolygon(polygon);
                break;
            case VPolyline polyline:
                WritePolyline(polyline);
                break;
            case VBezier bezier:
                WriteBezier(bezier);
                break;
            case VSpline spline:
                WriteSpline(spline);
                break;
            case VArrow arrow:
                WriteArrow(arrow);
                break;
            case VText text:
                WriteText(text);
                break;

            // Anything with no native DXF entity is flattened rather than dropped. Before this the
            // switch simply fell off the end and the shape vanished from the file with no error --
            // and because each exporter's switch was written separately, they covered different
            // subsets, so the same drawing could export correctly to SVG and lose shapes here.
            default:
                WriteTessellated(shape);
                break;
        }
    }

    private C2VGeometry.Rendering.ShapeTessellator? _fallbackTessellator;
    private C2VGeometry.Rendering.PolylineFallbackSink? _fallbackSink;

    private void WriteTessellated(Shape shape)
    {
        if (_fallbackSink == null)
        {
            _fallbackTessellator = new C2VGeometry.Rendering.ShapeTessellator();
            _fallbackSink = new C2VGeometry.Rendering.PolylineFallbackSink
            {
                OnPolyline = (pts, closed, _) =>
                {
                    var buffer = new List<(double x, double y)>(pts.Count);
                    for (int i = 0; i < pts.Count; i++) buffer.Add((pts[i].X, pts[i].Y));
                    if (buffer.Count >= 2) WriteLwPolyline(buffer, closed);
                },
                OnFilled = (loops, _) => { },   // the outline is emitted separately as a polyline
                OnPoint = (p, _) => WritePoint(new VPoint(p.X, p.Y)),
                OnText = WriteText,
            };
        }

        _fallbackTessellator!.Tessellate(shape, _fallbackSink);
    }

    private void WritePoint(VPoint point)
    {
        WriteLine(0, "POINT");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, point.X, point.Y, 0);
    }

    private void WriteLine(VLine line)
    {
        _sb.AppendLine("0");
        _sb.AppendLine("LINE");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, line.Start.X, line.Start.Y, 0);
        WriteCoord(11, 21, 31, line.End.X, line.End.Y, 0);
    }

    private void WriteCircle(VCircle circle)
    {
        WriteLine(0, "CIRCLE");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, circle.Center.X, circle.Center.Y, 0);
        WriteDouble(40, circle.Radius);
    }

    private void WriteArc(VArc arc)
    {
        WriteLine(0, "ARC");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, arc.Center.X, arc.Center.Y, 0);
        WriteDouble(40, arc.Radius);
        // DXF ARC entities are always traversed CCW from angle 50 to angle 51.
        // A VArc with EndAngle < StartAngle sweeps clockwise, so emit the angles
        // ascending — the CCW span [min, max] then covers the same arc segment.
        double startAngle = arc.StartAngle;
        double endAngle = arc.EndAngle;
        if (startAngle > endAngle) (startAngle, endAngle) = (endAngle, startAngle);
        WriteDouble(50, startAngle);
        WriteDouble(51, endAngle);
    }

    /// <summary>
    /// A <see cref="VEllipse"/> as a polyline — DXF R12 has no native ELLIPSE entity.
    /// </summary>
    /// <remarks>
    /// Samples the ellipse's actual sweep through <see cref="VEllipse.PointAtAngle"/>, so both a
    /// partial sweep and <see cref="VEllipse.Rotation"/> survive the export. It used to walk a full
    /// turn of the unrotated parametric form regardless, which turned every half ellipse into a
    /// whole one and laid every turned ellipse back down flat.
    /// </remarks>
    private void WriteEllipse(VEllipse ellipse)
    {
        double sweep = ellipse.EndAngle - ellipse.StartAngle;
        bool whole = Math.Abs(Math.Abs(sweep) - 360.0) < 1e-9 || Math.Abs(sweep) < 1e-9;
        double effective = Math.Abs(sweep) < 1e-9 ? 360.0 : sweep;

        var points = new List<(double x, double y)>();
        int segments = 72;
        for (int i = 0; i <= segments; i++)
        {
            var p = ellipse.PointAtAngle(ellipse.StartAngle + effective * i / segments);
            points.Add((p.X, p.Y));
        }

        // A closed LWPOLYLINE implies the closing segment, so the duplicated last vertex would draw
        // a zero-length one on top of the first.
        if (whole) points.RemoveAt(points.Count - 1);

        WriteLwPolyline(points, closed: whole);
    }

    /// <summary>
    /// A <see cref="VRectangle"/> as a closed LWPOLYLINE through its four corners.
    /// </summary>
    /// <remarks>
    /// Read straight off <see cref="VPolygon.Points"/>, which already carry the rectangle's
    /// <c>RotationAngle</c>. Rebuilding the corners from <c>Corner</c>, <c>Width</c> and
    /// <c>Height</c> — the previous implementation — silently dropped the rotation, so a rectangle
    /// drawn at an angle came back into CAD axis-aligned.
    /// </remarks>
    private void WriteRectangle(VRectangle rect)
    {
        var points = new List<(double x, double y)>(rect.Points.Count);
        foreach (var vertex in rect.Points)
            points.Add((vertex.X, vertex.Y));

        if (points.Count > 0)
            WriteLwPolyline(points, closed: true);
    }

    private void WritePolygon(VPolygon polygon)
    {
        var points = new List<(double x, double y)>();
        foreach (var vertex in polygon.Points)
        {
            points.Add((vertex.X, vertex.Y));
        }
        if (points.Count > 0)
        {
            WriteLwPolyline(points, closed: true);
        }
    }

    private void WritePolyline(VPolyline polyline)
    {
        var points = new List<(double x, double y)>();
        foreach (var vertex in polyline.Points)
        {
            points.Add((vertex.X, vertex.Y));
        }
        if (points.Count > 0)
        {
            WriteLwPolyline(points, closed: false);
        }
    }

    private void WriteBezier(VBezier bezier)
    {
        // Approximate bezier with polyline
        var points = new List<(double x, double y)>();
        int segments = 32;
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            var pt = EvaluateBezier(bezier, t);
            points.Add((pt.x, pt.y));
        }
        WriteLwPolyline(points, closed: false);
    }

    private (double x, double y) EvaluateBezier(VBezier bezier, double t)
    {
        double u = 1 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;

        double x = uuu * bezier.P0.X + 3 * uu * t * bezier.P1.X +
                   3 * u * tt * bezier.P2.X + ttt * bezier.P3.X;
        double y = uuu * bezier.P0.Y + 3 * uu * t * bezier.P1.Y +
                   3 * u * tt * bezier.P2.Y + ttt * bezier.P3.Y;

        return (x, y);
    }

    private void WriteSpline(VSpline spline)
    {
        // Export control points as polyline
        var points = new List<(double x, double y)>();
        foreach (var pt in spline.ControlPoints)
        {
            points.Add((pt.X, pt.Y));
        }
        if (points.Count > 0)
        {
            WriteLwPolyline(points, closed: false);
        }
    }

    private void WriteArrow(VArrow arrow)
    {
        // Write as line with arrowhead geometry
        _sb.AppendLine("0");
        _sb.AppendLine("LINE");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, arrow.Start.X, arrow.Start.Y, 0);
        WriteCoord(11, 21, 31, arrow.End.X, arrow.End.Y, 0);

        // Arrowhead geometry comes from VArrow, so a DXF matches the drawing. This used to hard-code
        // both the angle (30°) and the size (min(length * 0.2, 10)) — ignoring HeadLength and
        // HeadAngle entirely — and it dropped the second head of a double-ended arrow.
        WriteArrowHead(arrow, arrow.End, arrow.Start);
        if (arrow.DoubleEnded)
            WriteArrowHead(arrow, arrow.Start, arrow.End);
    }

    private void WriteArrowHead(VArrow arrow, VXYZ tip, VXYZ from)
    {
        var (wing1, wing2) = arrow.GetArrowheadPoints(tip, from);
        if (wing1.IsAlmostEqualTo(tip) && wing2.IsAlmostEqualTo(tip)) return;

        // Closed triangle, matching the solid head the canvas draws.
        WriteLineEntity(tip, wing1);
        WriteLineEntity(wing1, wing2);
        WriteLineEntity(wing2, tip);
    }

    private void WriteLineEntity(VXYZ from, VXYZ to)
    {
        _sb.AppendLine("0");
        _sb.AppendLine("LINE");
        WriteHandle();
        WriteLayer();
        WriteCoord(10, 20, 30, from.X, from.Y, 0);
        WriteCoord(11, 21, 31, to.X, to.Y, 0);
    }

    /// <summary>
    /// Vertical distance between the baselines of consecutive lines, as a multiple of the text
    /// height. DXF has no line-box concept to copy, so this approximates what the canvas does
    /// rather than claiming to match it exactly.
    /// </summary>
    private const double DxfLineSpacing = 1.2;

    /// <summary>
    /// Writes a <see cref="VText"/> as <b>one TEXT entity per line</b>.
    ///
    /// <para>
    /// A DXF group value is a whole line of the file, so writing <c>Content</c> straight into group
    /// 1 put any embedded newline into the file as a bare line — and the reader then took that line
    /// as the next group CODE and the entity stream desynchronised from there on. A multi-line
    /// label did not merely lose its line breaks, it produced a file no DXF reader could parse.
    /// TEXT has no multi-line form (MTEXT encodes breaks as <c>\P</c>), so the fix is one entity
    /// per line, stacked downwards from the location.
    /// </para>
    ///
    /// <para>
    /// The stacking direction follows <see cref="VText.Angle"/> — for rotated text "down" is
    /// perpendicular to the baseline, not world-down, so lines stay with their own label instead of
    /// marching across the drawing. <see cref="VText.Justify"/> is deliberately NOT honoured: every
    /// line is written at the same start point, because the only width available here is
    /// <c>GetBounds</c>'s character-count estimate, and offsetting lines by a wrong width would look
    /// like a bug rather than an approximation.
    /// </para>
    /// </summary>
    private void WriteText(VText text)
    {
        var content = text.Content ?? "";
        var lines = content.Replace("\r\n", "\n").Split('\n');

        // Rotated text stacks perpendicular to its own baseline: world-down for Angle 0.
        var rad = text.Angle * Math.PI / 180.0;
        var stepX = Math.Sin(rad) * text.Height * DxfLineSpacing;
        var stepY = -Math.Cos(rad) * text.Height * DxfLineSpacing;

        for (int i = 0; i < lines.Length; i++)
        {
            WriteLine(0, "TEXT");
            WriteHandle();
            WriteLayer();
            WriteCoord(10, 20, 30, text.Location.X + stepX * i, text.Location.Y + stepY * i, 0);
            WriteDouble(40, text.Height);
            WriteLine(1, lines[i]);
            if (text.Angle != 0)
                WriteDouble(50, text.Angle); // DXF text rotation is CCW degrees, same convention as world.
            WriteLine(7, "STANDARD");
        }
    }

    private void WriteLwPolyline(List<(double x, double y)> points, bool closed)
    {
        // Use R12-compatible POLYLINE format (not LWPOLYLINE which requires R14+)
        WriteLine(0, "POLYLINE");
        WriteHandle();
        WriteLayer();
        WriteLine(66, "1"); // Vertices follow flag
        WriteLine(70, closed ? "1" : "0"); // Closed flag
        WriteCoord(10, 20, 30, 0, 0, 0); // Base point

        foreach (var (x, y) in points)
        {
            WriteLine(0, "VERTEX");
            WriteHandle();
            WriteLayer();
            WriteCoord(10, 20, 30, x, y, 0);
        }

        WriteLine(0, "SEQEND");
        WriteHandle();
        WriteLayer();
    }

    private void WriteHandle()
    {
        WriteLine(5, _handleCounter.ToString("X"));
        _handleCounter++;
    }

    private void WriteLayer()
    {
        WriteLine(8, "0");
    }

    private void WriteLine(int code, string value)
    {
        _sb.AppendLine(code.ToString());
        _sb.AppendLine(value);
    }

    private void WriteDouble(int code, double value)
    {
        _sb.AppendLine(code.ToString());
        _sb.AppendLine(value.ToString("F6", CultureInfo.InvariantCulture));
    }

    private void WriteCoord(int xCode, int yCode, int zCode, double x, double y, double z)
    {
        WriteDouble(xCode, x);
        WriteDouble(yCode, y);
        WriteDouble(zCode, z);
    }
}
