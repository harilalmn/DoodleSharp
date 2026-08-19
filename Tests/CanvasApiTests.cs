using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The user-facing <see cref="Canvas"/> helper.
///
/// <para>
/// It exists because a callback that redraws had no way to take the previous frame's shapes back
/// off. <c>Frame.Clear()</c> reads as though it should and does not — it drops queued per-frame
/// callbacks — so the reported symptom was a mouse handler leaving a trail of every shape it had
/// ever created.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class CanvasApiTests : IDisposable
{
    private readonly IShapeRegistry? _previousRegistry;
    private readonly bool _previousAutoRegister;

    public CanvasApiTests()
    {
        _previousRegistry = Shape.DefaultRegistry;
        _previousAutoRegister = Shape.AutoRegister;
        Shape.AutoRegister = true;
    }

    public void Dispose()
    {
        Shape.DefaultRegistry = _previousRegistry;
        Shape.AutoRegister = _previousAutoRegister;
    }

    private sealed class CountingRegistry : IShapeRegistry
    {
        public readonly List<Shape> Shapes = new();
        public int ClearCalls;

        public void Register(Shape s) => Shapes.Add(s);
        public void Unregister(Shape s) => Shapes.Remove(s);
        public void Clear() { ClearCalls++; Shapes.Clear(); }
        public void NotifyOrderChanged(Shape s) { }
        public void Place(Shape s, Viewport v) => Register(s);
    }

    [Fact]
    public void TheTemplatesDoNotShadowTheCanvasClass()
    {
        // DoodleSharp.Canvas is also a NAMESPACE, and in any file that imports it the namespace wins
        // — `Canvas.Clear()` then fails to compile with "the type or namespace name 'Clear' does not
        // exist". That is why this very test file has to qualify the calls. It is harmless for user
        // code only because the templates import C2VGeometry, DoodleSharp.Animation and
        // DoodleSharp.Console and never DoodleSharp.Canvas. If a template ever adds it, every
        // sketch calling Canvas.Clear() breaks at once.
        var templates = File.ReadAllText(
            Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "Project", "Templates.cs"));

        Assert.DoesNotContain("using DoodleSharp.Canvas;", templates);
    }

    [Fact]
    public void ClearRemovesEveryShape()
    {
        var registry = new CountingRegistry();
        Shape.DefaultRegistry = registry;

        new VCircle(new VXYZ(0, 0), 10);
        new VLine(0, 0, 10, 10);
        new VText(new VXYZ(0, 0), "A");

        Assert.Equal(3, registry.Shapes.Count);

        C2VGeometry.Canvas.Clear();

        Assert.Empty(registry.Shapes);
        Assert.Equal(1, registry.ClearCalls);
    }

    [Fact]
    public void ClearWithNoRegistryIsANoOp()
    {
        // A unit test or a headless host has no canvas attached. Throwing there would make the
        // helper unusable in exactly the places that are easiest to test in.
        Shape.DefaultRegistry = null;

        C2VGeometry.Canvas.Clear();
        C2VGeometry.Canvas.Remove(new VCircle(new VXYZ(0, 0), 5));
        C2VGeometry.Canvas.Remove((IEnumerable<Shape>)null!);
        C2VGeometry.Canvas.Remove((Shape[])null!);
    }

    [Fact]
    public void RemoveTakesShapesOffWithoutTouchingTheRest()
    {
        var registry = new CountingRegistry();
        Shape.DefaultRegistry = registry;

        var keep = new VCircle(new VXYZ(0, 0), 10);
        var dropA = new VLine(0, 0, 10, 10);
        var dropB = new VText(new VXYZ(0, 0), "A");

        C2VGeometry.Canvas.Remove(dropA, dropB);

        Assert.Single(registry.Shapes);
        Assert.Same(keep, registry.Shapes[0]);
        Assert.Equal(0, registry.ClearCalls);
    }

    [Fact]
    public void RemoveToleratesNullsAndStrangers()
    {
        var registry = new CountingRegistry();
        Shape.DefaultRegistry = registry;

        var onCanvas = new VCircle(new VXYZ(0, 0), 10);

        Shape.AutoRegister = false;
        var neverAdded = new VCircle(new VXYZ(50, 50), 5);
        Shape.AutoRegister = true;

        // A rebuild loop routinely holds a list with gaps and shapes it already removed.
        C2VGeometry.Canvas.Remove(null!, neverAdded, onCanvas, neverAdded);

        Assert.Empty(registry.Shapes);
    }

    [Fact]
    public void RemoveAcceptsALiveViewOfTheRegistry()
    {
        // GetShapes() is the obvious argument, and it is a view over the collection Remove mutates.
        // Iterating it directly would throw halfway through.
        var registry = new CountingRegistry();
        Shape.DefaultRegistry = registry;

        new VCircle(new VXYZ(0, 0), 10);
        new VCircle(new VXYZ(20, 0), 10);
        new VCircle(new VXYZ(40, 0), 10);

        C2VGeometry.Canvas.Remove(registry.Shapes);

        Assert.Empty(registry.Shapes);
    }

    // ── The two Clears must stay distinct ────────────────────────────────────────────────────

    [Fact]
    public void CanvasClearDoesNotRewindShapeIdsOrStopTheTimeline()
    {
        // CanvasRenderer.Clear() is the host's between-runs reset: it also rewinds the id counter
        // and stops the active timeline. Neither is implied by "clear the canvas", and both would be
        // a nasty surprise fired from inside a mouse handler — so the interface Clear() that user
        // code reaches is the geometry half only.
        var renderer = CanvasRenderer.Instance;
        Shape.DefaultRegistry = renderer;
        renderer.Clear();

        new VCircle(new VXYZ(0, 0), 10);
        var before = new VCircle(new VXYZ(20, 0), 10);
        var idBeforeClear = before.Id;

        C2VGeometry.Canvas.Clear();

        Assert.Empty(renderer.GetShapes());

        var after = new VCircle(new VXYZ(40, 0), 10);
        Assert.True(after.Id > idBeforeClear,
            "Canvas.Clear must not rewind the id counter; that is the host's lifecycle reset");

        renderer.Clear();
    }

    [Fact]
    public void TheHostResetStillRewindsIds()
    {
        // ...and the lifecycle clear must keep doing what it always did, or every run would start
        // its shape ids where the last one stopped.
        var renderer = CanvasRenderer.Instance;
        Shape.DefaultRegistry = renderer;

        new VCircle(new VXYZ(0, 0), 10);
        renderer.Clear();

        var first = new VCircle(new VXYZ(0, 0), 10);
        Assert.Equal(1, first.Id);

        renderer.Clear();
    }

    [Fact]
    public void ClearingBumpsTheRegistryVersion()
    {
        // RenderCanvas keeps its own snapshot of the shape list (note 96), and re-snapshots only
        // when RegistryVersion moves. Without the bump the display would keep showing what was
        // just cleared.
        var renderer = CanvasRenderer.Instance;
        Shape.DefaultRegistry = renderer;
        renderer.Clear();

        new VCircle(new VXYZ(0, 0), 10);
        var version = renderer.RegistryVersion;

        C2VGeometry.Canvas.Clear();

        Assert.True(renderer.RegistryVersion > version,
            "a cleared canvas must be visible to the host, not just to the registry");

        renderer.Clear();
    }

    [Fact]
    public void AClearedShapeCanBePlacedAgain()
    {
        // AddShape early-returns on an already-placed shape, so clearing has to reset IsPlaced or a
        // shape the user kept a reference to could never go back on.
        var renderer = CanvasRenderer.Instance;
        Shape.DefaultRegistry = renderer;
        renderer.Clear();

        var circle = new VCircle(new VXYZ(0, 0), 10);
        C2VGeometry.Canvas.Clear();
        Assert.Empty(renderer.GetShapes());

        circle.Place();

        Assert.Single(renderer.GetShapes());

        renderer.Clear();
    }
}
