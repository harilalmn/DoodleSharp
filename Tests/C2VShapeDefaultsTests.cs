using System;
using Xunit;
using DoodleSharp.Canvas;
using C2VGeometry;

namespace DoodleSharp.Tests;

// Verifies that C2VGeometry shapes honor the global overrides in
// C2VGeometry.ShapeDefaults, falling back to their per-type hardcoded defaults
// when no global override is set.
//
// We force Shape.DefaultRegistry = null so constructing shapes never registers
// them anywhere, and Reset() the global defaults around every test for isolation.
// Because Shape.DefaultRegistry is a process-wide static that the canvas tests
// also depend on, this class lives in the serialized "CanvasState" collection and
// restores the registry to CanvasRenderer.Instance on teardown.
[Collection("CanvasState")]
public class C2VShapeDefaultsTests : IDisposable
{
    public C2VShapeDefaultsTests()
    {
        Shape.DefaultRegistry = null;
        ShapeDefaults.Reset();
    }

    public void Dispose()
    {
        ShapeDefaults.Reset();
        Shape.DefaultRegistry = CanvasRenderer.Instance;
    }

    [Fact]
    public void GlobalColor_OverridesPerTypeDefault()
    {
        ShapeDefaults.GlobalColor = "HotPink";

        var circle = new VCircle(0, 0, 1);

        Assert.Equal("HotPink", circle.Color);
    }

    [Fact]
    public void Reset_RestoresPerTypeDefault()
    {
        ShapeDefaults.GlobalColor = "HotPink";
        ShapeDefaults.Reset();

        var circle = new VCircle(0, 0, 1);

        Assert.Equal("Yellow", circle.Color);
    }

    [Fact]
    public void GlobalLineWeight_OverridesDefault()
    {
        ShapeDefaults.GlobalLineWeight = 7;

        var circle = new VCircle(0, 0, 1);

        Assert.Equal(7, circle.LineWeight);
    }
}
