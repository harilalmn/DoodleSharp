using System;
using System.Collections.Generic;
using Xunit;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Regression guard: shapes that build an internal curve representation (VPolygon,
/// VRectangle, Region) must NOT auto-register those edge segments. Once a live
/// IShapeRegistry is attached (as in the app, where CanvasRenderer is the registry),
/// `new VLine(...)` inside BuildCurvesFromPoints would dump phantom edge shapes onto
/// the canvas — the shape's outline would render as separate default-colored lines.
/// The fix uses the non-registering VLine.Internal factory (see CLAUDE.md #10).
/// </summary>
[Collection("CanvasState")]
public class GeometryRegistryPollutionTests : IDisposable
{
    private sealed class CountingRegistry : IShapeRegistry
    {
        public readonly List<Shape> Shapes = new();
        public void Register(Shape s) => Shapes.Add(s);
        public void Unregister(Shape s) => Shapes.Remove(s);
        public void Clear() => Shapes.Clear();
        public void MoveAbove(Shape s, Shape r) { }
        public void MoveBehind(Shape s, Shape r) { }
    }

    private readonly CountingRegistry _reg = new();

    public GeometryRegistryPollutionTests() => Shape.DefaultRegistry = _reg;
    public void Dispose() => Shape.DefaultRegistry = null;

    [Fact]
    public void VPolygon_RegistersOnlyItself_NotItsEdges()
    {
        _reg.Shapes.Clear();
        _ = new VPolygon(new[] { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(5, 8) });
        Assert.Single(_reg.Shapes); // polygon only — not polygon + 3 edge VLines
    }

    [Fact]
    public void VRectangle_RegistersOnlyItself_NotItsEdges()
    {
        _reg.Shapes.Clear();
        _ = new VRectangle(new VXYZ(0, 0), 20, 10);
        Assert.Single(_reg.Shapes); // rectangle only — not rectangle + 4 edge VLines
    }

    [Fact]
    public void Region_FromPolygon_DoesNotRegisterEdges()
    {
        var poly = new VPolygon(new[] { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(5, 8) });
        _reg.Shapes.Clear();
        _ = Region.FromPolygon(poly);
        // FromPolygon uses the non-registering ctor and VLine.Internal edges, so it
        // touches the registry not at all. Before the fix it dumped the edge VLines.
        Assert.Empty(_reg.Shapes);
    }

    // ── Arc intersections ───────────────────────────────────────────────────
    // Same bug class, found later: the arc paths built their supporting circle with
    // `new VCircle(...)`, which auto-registers, so every arc intersection test dropped one or two
    // phantom circles onto the canvas. DoodleSharp's unnamed-shape sweep hid them after Main()
    // returned — they still rendered during the run — and a host with no such sweep would
    // have shown them outright. They now use the non-registering VCircle.Internal.

    [Fact]
    public void IntersectLineArc_DoesNotRegisterItsSupportingCircle()
    {
        var line = new VLine(-20, 0, 20, 0);
        var arc = new VArc(new VXYZ(0, 0), 10, 0, 180);
        _reg.Shapes.Clear();

        _ = CurveIntersection.IntersectLineArc(line, arc);

        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void IntersectCircleArc_DoesNotRegisterItsSupportingCircle()
    {
        var circle = new VCircle(5, 0, 10);
        var arc = new VArc(new VXYZ(0, 0), 10, 0, 180);
        _reg.Shapes.Clear();

        _ = CurveIntersection.IntersectCircleArc(circle, arc);

        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void IntersectArcArc_DoesNotRegisterItsSupportingCircles()
    {
        var a = new VArc(new VXYZ(0, 0), 10, 0, 180);
        var b = new VArc(new VXYZ(8, 0), 10, 0, 180);
        _reg.Shapes.Clear();

        _ = CurveIntersection.IntersectArcArc(a, b);

        Assert.Empty(_reg.Shapes);
    }

    // ── Query methods that have to build a Shape to express their answer ────
    //
    // These return a Shape because the answer's *kind* varies (a crossing is a point, an overlap
    // is a segment). Asking the question must not draw anything: the caller decides, by calling
    // Draw() on the result. Before this, every intersection test silently littered the canvas.

    [Fact]
    public void IntersectLineLine_DoesNotDrawTheCrossingPoint()
    {
        var a = new VLine(0, 0, 10, 10);
        var b = new VLine(0, 10, 10, 0);
        _reg.Shapes.Clear();

        var hit = GeometryHelper.IntersectLineLine(a, b);

        Assert.NotNull(hit);
        Assert.IsType<VPoint>(hit);
        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void IntersectLineLine_DoesNotDrawTheOverlapSegment()
    {
        // Collinear overlap returns a VLine rather than a VPoint — the other construction path.
        var a = new VLine(0, 0, 10, 0);
        var b = new VLine(5, 0, 15, 0);
        _reg.Shapes.Clear();

        var overlap = GeometryHelper.IntersectLineLine(a, b);

        Assert.IsType<VLine>(overlap);
        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void IntersectRectRect_DoesNotDrawTheOverlap()
    {
        var r1 = new VRectangle(0, 0, 10, 10);
        var r2 = new VRectangle(5, 5, 10, 10);
        _reg.Shapes.Clear();

        var overlap = GeometryHelper.IntersectRectRect(r1, r2);

        Assert.IsType<VRectangle>(overlap);
        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void IntersectLineRect_DoesNotDrawTheClippedSegment()
    {
        var line = new VLine(-5, 5, 15, 5);
        var rect = new VRectangle(0, 0, 10, 10);
        _reg.Shapes.Clear();

        var clipped = GeometryHelper.IntersectLineRect(line, rect);

        Assert.IsType<VLine>(clipped);
        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void RayAndXLineConvertersDoNotDrawTheirResult()
    {
        var ray = new VRay(new VXYZ(0, 0), new VXYZ(1, 1));
        var xline = new VXLine(new VXYZ(0, 0), new VXYZ(0, 1));
        _reg.Shapes.Clear();

        _ = ray.ToFiniteLine();
        _ = ray.ToXLine();
        _ = xline.ToFiniteLine();

        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void CloningARegionDoesNotRegisterItsEdges()
    {
        // Clone() is abstract and every shape's implementation registers, so cloning a region's
        // internal loop leaked one shape per edge.
        var poly = new VPolygon(new[] { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10), new VXYZ(0, 10) });
        var region = Region.FromPolygon(poly);
        _reg.Shapes.Clear();

        _ = region.Clone();

        Assert.Empty(_reg.Shapes);
    }

    [Fact]
    public void SuspendingAutoRegistrationRestoresThePreviousValue()
    {
        // The scope must restore, not force true — nesting it inside a Chart build (which already
        // sets the flag false) would otherwise switch registration back on mid-construction.
        Shape.AutoRegister = false;
        try
        {
            using (Shape.SuspendAutoRegistration()) { }
            Assert.False(Shape.AutoRegister);
        }
        finally
        {
            Shape.AutoRegister = true;
        }

        using (Shape.SuspendAutoRegistration())
        {
            Assert.False(Shape.AutoRegister);
        }
        Assert.True(Shape.AutoRegister);
    }
}
