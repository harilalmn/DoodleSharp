using System;
using System.Linq;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The family of bugs that came from treating an angular sweep as a pair of normalised absolute
/// angles, and from shapes that carried an orientation nothing ever read.
///
/// <para>
/// These belong together because they shared a cause: a sweep is a start plus a signed offset, not
/// a pair of points on a circle, and folding either end into [0, 360) on its own throws away both
/// the direction of travel and any sweep that crosses zero. <see cref="GeometryHelper"/> owns the
/// rule now, and <see cref="VArc"/>, <see cref="VEllipse"/> and the ray caster all defer to it.
/// </para>
/// </summary>
public class SweepAndOrientationTests
{
    private static void Close(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) < tolerance, $"expected {expected}, got {actual}");

    // ---- VArc: a rotation must not rewrite the sweep ----

    [Fact]
    public void RotatingAnArcThatCrossesZeroKeepsItsSweep()
    {
        // 350 to 370 is a 20-degree arc. Normalising the two ends independently turns it into
        // 350 to 10, which reads as the 340-degree arc going the other way.
        var arc = new VArc(new VXYZ(0, 0), 10, 350, 370);
        var lengthBefore = arc.GetLength();

        arc.Rotate(new VXYZ(0, 0), 45);

        Close(lengthBefore, arc.GetLength());
        Close(20, arc.EndAngle - arc.StartAngle);
    }

    [Fact]
    public void RotatingAnArcByZeroChangesNothing()
    {
        var arc = new VArc(new VXYZ(0, 0), 10, 350, 370);
        arc.Rotate(new VXYZ(0, 0), 0);

        Close(350, arc.StartAngle);
        Close(370, arc.EndAngle);
    }

    [Fact]
    public void RotatingAnArcMovesItsPointsWithIt()
    {
        var arc = new VArc(new VXYZ(0, 0), 10, 0, 90);
        var midBefore = arc.Evaluate(0.5);

        arc.Rotate(new VXYZ(0, 0), 90);
        var midAfter = arc.Evaluate(0.5);

        var expected = GeometryHelper.RotatePoint(midBefore, new VXYZ(0, 0), 90);
        Close(expected.X, midAfter.X);
        Close(expected.Y, midAfter.Y);
    }

    // ---- VArc: a mirror must respect the line it was given ----

    [Theory]
    [InlineData(0, 1, 0)]      // mirror along the X axis
    [InlineData(0, 0, 1)]      // mirror along the Y axis
    [InlineData(0, 1, 1)]      // 45 degrees
    [InlineData(30, 2, -1)]    // an arbitrary line through the origin
    public void FlippingAnArcMirrorsItAboutTheGivenLine(double startAngle, double dx, double dy)
    {
        var mirror = new VLine(new VXYZ(0, 0), new VXYZ(dx, dy));
        var arc = new VArc(new VXYZ(3, 4), 10, startAngle, startAngle + 60);

        var samples = Enumerable.Range(0, 5)
            .Select(i => GeometryHelper.FlipPoint(arc.Evaluate(i / 4.0), mirror))
            .ToArray();

        arc.Flip(mirror);

        // Mirroring reverses the direction of travel, so the mirrored samples come back reversed.
        for (int i = 0; i < samples.Length; i++)
        {
            var actual = arc.Evaluate((samples.Length - 1 - i) / 4.0);
            Close(samples[i].X, actual.X, 1e-9);
            Close(samples[i].Y, actual.Y, 1e-9);
        }
    }

    [Fact]
    public void FlippingAnArcTwiceAboutTheSameLineIsIdentity()
    {
        var mirror = new VLine(new VXYZ(1, 1), new VXYZ(4, -2));
        var arc = new VArc(new VXYZ(3, 4), 10, 20, 140);
        var midBefore = arc.Evaluate(0.5);

        arc.Flip(mirror);
        arc.Flip(mirror);

        Close(midBefore.X, arc.Evaluate(0.5).X, 1e-9);
        Close(midBefore.Y, arc.Evaluate(0.5).Y, 1e-9);
    }

    // ---- VArc: bounds are the arc's, not the circle's ----

    [Fact]
    public void ArcBoundsCoverTheArcAndNotTheWholeCircle()
    {
        var bounds = new VArc(new VXYZ(0, 0), 10, 0, 90).GetBounds();

        Close(0, bounds.Min.X);
        Close(0, bounds.Min.Y);
        Close(10, bounds.Max.X);
        Close(10, bounds.Max.Y);
    }

    [Fact]
    public void ClockwiseArcBoundsMatchTheCounterClockwiseOnesOverTheSameSpan()
    {
        var ccw = new VArc(new VXYZ(0, 0), 10, 0, 90).GetBounds();
        var cw = new VArc(new VXYZ(0, 0), 10, 90, 0).GetBounds();

        Close(ccw.Min.X, cw.Min.X);
        Close(ccw.Min.Y, cw.Min.Y);
        Close(ccw.Max.X, cw.Max.X);
        Close(ccw.Max.Y, cw.Max.Y);
    }

    [Fact]
    public void FullCircleArcStillBoundsTheWholeCircle()
    {
        var bounds = new VArc(new VXYZ(1, 2), 10, 0, 360).GetBounds();

        Close(-9, bounds.Min.X);
        Close(-8, bounds.Min.Y);
        Close(11, bounds.Max.X);
        Close(12, bounds.Max.Y);
    }

    [Fact]
    public void ArcBoundsContainEverySampledPoint()
    {
        foreach (var (start, end) in new[] { (0.0, 90.0), (350.0, 370.0), (90.0, 0.0), (-45.0, 200.0), (10.0, 350.0) })
        {
            var arc = new VArc(new VXYZ(2, -3), 7, start, end);
            var bounds = arc.GetBounds();

            for (int i = 0; i <= 200; i++)
            {
                var p = arc.Evaluate(i / 200.0);
                Assert.True(p.X >= bounds.Min.X - 1e-9 && p.X <= bounds.Max.X + 1e-9,
                    $"x {p.X} outside [{bounds.Min.X}, {bounds.Max.X}] for {start}..{end}");
                Assert.True(p.Y >= bounds.Min.Y - 1e-9 && p.Y <= bounds.Max.Y + 1e-9,
                    $"y {p.Y} outside [{bounds.Min.Y}, {bounds.Max.Y}] for {start}..{end}");
            }
        }
    }

    // ---- VArc: splitting and parameterisation stay inside the sweep ----

    [Fact]
    public void SplittingAnArcThatCrossesZeroGivesTwoHalvesOfTheOriginal()
    {
        var arc = new VArc(new VXYZ(0, 0), 10, 350, 370);
        var (first, second) = arc.SplitAtPoint(new VXYZ(10, 0));

        Close(arc.GetLength(), first.GetLength() + second.GetLength());
    }

    [Fact]
    public void ParameterAtPointIsMeasuredAlongAClockwiseArcToo()
    {
        var arc = new VArc(new VXYZ(0, 0), 10, 90, 0);
        var mid = arc.Evaluate(0.5);

        Close(0.5, arc.ParameterAtPoint(mid), 1e-6);
    }

    // ---- the shared sweep rule ----

    [Theory]
    [InlineData(350, 370, 0, true)]     // crosses zero
    [InlineData(350, 370, 180, false)]
    [InlineData(90, 0, 45, true)]       // clockwise
    [InlineData(90, 0, 180, false)]
    [InlineData(0, 720, 123, true)]     // more than a full turn covers everything
    public void SweepContainsHonoursDirectionAndWrapping(double start, double end, double angle, bool expected)
    {
        Assert.Equal(expected, GeometryHelper.SweepContains(start, end, angle));
    }

    // ---- VEllipse: Rotate has something to write to ----

    [Fact]
    public void RotatingAnEllipseAboutItsOwnCentreTurnsIt()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5);
        ellipse.Rotate(new VXYZ(0, 0), 90);

        var p = ellipse.EvaluateByAngle(0);
        Close(0, p.X, 1e-9);
        Close(10, p.Y, 1e-9);
    }

    [Fact]
    public void AnUnrotatedEllipseIsUnchanged()
    {
        var ellipse = new VEllipse(new VXYZ(1, 2), 10, 5);

        Assert.Equal(0, ellipse.Rotation);
        Close(11, ellipse.EvaluateByAngle(0).X);
        Close(2, ellipse.EvaluateByAngle(0).Y);

        var bounds = ellipse.GetBounds();
        Close(-9, bounds.Min.X);
        Close(-3, bounds.Min.Y);
        Close(11, bounds.Max.X);
        Close(7, bounds.Max.Y);
    }

    [Fact]
    public void RotatedEllipseBoundsAreExact()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5) { Rotation = 90 };
        var bounds = ellipse.GetBounds();

        Close(-5, bounds.Min.X);
        Close(-10, bounds.Min.Y);
        Close(5, bounds.Max.X);
        Close(10, bounds.Max.Y);
    }

    [Fact]
    public void PartialEllipseBoundsCoverOnlyTheSweep()
    {
        var bounds = new VEllipse(new VXYZ(0, 0), 10, 5, 0, 180).GetBounds();

        Close(0, bounds.Min.Y);
        Close(5, bounds.Max.Y);
        Close(-10, bounds.Min.X);
        Close(10, bounds.Max.X);
    }

    [Fact]
    public void EllipseBoundsContainEverySampledPoint()
    {
        foreach (var rotation in new[] { 0.0, 17.0, 90.0, 143.0, -60.0 })
        foreach (var (start, end) in new[] { (0.0, 360.0), (0.0, 180.0), (200.0, 40.0), (350.0, 370.0) })
        {
            var ellipse = new VEllipse(new VXYZ(3, -1), 10, 4, start, end) { Rotation = rotation };
            var bounds = ellipse.GetBounds();

            for (int i = 0; i <= 200; i++)
            {
                var p = ellipse.EvaluateByAngle(i / 200.0);
                Assert.True(p.X >= bounds.Min.X - 1e-9 && p.X <= bounds.Max.X + 1e-9,
                    $"x {p.X} outside [{bounds.Min.X}, {bounds.Max.X}] rot={rotation} {start}..{end}");
                Assert.True(p.Y >= bounds.Min.Y - 1e-9 && p.Y <= bounds.Max.Y + 1e-9,
                    $"y {p.Y} outside [{bounds.Min.Y}, {bounds.Max.Y}] rot={rotation} {start}..{end}");
            }
        }
    }

    [Fact]
    public void RotationSurvivesCloneAndOffset()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5) { Rotation = 33 };

        Assert.Equal(33, ellipse.Clone().Rotation);
        Assert.Equal(33, ((VEllipse)ellipse.Offset(1)).Rotation);
    }

    [Fact]
    public void FlippingAnEllipseTwiceAboutTheSameLineIsIdentity()
    {
        var mirror = new VLine(new VXYZ(1, 1), new VXYZ(4, -2));
        var ellipse = new VEllipse(new VXYZ(3, 4), 10, 5, 20, 140) { Rotation = 25 };
        var before = ellipse.EvaluateByAngle(0.5);

        ellipse.Flip(mirror);
        ellipse.Flip(mirror);

        Close(before.X, ellipse.EvaluateByAngle(0.5).X, 1e-9);
        Close(before.Y, ellipse.EvaluateByAngle(0.5).Y, 1e-9);
    }

    [Fact]
    public void FlippingAnEllipseMirrorsItsPoints()
    {
        var mirror = new VLine(new VXYZ(0, 0), new VXYZ(0, 1));   // the Y axis
        var ellipse = new VEllipse(new VXYZ(3, 4), 10, 5) { Rotation = 25 };
        var before = ellipse.EvaluateByAngle(0.25);
        var expected = GeometryHelper.FlipPoint(before, mirror);

        ellipse.Flip(mirror);

        // Same set of points, travelled the other way: 0.25 in becomes 0.75 in.
        var actual = ellipse.EvaluateByAngle(0.75);
        Close(expected.X, actual.X, 1e-9);
        Close(expected.Y, actual.Y, 1e-9);
    }

    [Fact]
    public void ALineMeetsARotatedEllipseWhereItActuallyIs()
    {
        // 10x5 turned a quarter turn: the X axis now crosses it at the SHORT radius.
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5) { Rotation = 90 };
        var hits = CurveIntersection.Intersect(new VLine(new VXYZ(-20, 0), new VXYZ(20, 0)), ellipse);

        Assert.Equal(2, hits.Points.Count);
        foreach (var p in hits.Points)
        {
            Close(5, Math.Abs(p.X));
            Close(0, p.Y);
        }
    }

    /// <summary>
    /// <see cref="VEllipse.Contains"/> was the one member that did not follow
    /// <see cref="VEllipse.Rotation"/>, because the implicit equation it evaluates divides by the
    /// radii and so only means anything along the ellipse's own axes.
    /// </summary>
    [Fact]
    public void ContainsFollowsTheEllipsesRotation()
    {
        // 100x20 turned a quarter turn: it is now tall and narrow.
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20) { Rotation = 90 };

        Assert.True(ellipse.Contains(new VXYZ(0, 80)), "(0, 80) is inside a quarter-turned 100x20 ellipse");
        Assert.False(ellipse.Contains(new VXYZ(80, 0)), "(80, 0) is outside it");
    }

    [Fact]
    public void ContainsIsUnchangedForAnUnrotatedEllipse()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);

        Assert.True(ellipse.Contains(new VXYZ(80, 0)));
        Assert.False(ellipse.Contains(new VXYZ(0, 80)));
    }

    /// <summary>
    /// Classification has to be right all the way round, not just on the two axes. Sampled just
    /// inside and just outside the curve rather than exactly on it: a point ON the boundary of an
    /// interior test is genuinely ambiguous in floating point, and always has been.
    /// </summary>
    [Fact]
    public void ContainsClassifiesEveryDirectionCorrectly()
    {
        foreach (var rotation in new[] { 0.0, 37.0, 90.0, -64.0 })
        {
            var ellipse = new VEllipse(new VXYZ(3, -2), 50, 15) { Rotation = rotation };

            for (int degrees = 0; degrees < 360; degrees++)
            {
                var onCurve = ellipse.PointAtAngle(degrees);
                var toEdge = onCurve - ellipse.Center;

                Assert.True(ellipse.Contains(ellipse.Center + toEdge * 0.98),
                    $"just inside at {degrees} deg (rotation {rotation}) reported outside");
                Assert.False(ellipse.Contains(ellipse.Center + toEdge * 1.02),
                    $"just outside at {degrees} deg (rotation {rotation}) reported inside");
            }
        }
    }

    /// <summary>
    /// The sweep fix reached <see cref="VArc.ParameterAtPoint"/> but was not carried across to the
    /// ellipse, which kept the "normalise into [0, 360), divide by the sweep" form and additionally
    /// read the angle in world axes.
    /// </summary>
    [Theory]
    [InlineData(0, 180, 0)]      // forward sweep
    [InlineData(90, 0, 0)]       // clockwise
    [InlineData(0, 180, 40)]     // forward, rotated
    [InlineData(200, 40, 25)]    // clockwise across zero, rotated
    public void EllipseParameterAtPointIsMeasuredAlongItsOwnSweep(double start, double end, double rotation)
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 60, 30, start, end) { Rotation = rotation };
        var midpoint = ellipse.EvaluateByAngle(0.5);

        Close(0.5, ellipse.ParameterAtPoint(midpoint), 1e-6);
    }

    /// <summary>
    /// A control-point handle that is not on the shape is a handle you cannot grab.
    /// </summary>
    [Fact]
    public void EllipseControlPointHandlesSitOnTheCurve()
    {
        var ellipse = new VEllipse(new VXYZ(5, 5), 40, 10) { Rotation = 55 };
        var handles = ellipse.GetControlPoints();

        foreach (var index in new[] { 1, 2 })
        {
            var handle = new VXYZ(handles[index].X, handles[index].Y);
            Close(0, ellipse.DistanceTo(handle), 1e-6);
        }
    }

    [Fact]
    public void UnrotatedEllipseHandlesAreWhereTheyAlwaysWere()
    {
        var handles = new VEllipse(new VXYZ(0, 0), 40, 10).GetControlPoints();

        Close(40, handles[1].X);
        Close(0, handles[1].Y);
        Close(0, handles[2].X);
        Close(10, handles[2].Y);
    }

    /// <summary>
    /// One line-spacing constant, four readers: <see cref="VText.GetBounds"/> and the DXF, SVG and
    /// PDF writers. They had drifted, so a label's exported block was shorter than the box reserved
    /// for it.
    /// </summary>
    [Fact]
    public void MultiLineHeightScalesOnlyTheGapsBetweenLines()
    {
        var single = new VText(new VXYZ(0, 0), "AAA", 10).GetBounds();
        Close(10, single.Max.Y - single.Min.Y);

        var triple = new VText(new VXYZ(0, 0), "AAA\nBBB\nCCC", 10).GetBounds();
        Close(10 * (1 + 2 * 1.2), triple.Max.Y - triple.Min.Y);
    }

    [Fact]
    public void TheTwoEllipseGetLengthsAgree()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5);
        Close(((ICurve)ellipse).GetLength(), ellipse.GetLength());
    }

    [Fact]
    public void SplittingAnEllipseGivesPiecesInsideTheOriginalSweep()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 10, 5, 0, 180);
        var (first, second) = ellipse.SplitAtPoint(new VXYZ(0, 5));

        var a = (VEllipse)first;
        var b = (VEllipse)second;

        Assert.InRange(a.EndAngle, 0, 180);
        Assert.Equal(0, a.StartAngle);
        Assert.Equal(180, b.EndAngle);
    }

    // ---- VRectangle: a mirror must mirror the rotation too ----

    [Fact]
    public void FlippingARotatedRectangleMirrorsItsCorners()
    {
        var mirror = new VLine(new VXYZ(0, 0), new VXYZ(0, 1));   // the Y axis
        var rect = new VRectangle(new VXYZ(2, 1), 10, 4) { RotationAngle = 30 };

        var expected = rect.Points.Select(p => GeometryHelper.FlipPoint(p, mirror)).ToList();
        rect.Flip(mirror);

        foreach (var want in expected)
        {
            Assert.True(rect.Points.Any(got => Math.Abs(got.X - want.X) < 1e-9 && Math.Abs(got.Y - want.Y) < 1e-9),
                $"mirrored corner {want} is not among {string.Join(", ", rect.Points)}");
        }
    }

    [Theory]
    [InlineData(0, 0, 90)]      // about the origin
    [InlineData(7, 3, 90)]      // about the rectangle's own centre
    [InlineData(-4, 11, 37)]    // about an arbitrary point
    public void RotatingARectangleMovesItsCornersLikeEveryOtherShape(double px, double py, double angle)
    {
        var pivot = new VXYZ(px, py);
        var rect = new VRectangle(new VXYZ(2, 1), 10, 4);
        var expected = rect.Points.Select(p => GeometryHelper.RotatePoint(p, pivot, angle)).ToList();

        rect.Rotate(pivot, angle);

        foreach (var want in expected)
        {
            Assert.True(rect.Points.Any(got => Math.Abs(got.X - want.X) < 1e-9 && Math.Abs(got.Y - want.Y) < 1e-9),
                $"rotated corner {want} is not among {string.Join(", ", rect.Points)}");
        }
    }

    [Fact]
    public void RotatingARectangleAboutItsOwnCentreLeavesTheCentrePut()
    {
        var rect = new VRectangle(new VXYZ(2, 1), 10, 4);
        var centre = new VXYZ(7, 3);

        rect.Rotate(centre, 41);

        var after = rect.GetBounds();
        Close(7, (after.Min.X + after.Max.X) / 2);
        Close(3, (after.Min.Y + after.Max.Y) / 2);
    }

    [Fact]
    public void FlippingAnUnrotatedRectangleIsUnchanged()
    {
        var rect = new VRectangle(new VXYZ(2, 1), 10, 4);
        rect.Flip(new VLine(new VXYZ(0, 0), new VXYZ(0, 1)));

        Assert.Equal(0, rect.RotationAngle);
        Close(-12, rect.Corner.X);
        Close(1, rect.Corner.Y);
    }
}
