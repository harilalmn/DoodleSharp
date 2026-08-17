using System;
using System.Collections.Generic;

namespace C2VGeometry.Rendering;

/// <summary>
/// The one place a shape is turned into drawable primitives.
///
/// <para>
/// Everything that used to type-switch over <c>V*</c> types to draw or export should route through
/// here. The instance holds scratch buffers and is reused across shapes and frames, because this
/// runs once per visible shape per frame and allocating a point list per curve is precisely the
/// cost this whole exercise is removing.
/// </para>
///
/// <para>
/// <b>Not thread-safe</b>, by design — the buffers are the point. Give each thread its own.
/// </para>
/// </summary>
public sealed class ShapeTessellator
{
    private readonly List<VXYZ> _points = new(256);
    private readonly List<IReadOnlyList<VXYZ>> _loops = new(8);

    /// <summary>
    /// How deep <see cref="VGroup"/> recursion may go before it is abandoned. Groups can nest
    /// arbitrarily and nothing prevents a user constructing a cycle; without a limit that is a
    /// stack overflow, which .NET cannot catch.
    /// </summary>
    private const int MaxGroupDepth = 32;

    /// <summary>
    /// Emits <paramref name="shape"/> to <paramref name="sink"/>.
    /// </summary>
    /// <returns>
    /// False if the shape is not something this tessellator produces primitives for, so the caller
    /// can fall back to a renderer that handles it. Returning false is not an error — it is how a
    /// partial fast path coexists with a complete slow one.
    /// </returns>
    public bool Tessellate(Shape shape, IPrimitiveSink sink) => Tessellate(shape, sink, 0);

    private bool Tessellate(Shape shape, IPrimitiveSink sink, int depth)
    {
        if (shape is VGroup group)
        {
            if (depth >= MaxGroupDepth) return false;

            var all = true;
            foreach (var child in group.Shapes)
            {
                if (child is Shape s && s.IsVisible)
                    all &= Tessellate(s, sink, depth + 1);
            }
            return all;
        }

        var pen = PenSpec.From(shape);
        if (!sink.BeginShape(shape, pen)) return false;

        try
        {
            if (sink.Hints.PreferNative && sink.TryEmitNative(shape, pen))
                return true;

            return Emit(shape, sink, pen);
        }
        finally
        {
            sink.EndShape();
        }
    }

