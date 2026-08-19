using System;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// The four conveniences that the documentation described for a long time but that were never
/// implemented — <c>DoubleExtensions.ToRadians</c>/<c>ToDegrees</c>, <c>VCircle.Diameter</c>,
/// <c>VPolyline.PointCount</c> and <c>Shape.CopyStyleTo</c> — plus <c>Shape.Place</c>, the name
/// that replaces the overloaded meaning of <c>Draw()</c>.
/// </summary>
[Collection("CanvasState")]
public class NewConvenienceApiTests : IDisposable
{
    private sealed class CountingRegistry : IShapeRegistry
    {
        public readonly System.Collections.Generic.List<Shape> Shapes = new();
        public void Register(Shape s) { if (!Shapes.Contains(s)) Shapes.Add(s); }
        public void Unregister(Shape s) => Shapes.Remove(s);
        public void Clear() => Shapes.Clear();
        public void NotifyOrderChanged(Shape s) { }
    }

    private readonly CountingRegistry _reg = new();

    public NewConvenienceApiTests() => Shape.DefaultRegistry = _reg;
    public void Dispose() => Shape.DefaultRegistry = null;

    private const double Tol = 1e-12;

    // ── Angle conversions ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, Math.PI / 2)]
    [InlineData(180, Math.PI)]
    [InlineData(-45, -Math.PI / 4)]
    public void ToRadiansConverts(double degrees, double expected)
        => Assert.Equal(expected, degrees.ToRadians(), Tol);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Math.PI / 2, 90)]
    [InlineData(Math.PI, 180)]
    public void ToDegreesConverts(double radians, double expected)
        => Assert.Equal(expected, radians.ToDegrees(), 1e-9);

    [Fact]
    public void AngleConversionsRoundTrip()
    {
        Assert.Equal(37.5, 37.5.ToRadians().ToDegrees(), 1e-9);
    }

    [Fact]
    public void ToRadiansAgreesWithTheLibrarysOwnRotation()
    {
        // The point of these helpers is the boundary with System.Math. If they disagreed with the
        // library's degree-based rotation they would be worse than not existing.
        var viaLibrary = VXYZ.BasisX.Rotate(30);
        var viaMath = new VXYZ(Math.Cos(30.0.ToRadians()), Math.Sin(30.0.ToRadians()));

        Assert.Equal(viaMath.X, viaLibrary.X, 1e-9);
        Assert.Equal(viaMath.Y, viaLibrary.Y, 1e-9);
    }

    // ── VCircle.Diameter ────────────────────────────────────────────────────

    [Fact]
    public void DiameterIsTwiceTheRadius()
    {
        var c = new VCircle(0, 0, 10);
        Assert.Equal(20, c.Diameter, Tol);
    }

    [Fact]
    public void SettingDiameterResizesAboutTheCentre()
    {
        var c = new VCircle(new VXYZ(5, 7), 10);
        c.Diameter = 50;

        Assert.Equal(25, c.Radius, Tol);
        Assert.Equal(5, c.Center.X, Tol);   // centre must not move
        Assert.Equal(7, c.Center.Y, Tol);
    }

    [Fact]
    public void DiameterAgreesWithFromCenterDiameter()
    {
        var built = VCircle.FromCenterDiameter(new VXYZ(0, 0), 30);
        Assert.Equal(30, built.Diameter, Tol);
    }

    // ── VPolyline.PointCount ────────────────────────────────────────────────

    [Fact]
    public void PointCountMatchesTheVertexList()
    {
        var p = new VPolyline(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10));
        Assert.Equal(3, p.PointCount);
        Assert.Equal(p.Points.Count, p.PointCount);
    }

    // ── Shape.CopyStyleTo ───────────────────────────────────────────────────

    [Fact]
    public void CopyStyleToCopiesEveryStyleMember()
    {
        var source = new VCircle(0, 0, 5)
        {
            Color = "Red",
            FillColor = "Blue",
            LineWeight = 4.5,
            LineType = LineType.Dashed,
            LineTypeScale = 2.5
        };
        var target = new VCircle(20, 20, 5);

        source.CopyStyleTo(target);

        Assert.Equal("Red", target.Color);
        Assert.Equal("Blue", target.FillColor);
        Assert.Equal(4.5, target.LineWeight);
        Assert.Equal(LineType.Dashed, target.LineType);
        Assert.Equal(2.5, target.LineTypeScale);
    }

    [Fact]
    public void CopyStyleToLeavesGeometryAndIdentityAlone()
    {
        var source = new VCircle(0, 0, 5) { Color = "Red", Name = "source" };
        var target = new VCircle(20, 30, 7) { Name = "target" };

        source.CopyStyleTo(target);

        Assert.Equal(20, target.Center.X, Tol);
        Assert.Equal(7, target.Radius, Tol);
        Assert.Equal("target", target.Name);
        Assert.NotEqual(source.Id, target.Id);
    }

    [Fact]
    public void CopyStyleToReturnsTheTargetForChaining()
    {
        var source = new VCircle(0, 0, 5) { Color = "Lime" };
        var target = new VCircle(1, 1, 1);

        Assert.Same(target, source.CopyStyleTo(target));
    }

    [Fact]
    public void CopyStyleToToleratesNullAndSelf()
    {
        var source = new VCircle(0, 0, 5) { Color = "Red" };

        Assert.Null(source.CopyStyleTo(null));
        Assert.Same(source, source.CopyStyleTo(source));
        Assert.Equal("Red", source.Color);
    }

    // ── Shape.Place ─────────────────────────────────────────────────────────

    [Fact]
    public void PlacePutsAnUnregisteredShapeOnTheCanvas()
    {
        // The case Place exists for: a query result, which deliberately does not register.
        var a = new VLine(0, 0, 10, 10);
        var b = new VLine(0, 10, 10, 0);
        _reg.Shapes.Clear();

        var hit = GeometryHelper.IntersectLineLine(a, b);
        Assert.Empty(_reg.Shapes);

        hit!.Place();

        Assert.Single(_reg.Shapes);
        Assert.True(hit.IsExplicitlyDrawn);
    }

    [Fact]
    public void PlaceIsIdempotent()
    {
        var c = new VCircle(0, 0, 5);
        _reg.Shapes.Clear();

        c.Place();
        c.Place();

        Assert.Single(_reg.Shapes);
    }

    [Fact]
    public void DrawIsExactlyPlace()
    {
        // Draw is kept as the historical name; if the two ever diverge, every existing sample and
        // user project silently changes behaviour.
        var viaPlace = new VCircle(0, 0, 5);
        var viaDraw = new VCircle(10, 0, 5);

        viaPlace.Place();
        viaDraw.Draw();

        Assert.Equal(viaPlace.IsExplicitlyDrawn, viaDraw.IsExplicitlyDrawn);
        Assert.Contains(viaPlace, _reg.Shapes);
        Assert.Contains(viaDraw, _reg.Shapes);
    }

    [Fact]
    public void PlaceIsReachableThroughIDrawable()
    {
        // CanvasRenderer.GetShapes() hands back IDrawable, so "prefer Place()" has to compile there
        // too — otherwise the advice fails in exactly the place the docs send people.
        IDrawable shape = new VCircle(0, 0, 5);
        _reg.Shapes.Clear();

        shape.Place();

        Assert.Single(_reg.Shapes);
    }

    [Fact]
    public void PlaceThroughICurveReachesTheShapesImplementation()
    {
        ICurve curve = new VLine(0, 0, 10, 10);
        _reg.Shapes.Clear();

        curve.Place();

        // The interface default forwards to Draw(); Shape overrides Place() outright. Either way
        // the shape must end up registered exactly once — a default that recursed would hang.
        Assert.Single(_reg.Shapes);
        Assert.True(((Shape)curve).IsExplicitlyDrawn);
    }

    [Fact]
    public void RemoveIsTheInverseOfPlace()
    {
        var c = new VCircle(0, 0, 5);
        _reg.Shapes.Clear();

        c.Place();
        c.Remove();

        Assert.Empty(_reg.Shapes);
    }
}
