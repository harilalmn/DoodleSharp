using System;
using Xunit;
using DoodleSharp.Canvas;
using DoodleSharp.Execution;
using DoodleSharp.Sketching;
using C2V = C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// The teardown contract around a run: whatever the previous run armed must be dropped before the
/// next one, and at every point a collectible <c>AssemblyLoadContext</c> is about to be unloaded.
///
/// <para>
/// A queued <c>Frame</c> callback is a delegate pointing into the user assembly, so leaving one
/// behind has two distinct consequences depending on the site. Where the ALC is being unloaded it
/// <i>pins</i> it, so the unload silently does nothing and the old run's callbacks keep firing
/// against shapes a later run has replaced. Where the same IL is being re-invoked
/// (<c>ReExecuteResidentAsync</c>, which a Global-Parameters slider drag hits many times a second)
/// nothing is pinned but the arming <i>accumulates</i> — the drag stacked one live animation loop per
/// tick and the motion visibly accelerated.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class RunLifecycleTests : IDisposable
{
    public RunLifecycleTests()
    {
        DoodleSharp.Animation.Frame.Clear();
        SketchRuntime.Instance.Stop();
        CanvasRenderer.Instance.Clear();
        C2V.Shape.DefaultRegistry = null;
    }

    public void Dispose()
    {
        DoodleSharp.Animation.Frame.Clear();
        SketchRuntime.Instance.Stop();
        CanvasRenderer.Instance.Clear();
        C2V.Shape.DefaultRegistry = null;
    }

    [Fact]
    public void InvalidateResidentWithNothingResidentLeavesCallbacksAlone()
    {
        // The clear in InvalidateResident is deliberately scoped to the branch that actually unloads
        // a context, and this test exists to keep it there. Making it unconditional looks safer and
        // is worse: InvalidateResident is also called on a programmatic source edit and on a project
        // switch, so an unconditional clear would silently kill a running animation in cases where
        // no assembly is being torn down and nothing is pinned. A Frame loop started by a successful
        // Main() always *has* a resident context (ExecuteAssemblyAsync makes the assembly resident on
        // success), so the branch that matters is the branch that is covered.
        ModuleCompiler.InvalidateResident();   // ensure there is no resident context

        DoodleSharp.Animation.Frame.Request(() => { });
        ModuleCompiler.InvalidateResident();

        Assert.True(DoodleSharp.Animation.Frame.HasPending);
    }

    [Fact]
    public void SketchStopDropsQueuedFrameCallbacks()
    {
        // Start a sketch so Stop() gets past its no-op early return.
        SketchRuntime.Instance.Start(typeof(BareSketch), System.Runtime.Loader.AssemblyLoadContext.Default);
        DoodleSharp.Animation.Frame.Request(() => { });
        Assert.True(DoodleSharp.Animation.Frame.HasPending);

        SketchRuntime.Instance.Stop();

        Assert.False(DoodleSharp.Animation.Frame.HasPending);
    }

    [Fact]
    public void SketchStopWithNoSketchRunningStillReturnsQuietly()
    {
        // Stop() early-returns here, which is exactly why the Stop *button* has to clear Frame
        // itself rather than relying on this call. Pin the early return so the button's own
        // unconditional clear is not mistaken for redundancy later.
        DoodleSharp.Animation.Frame.Request(() => { });

        SketchRuntime.Instance.Stop();

        Assert.True(DoodleSharp.Animation.Frame.HasPending);
    }

    [Fact]
    public async System.Threading.Tasks.Task ResidentReExecuteWithNoResidentAssemblyFailsCleanly()
    {
        ModuleCompiler.InvalidateResident();

        var result = await ModuleCompiler.ReExecuteResidentAsync();

        Assert.False(result.Success);
        Assert.Contains("resident", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── RegistryVersion: the signal that separates "shapes moved" from "shapes added/removed" ──

    [Fact]
    public void RegistryVersionMovesWhenAShapeIsAdded()
    {
        var before = CanvasRenderer.Instance.RegistryVersion;

        CanvasRenderer.Instance.AddShape(new C2V.VCircle(new C2V.VXYZ(0, 0), 5));

        Assert.NotEqual(before, CanvasRenderer.Instance.RegistryVersion);
    }

    [Fact]
    public void RegistryVersionMovesWhenAShapeIsRemoved()
    {
        var circle = new C2V.VCircle(new C2V.VXYZ(0, 0), 5);
        CanvasRenderer.Instance.AddShape(circle);
        var before = CanvasRenderer.Instance.RegistryVersion;

        CanvasRenderer.Instance.RemoveShape(circle);

        Assert.NotEqual(before, CanvasRenderer.Instance.RegistryVersion);
    }

    [Fact]
    public void RegistryVersionDoesNotMoveWhenAShapeIsOnlyMutated()
    {
        var circle = new C2V.VCircle(new C2V.VXYZ(0, 0), 5);
        CanvasRenderer.Instance.AddShape(circle);
        var before = CanvasRenderer.Instance.RegistryVersion;

        // The common per-frame case: same objects, new positions. The host relies on the version
        // *not* moving here, so it can take the cheap re-index path instead of re-snapshotting.
        circle.Center = new C2V.VXYZ(50, 50);
        circle.Radius = 9;

        Assert.Equal(before, CanvasRenderer.Instance.RegistryVersion);
    }

    [Fact]
    public void RegistryVersionDoesNotMoveOnADuplicateAdd()
    {
        var circle = new C2V.VCircle(new C2V.VXYZ(0, 0), 5);
        CanvasRenderer.Instance.AddShape(circle);
        var before = CanvasRenderer.Instance.RegistryVersion;

        // AddShape early-returns on IsPlaced; the version must agree with that.
        CanvasRenderer.Instance.AddShape(circle);

        Assert.Equal(before, CanvasRenderer.Instance.RegistryVersion);
    }

    [Fact]
    public void RegistryVersionDoesNotMoveRemovingAShapeThatIsNotThere()
    {
        CanvasRenderer.Instance.AddShape(new C2V.VCircle(new C2V.VXYZ(0, 0), 5));
        var before = CanvasRenderer.Instance.RegistryVersion;

        CanvasRenderer.Instance.RemoveShape(new C2V.VCircle(new C2V.VXYZ(99, 99), 1));

        Assert.Equal(before, CanvasRenderer.Instance.RegistryVersion);
    }

    [Fact]
    public void RegistryVersionMovesOnClear()
    {
        CanvasRenderer.Instance.AddShape(new C2V.VCircle(new C2V.VXYZ(0, 0), 5));
        var before = CanvasRenderer.Instance.RegistryVersion;

        CanvasRenderer.Instance.Clear();

        Assert.NotEqual(before, CanvasRenderer.Instance.RegistryVersion);
    }

    private class BareSketch : Sketch
    {
    }
}
