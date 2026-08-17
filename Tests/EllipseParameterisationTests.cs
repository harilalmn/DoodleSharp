using System;
using System.Linq;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="VEllipse"/> is parameterised by arc length, like every other <see cref="ICurve"/>.
///
/// <para>
/// It used to interpolate the sweep angle instead. On an eccentric ellipse equal angles cover
/// unequal arc, so <c>Divide</c> bunched points near the flat ends — inconsistent with VLine,
/// VPolyline and VPolygon, and wrong for anything that samples a curve evenly (dashes, animation
/// paths, morph targets).
/// </para>
/// </summary>
[Collection("CanvasState")]
public class EllipseParameterisationTests
{
    /// <summary>Chord lengths between consecutive points of a division.</summary>
    private static double[] StepLengths(System.Collections.Generic.List<VXYZ> points) =>
        Enumerable.Range(0, points.Count - 1)
                  .Select(i => points[i].DistanceTo(points[i + 1]))
                  .ToArray();

    [Fact]
    public void DivideSpacesPointsEvenlyOnAnEccentricEllipse()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);   // 5:1 — strongly eccentric

        var steps = StepLengths(ellipse.Divide(64));

        // Every step within a few percent of the mean. Under angle parameterisation the longest
        // step was several times the shortest.
        var mean = steps.Average();
        Assert.All(steps, s => Assert.InRange(s, mean * 0.9, mean * 1.1));
    }

    [Fact]
    public void AngleParameterisationIsStillAvailableAndIsVisiblyUneven()
    {
        // Documents the difference, and keeps the escape hatch working.
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);

        var byAngle = Enumerable.Range(0, 65)
            .Select(i => ellipse.EvaluateByAngle(i / 64.0))
            .ToList();

        var steps = StepLengths(byAngle);
        Assert.True(steps.Max() / steps.Min() > 2.0,
            "angle parameterisation should bunch points at the flat ends");
    }

    [Fact]
    public void HalfwayParameterIsHalfwayAlongTheCurve()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);

        var all = ellipse.Divide(512);
        double total = StepLengths(all).Sum();

        var midpoint = ellipse.Evaluate(0.5);

        // Walk the division until we reach the midpoint, and check we covered about half the length.
        double covered = 0;
        for (int i = 0; i < all.Count - 1; i++)
        {
            if (all[i].DistanceTo(midpoint) < total * 0.01) break;
            covered += all[i].DistanceTo(all[i + 1]);
        }

        Assert.InRange(covered, total * 0.45, total * 0.55);
    }

    [Fact]
    public void ACircularEllipseIsUnchangedByTheNewParameterisation()
    {
        // Where the radii are equal, angle and arc length are proportional, so the two agree.
        var circle = new VEllipse(new VXYZ(0, 0), 50, 50);

        for (double t = 0; t <= 1.0; t += 0.125)
        {
            var byLength = circle.Evaluate(t);
            var byAngle = circle.EvaluateByAngle(t);
            Assert.Equal(byAngle.X, byLength.X, 3);
            Assert.Equal(byAngle.Y, byLength.Y, 3);
        }
    }

    [Fact]
    public void EndpointsAreExact()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20) { StartAngle = 0, EndAngle = 180 };

        var start = ellipse.Evaluate(0);
        var end = ellipse.Evaluate(1);

        Assert.Equal(100, start.X, 6);
        Assert.Equal(0, start.Y, 6);
        Assert.Equal(-100, end.X, 6);
        Assert.Equal(0, end.Y, 6);
    }

    [Fact]
    public void SetBoundsTrimsByArcLengthToo()
    {
        var ellipse = new VEllipse(new VXYZ(0, 0), 100, 20);
        double fullLength = StepLengths(ellipse.Divide(512)).Sum();

        ellipse.SetBounds(0.25, 0.75);   // keep the middle half of the curve

        double trimmedLength = StepLengths(ellipse.Divide(512)).Sum();
        Assert.InRange(trimmedLength, fullLength * 0.45, fullLength * 0.55);
    }
}