    private bool Emit(Shape shape, IPrimitiveSink sink, in PenSpec pen)
    {
        var scale = sink.Hints.Scale;

        switch (shape)
        {
            case VPoint p:
                sink.EmitPoint(new VXYZ(p.X, p.Y));
                return true;

            case VLine line:
                _points.Clear();
                _points.Add(line.Start);
                _points.Add(line.End);
                sink.EmitPolyline(_points, closed: false);
                return true;

            case VText text:
                sink.EmitText(text);
                return true;

            // VRectangle and VCell are VPolygon subclasses, so this covers them too.
            case VPolygon polygon:
                return EmitClosed(polygon.Points, pen, sink);

            case VPolyline polyline:
                if (polyline.Points == null || polyline.Points.Count < 2) return true;
                sink.EmitPolyline(polyline.Points, IsClosed(polyline.Points));
                return true;

            case VCircle circle:
                SampleEllipse(circle.Center, circle.Radius, circle.Radius, 0, 360, scale);
                return EmitClosed(_points, pen, sink);

            case VEllipse ellipse:
            {
                var full = Math.Abs(ellipse.EndAngle - ellipse.StartAngle) >= 359.999;
                SampleEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY,
                              ellipse.StartAngle, ellipse.EndAngle, scale);
                if (full) return EmitClosed(_points, pen, sink);
                sink.EmitPolyline(_points, closed: false);
                return true;
            }

            case VArc arc:
                SampleEllipse(arc.Center, arc.Radius, arc.Radius, arc.StartAngle, arc.EndAngle, scale);
                sink.EmitPolyline(_points, closed: false);
                return true;

            case VHatch hatch:
            {
                // Already-generated segments, memoised on the shape. Emitted as independent
                // two-point runs rather than one polyline — hatch lines are disjoint.
                foreach (var (start, end) in hatch.GetCachedLines())
                {
                    _points.Clear();
                    _points.Add(start);
                    _points.Add(end);
                    sink.EmitPolyline(_points, closed: false);
                }
                return true;
            }

            case Region region:
            {
                var radiusPx = Math.Max(region.GetBounds().Width, region.GetBounds().Height) * 0.5 * scale;
                region.GetCachedOutline(SegmentsForRadius(radiusPx), out var outer, out var holes);

                if (pen.HasFill)
                {
                    _loops.Clear();
                    _loops.Add(outer);
                    foreach (var hole in holes) _loops.Add(hole);
                    sink.EmitFilledLoops(_loops, FillRule.EvenOdd);
                }

                sink.EmitPolyline(outer, closed: true);
                foreach (var hole in holes) sink.EmitPolyline(hole, closed: true);
                return true;
            }

            case VBezier bezier:
                _points.Clear();
                foreach (var pt in bezier.Divide(SegmentsForLength(bezier, scale))) _points.Add(pt);
                sink.EmitPolyline(_points, closed: false);
                return true;

            case VSpline spline:
                _points.Clear();
                foreach (var pt in spline.Divide(SegmentsForLength(spline, scale))) _points.Add(pt);
                sink.EmitPolyline(_points, closed: false);
                return true;

            // Everything else — VArrow, VDimension, VRadialDimension, VGrid, VSpatialGrid, VRay,
            // VXLine — has drawing rules the geometry library does not own: arrowhead sizing in
            // screen pixels, dimension text layout, viewport-clipped infinite lines. They stay with
            // the host renderer rather than being half-reproduced here.
            default:
                return false;
        }
    }

    private bool EmitClosed(IReadOnlyList<VXYZ> points, in PenSpec pen, IPrimitiveSink sink)
    {
        if (points == null || points.Count < 2) return true;

        if (pen.HasFill)
        {
            _loops.Clear();
            _loops.Add(points);
            sink.EmitFilledLoops(_loops, FillRule.EvenOdd);
        }

        sink.EmitPolyline(points, closed: true);
        return true;
    }

    private static bool IsClosed(IReadOnlyList<VXYZ> points)
    {
        if (points.Count < 3) return false;
        var a = points[0];
        var b = points[^1];
        return Math.Abs(a.X - b.X) < GeometryTolerance.Epsilon
            && Math.Abs(a.Y - b.Y) < GeometryTolerance.Epsilon;
    }

    /// <summary>
    /// Samples an elliptical sweep into <see cref="_points"/>. Written out rather than delegating to
    /// <c>ICurve.Divide</c> because <c>Divide</c> allocates a fresh <c>List&lt;VXYZ&gt;</c> and, on
    /// <c>VEllipse</c>, walks an arc-length table to get there — correct for a geometry query, far
    /// too expensive for something on the per-frame path.
    /// </summary>
    private void SampleEllipse(VXYZ centre, double rx, double ry,
                               double startDeg, double endDeg, double scale)
    {
        _points.Clear();

        var sweep = endDeg - startDeg;
        if (Math.Abs(sweep) < 1e-9) sweep = 360;

        var radiusPx = Math.Max(Math.Abs(rx), Math.Abs(ry)) * scale;
        var segments = SegmentsForRadius(radiusPx);

        // Scale the count by how much of the ellipse the sweep actually covers, so a 10-degree arc
        // does not get the segment budget of a full circle.
        segments = Math.Max(2, (int)(segments * Math.Min(1.0, Math.Abs(sweep) / 360.0)) + 1);

        var startRad = startDeg * Math.PI / 180.0;
        var sweepRad = sweep * Math.PI / 180.0;

        for (int i = 0; i <= segments; i++)
        {
            var t = startRad + sweepRad * (i / (double)segments);
            _points.Add(new VXYZ(centre.X + rx * Math.Cos(t), centre.Y + ry * Math.Sin(t)));
        }
    }

    /// <summary>
    /// Segments for a curve of the given on-screen radius. Square-root because a polygonal
    /// approximation's error falls with the square of the segment count — so a fixed count is
    /// simultaneously wasteful when zoomed out and visibly faceted when zoomed in.
    /// </summary>
    public static int SegmentsForRadius(double radiusPixels)
    {
        if (!double.IsFinite(radiusPixels) || radiusPixels <= 0) return 6;
        return (int)Math.Clamp(Math.Sqrt(radiusPixels) * 2.5, 6, 256);
    }

    private static int SegmentsForLength(Shape shape, double scale)
    {
        var b = shape.GetBounds();
        var extentPx = Math.Max(b.Width, b.Height) * scale;
        return SegmentsForRadius(extentPx * 0.5);
    }
}
