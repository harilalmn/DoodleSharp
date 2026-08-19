using System.Linq;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <c>Shape.DoesIntersect</c>/<c>Shape.Intersect(Shape)</c> and <c>ICurve.Intersect(ICurve)</c> are
/// two ways of asking the same geometric question, and they used to disagree: the Shape-typed pair
/// answered only for the four types that overrode it, so a ray that demonstrably crossed a circle
/// twice reported no intersection at all.
/// </summary>
[Collection("CanvasState")]
public class ShapeIntersectionTests
{
    public ShapeIntersectionTests() => Shape.AutoRegister = false;

    private static VCircle Circle() => new(171.50, 54.94, 35.74);

    /// <summary>A direction pointing straight at the test circle's centre.</summary>
    private static VXYZ TowardCircle() => new VXYZ(171.50, 54.94).Normalize();

    [Fact]
    public void RayThroughACircleIntersectsIt()
    {
        var ray = new VRay(new VXYZ(0, 0), TowardCircle());
        var circle = Circle();

        Assert.Equal(2, ray.Intersect((ICurve)circle).Points.Count);
        Assert.True(ray.DoesIntersect(circle));
        Assert.NotNull(((Shape)ray).Intersect(circle));
    }

    [Fact]
    public void TheTwoApisAgreeAcrossCurveTypes()
    {
        var circle = Circle();
        var toward = TowardCircle();

        ICurve[] curves =
        {
            new VRay(new VXYZ(0, 0), toward),
            new VLine(new VXYZ(0, 0), 1000 * toward),
            new VXLine(new VXYZ(0, 0), toward),
            new VPolyline(new VXYZ(0, 0), 500 * toward, 1000 * toward),
            new VArc(new VXYZ(0, 0), 180.0, 0, 90),
        };

        foreach (var curve in curves)
        {
            var shape = (Shape)curve;
            var expected = curve.Intersect(circle).HasIntersection;

            Assert.Equal(expected, shape.DoesIntersect(circle));
            Assert.Equal(expected, circle.DoesIntersect(shape));
            Assert.Equal(expected, shape.Intersect(circle) != null);
        }
    }

    [Fact]
    public void ARayThatMissesReportsNoIntersection()
    {
        var away = new VRay(new VXYZ(0, 0), new VXYZ(-1, -1));
        var circle = Circle();

        Assert.False(away.DoesIntersect(circle));
        Assert.Null(((Shape)away).Intersect(circle));
        Assert.False(circle.DoesIntersect(away));
    }

    [Fact]
    public void CircleAgainstCircleIsAnsweredToo()
    {
        // Neither VCircle overrides Shape.Intersect, so this pair used to be silently negative.
        var a = new VCircle(new VXYZ(0, 0), 10);
        var b = new VCircle(new VXYZ(15, 0), 10);
        var far = new VCircle(new VXYZ(100, 0), 10);

        Assert.True(a.DoesIntersect(b));
        Assert.False(a.DoesIntersect(far));
    }

    [Fact]
    public void IntersectShapeReturnsAPointForOneHitAndAGroupForSeveral()
    {
        var circle = Circle();

        // A ray from the centre leaves through the boundary exactly once.
        var fromCentre = new VRay(new VXYZ(171.50, 54.94), new VXYZ(1, 0));
        Assert.IsType<VPoint>(((Shape)fromCentre).Intersect(circle));

        // A ray through the centre crosses twice.
        var through = new VRay(new VXYZ(0, 0), TowardCircle());
        var group = Assert.IsType<VGroup>(((Shape)through).Intersect(circle));
        Assert.Equal(2, group.Shapes.Count);
    }

    [Fact]
    public void IntersectDoesNotDrawItsAnswer()
    {
        // Query methods must not litter the canvas with their result (the rule GeometryHelper's
        // IntersectLineLine and friends already follow).
        var registry = new CountingRegistry();
        var previousRegistry = Shape.DefaultRegistry;
        var previousAuto = Shape.AutoRegister;

        try
        {
            Shape.DefaultRegistry = registry;
            Shape.AutoRegister = true;

            var ray = new VRay(new VXYZ(0, 0), TowardCircle());
            var circle = Circle();
            registry.Count = 0;

            for (int i = 0; i < 20; i++)
            {
                ray.DoesIntersect(circle);
                ((Shape)ray).Intersect(circle);
                ray.Intersect((ICurve)circle);
            }

            Assert.Equal(0, registry.Count);
        }
        finally
        {
            Shape.AutoRegister = previousAuto;
            Shape.DefaultRegistry = previousRegistry;
        }
    }

    [Fact]
    public void RayQueriesAreNotSampledIntoAThousandChords()
    {
        // VRay/VXLine used to fall through to IntersectGeneric, which sampled both curves to their
        // 1000-segment cap: a million segment pairs, ~65 ms for a single ray against a single
        // circle. A few hundred rays over a handful of obstacles then took minutes.
        var circle = Circle();
        var toward = TowardCircle();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++)
        {
            var ray = new VRay(new VXYZ(0, 0), toward);
            ray.DoesIntersect(circle);
        }
        watch.Stop();

        // Two orders of magnitude of headroom over the analytic path, and three below the old one.
        Assert.True(watch.Elapsed.TotalSeconds < 2.0,
            $"2000 ray/circle queries took {watch.Elapsed.TotalSeconds:F2}s — the generic sampling path is back.");
    }

    [Fact]
    public void RayAndLineOverTheSameSpanAgreeExactly()
    {
        var circle = Circle();
        var toward = TowardCircle();

        var ray = new VRay(new VXYZ(0, 0), toward);
        var line = new VLine(new VXYZ(0, 0), ray.RenderExtent * toward);

        var rayPoints = ray.Intersect((ICurve)circle).Points.OrderBy(p => p.X).ToList();
        var linePoints = line.Intersect((ICurve)circle).Points.OrderBy(p => p.X).ToList();

        Assert.Equal(linePoints.Count, rayPoints.Count);
        for (int i = 0; i < linePoints.Count; i++)
        {
            Assert.Equal(linePoints[i].X, rayPoints[i].X, 9);
            Assert.Equal(linePoints[i].Y, rayPoints[i].Y, 9);
        }
    }

    private sealed class CountingRegistry : IShapeRegistry
    {
        public int Count;
        public void Register(Shape shape) => Count++;
        public void Unregister(Shape shape) { }
        public void Clear() { }
        public void NotifyOrderChanged(Shape shape) { }
        public void Place(Shape shape, Viewport viewport) => Register(shape);
    }
}
