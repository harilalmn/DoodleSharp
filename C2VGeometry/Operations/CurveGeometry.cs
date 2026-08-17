namespace C2VGeometry;

/// <summary>
/// Point-to-curve measurement shared by the curve shapes.
///
/// <para>
/// <see cref="Shape.DistanceTo"/> and <see cref="Shape.Contains"/> fall back to the bounding box,
/// which is meaningless for a curve: <c>line.Contains(p)</c> was true for any point in the diagonal's
/// bounding box — far off the line itself — and <c>line.DistanceTo(p)</c> measured to the box centre
/// rather than to the line. These helpers give the curve shapes a real implementation.
/// </para>
/// </summary>
public static class CurveGeometry
{
    /// <summary>
    /// Points sampled along a curve that has no closed-form distance (Bezier, spline). Enough that
    /// the error is well under a pixel at normal zoom, cheap enough for interactive use.
    /// </summary>
    internal const int DefaultSamples = 96;

    /// <summary>Shortest distance from a point to the segment [a, b].</summary>
    public static double DistanceToSegment(VXYZ point, VXYZ a, VXYZ b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;

        if (lengthSquared <= GeometryTolerance.Epsilon)
        {
            // Degenerate segment: fall back to the distance to the point itself.
            return Math.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));
        }

        // Project onto the segment, clamped so the nearest point stays within [a, b].
        double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);

        double nearestX = a.X + t * dx;
        double nearestY = a.Y + t * dy;
        double ex = point.X - nearestX;
        double ey = point.Y - nearestY;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>Shortest distance from a point to a polyline through <paramref name="vertices"/>.</summary>
    public static double DistanceToPath(VXYZ point, IReadOnlyList<VXYZ> vertices, bool closed = false)
    {
        if (vertices == null || vertices.Count == 0) return double.PositiveInfinity;
        if (vertices.Count == 1)
        {
            double dx = point.X - vertices[0].X;
            double dy = point.Y - vertices[0].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        double best = double.PositiveInfinity;
        for (int i = 0; i < vertices.Count - 1; i++)
            best = Math.Min(best, DistanceToSegment(point, vertices[i], vertices[i + 1]));

        if (closed)
            best = Math.Min(best, DistanceToSegment(point, vertices[^1], vertices[0]));

        return best;
    }

    /// <summary>
    /// Shortest distance from a point to any curve, by sampling it into a polyline. Used for curves
    /// with no practical closed form.
    /// </summary>
    public static double DistanceToCurve(VXYZ point, ICurve curve, int samples = DefaultSamples)
    {
        if (curve == null) return double.PositiveInfinity;

        var points = curve.Divide(Math.Max(2, samples));
        if (points == null || points.Count == 0) return double.PositiveInfinity;

        return DistanceToPath(point, points);
    }

    /// <summary>
    /// Whether a point lies on a stroke, within a tolerance scaled to the curve's own size.
    ///
    /// <para>
    /// A stroke encloses no area, so "contains" can only reasonably mean "lies on". The tolerance is
    /// relative so that the answer does not depend on the units the drawing happens to use: a
    /// hundred-unit line and a hundred-thousand-unit line behave the same way.
    /// </para>
    /// </summary>
    public static bool IsOnStroke(double distance, double curveExtent)
    {
        double tolerance = Math.Max(GeometryTolerance.Epsilon, Math.Abs(curveExtent) * 1e-6);
        return distance <= tolerance;
    }
}
