using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="Shape.ZIndex"/> — the global draw order that replaced <c>BringAbove</c>/
/// <c>SendBehind</c>.
///
/// <para>
/// The pair it replaced reordered the registry's list once, so the answer to "what is on top"
/// depended on the order the calls happened to be made in and was undone by the next shape to be
/// created. Order is now a property of the shape, so it survives anything added afterwards.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ZIndexTests : IDisposable
{
    private readonly IShapeRegistry? _previousRegistry;
    private readonly bool _previousAutoRegister;

    public ZIndexTests()
    {
        _previousRegistry = Shape.DefaultRegistry;
        _previousAutoRegister = Shape.AutoRegister;
        Shape.AutoRegister = true;
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        CanvasRenderer.Instance.ClearShapes();
    }

    public void Dispose()
    {
        CanvasRenderer.Instance.ClearShapes();
        Shape.DefaultRegistry = _previousRegistry;
        Shape.AutoRegister = _previousAutoRegister;
    }

    private static List<long> DrawOrderIds() =>
        CanvasRenderer.Instance.GetShapes().OfType<Shape>().Select(s => s.Id).ToList();

    [Fact]
    public void DefaultsToZeroAndLeavesCreationOrderAlone()
    {
        var a = new VCircle(new VXYZ(0, 0), 1);
        var b = new VCircle(new VXYZ(0, 0), 2);
        var c = new VCircle(new VXYZ(0, 0), 3);

        Assert.Equal(0, a.ZIndex);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, DrawOrderIds());
    }

    [Fact]
    public void HigherZIndexDrawsLast()
    {
        var background = new VCircle(new VXYZ(0, 0), 1);
        var label = new VText(new VXYZ(0, 0), "on top");

        // Created first, so without ZIndex it would be underneath.
        background.ZIndex = 5;

        Assert.Equal(new[] { label.Id, background.Id }, DrawOrderIds());
    }

    [Fact]
    public void NegativeZIndexPushesBehindEverything()
    {
        var first = new VCircle(new VXYZ(0, 0), 1);
        var backdrop = new VRectangle(new VXYZ(0, 0), 10, 10) { ZIndex = -1 };

        Assert.Equal(new[] { backdrop.Id, first.Id }, DrawOrderIds());
    }

    [Fact]
    public void CreationOrderBreaksTiesWithinABand()
    {
        // A stable sort is what makes this true; an unstable one would scramble the band.
        var a = new VCircle(new VXYZ(0, 0), 1) { ZIndex = 2 };
        var b = new VCircle(new VXYZ(0, 0), 2) { ZIndex = 1 };
        var c = new VCircle(new VXYZ(0, 0), 3) { ZIndex = 2 };
        var d = new VCircle(new VXYZ(0, 0), 4) { ZIndex = 1 };

        Assert.Equal(new[] { b.Id, d.Id, a.Id, c.Id }, DrawOrderIds());
    }

    [Fact]
    public void AShapeCreatedAfterwardsDoesNotLandOnTopOfAHigherBand()
    {
        // The whole point of a global index over the old pairwise calls: BringAbove settled an
        // argument between two shapes and was immediately undone by the next constructor.
        var label = new VText(new VXYZ(0, 0), "always on top") { ZIndex = 10 };
        var later = new VCircle(new VXYZ(0, 0), 1);

        Assert.Equal(new[] { later.Id, label.Id }, DrawOrderIds());
    }

    [Fact]
    public void AssigningZIndexBumpsTheRegistryVersion()
    {
        // The per-frame paths compare RegistryVersion to decide between re-indexing (boxes went
        // stale) and re-snapshotting (the set — or here, its order — changed). Without the bump a
        // ZIndex assigned from a mouse handler would not reach the screen until something else
        // happened to change the set.
        var shape = new VCircle(new VXYZ(0, 0), 1);
        var before = RegistryVersion();

        shape.ZIndex = 3;

        Assert.True(RegistryVersion() > before, "assigning ZIndex must invalidate the draw order");
    }

    [Fact]
    public void AssigningTheSameValueIsANoOp()
    {
        var shape = new VCircle(new VXYZ(0, 0), 1) { ZIndex = 4 };
        var before = RegistryVersion();

        shape.ZIndex = 4;

        Assert.Equal(before, RegistryVersion());
    }

    [Fact]
    public void CloneKeepsTheLayer()
    {
        var original = new VCircle(new VXYZ(0, 0), 1) { ZIndex = 7 };
        Assert.Equal(7, original.Clone().ZIndex);
    }

    [Fact]
    public void TheReplacedPairIsGone()
    {
        // Removing them was the point; a reintroduced BringAbove would reorder the registry's list
        // behind ZIndex's back and the two would disagree about what is on top.
        var members = typeof(Shape).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain("BringAbove", members);
        Assert.DoesNotContain("SendBehind", members);
        Assert.DoesNotContain("MoveAbove", typeof(IShapeRegistry).GetMethods().Select(m => m.Name));
        Assert.DoesNotContain("MoveBehind", typeof(IShapeRegistry).GetMethods().Select(m => m.Name));
    }

    private static int RegistryVersion() => CanvasRenderer.Instance.RegistryVersion;
}
