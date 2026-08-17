using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// <c>Contains</c> and <c>DistanceTo</c> on curve shapes.
///
/// <para>
/// Both used to fall through to <see cref="Shape"/>'s bounding-box implementations, which are
/// meaningless for a curve: <c>line.Contains(p)</c> was true for any point in the diagonal's
/// bounding box, and <c>line.DistanceTo(p)</c> measured to the box centre rather than to the line.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class CurveHitTestingTests
{
    private const double Tolerance = 1e-6;

    // ── VLine ───────────────────────────────────────────────────────────────

    [Fact]
    public void Line_DistanceIsMeasuredToTheSegment()
    {
        var line = new VLine(0, 0, 10, 0);

        Assert.Equal(0, line.DistanceTo(new VXYZ(5, 0)), 6);    // on it
        Assert.Equal(3, line.DistanceTo(new VXYZ(5, 3)), 6);    // perpendicular
    }

    [Fact]
    public void Line_DistanceClampsToTheEndpoints()
    {
        var line = new VLine(0, 0, 10, 0);

        // Beyond the end: the nearest point is the endpoint, not the infinite line.
        Assert.Equal(5, line.DistanceTo(new VXYZ(15, 0)), 6);
        Assert.Equal(5, line.DistanceTo(new VXYZ(-5, 0)), 6);
    }

    [Fact]
    public void Line_DoesNotContainPointsMerelyInsideItsBoundingBox()
    {
        // The reported case: a diagonal's bounding box is mostly nowhere near the line.
        var diagonal = new VLine(0, 0, 100, 100);

        Assert.False(diagonal.Contains(new VXYZ(100, 0)));   // opposite corner of the box
        Assert.False(diagonal.Contains(new VXYZ(0, 100)));
        Assert.True(diagonal.Contains(new VXYZ(50, 50)));    // actually on the line
    }

    // ── VPolyline ───────────────────────────────────────────────────────────

    [Fact]
    public void Polyline_MeasuresToTheNearestSegment()
    {
        var polyline = new VPolyline(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10));

        Assert.Equal(0, polyline.DistanceTo(new VXYZ(10, 5)), 6);
        Assert.Equal(2, polyline.DistanceTo(new VXYZ(5, 2)), 6);
        Assert.False(polyline.Contains(new VXYZ(2, 8)));   // inside the bbox, off the path
    }

    // ── VArc ────────────────────────────────────────────────────────────────

    [Fact]
    public void Arc_MeasuresToTheSweptPortionOnly()
    {
        // Upper half of a radius-10 circle at the origin.
        var arc = new VArc(new VXYZ(0, 0), 10, 0, 180);

        Assert.Equal(0, arc.DistanceTo(new VXYZ(0, 10)), 4);       // top of the sweep
        Assert.True(arc.Contains(new VXYZ(0, 10)));

        // Directly below the centre is on the circle but NOT on this arc: the nearest swept point
        // is an endpoint, 20 units away, not 0.
        Assert.True(arc.DistanceTo(new VXYZ(0, -10)) > 10);
        Assert.False(arc.Contains(new VXYZ(0, -10)));
    }

    [Fact]
    public void Arc_DoesNotContainItsCentre()
    {
        var arc = new VArc(new VXYZ(0, 0), 10, 0, 180);

        // The centre sits inside the bounding box, which is what the old implementation checked.
        Assert.False(arc.Contains(new VXYZ(0, 0)));
        Assert.Equal(10, arc.DistanceTo(new VXYZ(0, 0)), 3);
    }

    // ── VBezier / VSpline ───────────────────────────────────────────────────

    [Fact]
    public void Bezier_MeasuresToTheCurve()
    {
        // A Bezier with collinear controls is a straight line along y = 0.
        var bezier = new VBezier(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(20, 0), new VXYZ(30, 0));

        Assert.Equal(0, bezier.DistanceTo(new VXYZ(15, 0)), 3);
        Assert.Equal(4, bezier.DistanceTo(new VXYZ(15, 4)), 3);
    }

    [Fact]
    public void Spline_MeasuresToTheCurve()
    {
        var spline = new VSpline(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(20, 0), new VXYZ(30, 0));

        Assert.Equal(0, spline.DistanceTo(new VXYZ(15, 0)), 2);
        Assert.True(spline.DistanceTo(new VXYZ(15, 10)) > 5);
    }

    // ── VPolygon: a closed shape, so Contains means inside ───────────────────

    // ── Infinite-extent shapes, where a bounding box is doubly meaningless ───

    [Fact]
    public void XLine_MeasuresPerpendicularlyAndExtendsBothWays()
    {
        var line = new VXLine(new VXYZ(0, 0), new VXYZ(1, 0));   // the X axis

        Assert.Equal(0, line.DistanceTo(new VXYZ(1000, 0)), 6);   // far along it
        Assert.Equal(0, line.DistanceTo(new VXYZ(-1000, 0)), 6);  // and behind
        Assert.Equal(7, line.DistanceTo(new VXYZ(50, 7)), 6);     // perpendicular offset
        Assert.True(line.Contains(new VXYZ(-500, 0)));
        Assert.False(line.Contains(new VXYZ(0, 5)));
    }

    [Fact]
    public void Ray_StopsAtItsOrigin()
    {
        var ray = new VRay(new VXYZ(0, 0), new VXYZ(1, 0));   // +X from the origin

        Assert.Equal(0, ray.DistanceTo(new VXYZ(1000, 0)), 6);   // along the ray
        Assert.Equal(10, ray.DistanceTo(new VXYZ(-10, 0)), 6);   // behind it: measured to the origin
        Assert.True(ray.Contains(new VXYZ(500, 0)));
        Assert.False(ray.Contains(new VXYZ(-500, 0)));           // the ray does not go backwards
    }

    [Fact]
    public void Polygon_ContainsIsAnInteriorTest()
    {
        // An L-shape: its bounding box includes a region that is outside the polygon.
        var l = new VPolygon(
            new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 4),
            new VXYZ(4, 4), new VXYZ(4, 10), new VXYZ(0, 10));

        Assert.True(l.Contains(new VXYZ(2, 2)));      // inside
        Assert.True(l.Contains(new VXYZ(8, 2)));      // inside the lower arm
        Assert.False(l.Contains(new VXYZ(8, 8)));     // in the bbox, in the notch — outside
        Assert.False(l.Contains(new VXYZ(20, 20)));   // well outside
    }

    // ── Closed shapes that had an exact Contains but a bounding-box DistanceTo ──
    // Found by the documentation pass after the first round of this fix: these override Contains
    // but were still inheriting Shape.DistanceTo, which measures to the bounding-box centre.

    [Fact]
    public void Circle_DistanceIsToTheCircumference()
    {
        var circle = new VCircle(0, 0, 10);

        Assert.Equal(0, circle.DistanceTo(new VXYZ(10, 0)), 6);    // on it — was 10 before
        Assert.Equal(5, circle.DistanceTo(new VXYZ(15, 0)), 6);    // outside
        Assert.Equal(4, circle.DistanceTo(new VXYZ(6, 0)), 6);     // inside, measured to the edge
        Assert.Equal(10, circle.DistanceTo(new VXYZ(0, 0)), 6);    // centre
    }

    [Fact]
    public void Ellipse_ContainsIsAnInteriorTestWhenClosed()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);

        Assert.True(ellipse.Contains(new VXYZ(0, 0)));
        Assert.True(ellipse.Contains(new VXYZ(90, 0)));
        Assert.False(ellipse.Contains(new VXYZ(0, 30)));    // in the bbox, outside the ellipse
        Assert.False(ellipse.Contains(new VXYZ(95, 15)));   // corner region of the bbox
    }

    [Fact]
    public void Ellipse_PartialSweepIsTreatedAsAnOpenCurve()
    {
        // Half an ellipse encloses nothing, so Contains means "on the curve".
        var half = new VEllipse(new VXYZ(0, 0), 100, 20, 0, 180);

        Assert.False(half.Contains(new VXYZ(0, 0)));
        Assert.True(half.Contains(new VXYZ(100, 0)));   // an endpoint lies on it
    }

    [Fact]
    public void Ellipse_DistanceIsToTheCurve()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);

        Assert.Equal(0, ellipse.DistanceTo(new VXYZ(100, 0)), 2);
        Assert.Equal(10, ellipse.DistanceTo(new VXYZ(110, 0)), 2);
    }

    [Fact]
    public void Region_MeasuresToHoleEdgesToo()
    {
        // Contains already excluded holes; DistanceTo measured only the outer loop, so a point just
        // inside a hole was reported as far from the boundary when it sits right on one.
        var outer = new VPolygon(new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(100, 100), new VXYZ(0, 100));
        var region = new Region(outer);
        region.AddHole(new VPolygon(new VXYZ(40, 40), new VXYZ(60, 40), new VXYZ(60, 60), new VXYZ(40, 60)));

        // Dead centre of the hole: 10 from the hole edge, 50 from the outer boundary.
        Assert.Equal(10, region.DistanceTo(new VXYZ(50, 50)), 3);

        // And the interior test still excludes the hole.
        Assert.False(region.Contains(new VXYZ(50, 50)));
        Assert.True(region.Contains(new VXYZ(10, 10)));
    }

    [Fact]
    public void Polygon_DistanceIsToTheBoundary()
    {
        var square = new VPolygon(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10), new VXYZ(0, 10));

        Assert.Equal(0, square.DistanceTo(new VXYZ(5, 0)), 6);    // on an edge
        Assert.Equal(5, square.DistanceTo(new VXYZ(5, 5)), 6);    // centre: 5 from every edge
        Assert.Equal(5, square.DistanceTo(new VXYZ(15, 5)), 6);   // outside
    }
}
