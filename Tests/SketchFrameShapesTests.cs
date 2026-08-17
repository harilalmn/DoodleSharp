using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Xunit;
using DoodleSharp.Canvas;
using DoodleSharp.Sketching;
using C2V = C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the fix for "sketch mode renders frame 0 forever".
///
/// <para>
/// <c>SketchRuntime.Tick()</c> calls <c>CanvasRenderer.Clear()</c> and re-runs the user's
/// <c>Draw()</c>, so every frame produces *new shape objects*. But <c>RenderCanvas._currentShapes</c>
/// is a <c>ToList()</c> snapshot assigned only by <c>Render()</c>, which the sketch path never
/// calls — the frame loop called <c>Refresh()</c> alone. The canvas therefore kept repainting the
/// objects captured at Run time: a sketch that *created* its shapes in <c>Draw()</c> was frozen,
/// and only one that mutated <c>Setup()</c>-created objects in place appeared to animate.
/// </para>
///
/// <para>
/// <c>RenderCanvas</c> is a <c>FrameworkElement</c> and cannot be constructed off an STA thread
/// (see <c>UndoSurvivesRunTests</c>), so the wiring is guarded by scanning source — the same idiom
/// as <c>ShapeRotationTests.RenderCanvasAppliesRotationInExactlyOnePlace</c>.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class SketchFrameShapesTests : IDisposable
{
    public SketchFrameShapesTests()
    {
        SketchRuntime.Instance.Stop();
        CanvasRenderer.Instance.Clear();
        C2V.Shape.DefaultRegistry = null;
    }

    public void Dispose()
    {
        SketchRuntime.Instance.Stop();
        CanvasRenderer.Instance.Clear();
        C2V.Shape.DefaultRegistry = null;
    }

    private sealed class CircleSketch : Sketch
    {
        public override void Draw() => new C2V.VCircle(new C2V.VXYZ(0, 0), 10);
    }

    /// <summary>
    /// The reason the wiring is needed: a tick replaces the shape *objects*, it does not mutate
    /// them. Any snapshot the canvas took on a previous frame is stale, not merely out of date.
    /// </summary>
    [Fact]
    public void EachTickProducesFreshShapeObjects()
    {
        SketchRuntime.Instance.Start(typeof(CircleSketch), AssemblyLoadContext.Default);

        SketchRuntime.Instance.Tick();
        var frame1 = CanvasRenderer.Instance.GetShapes().ToList();

        SketchRuntime.Instance.Tick();
        var frame2 = CanvasRenderer.Instance.GetShapes().ToList();

        Assert.NotEmpty(frame1);
        Assert.Equal(frame1.Count, frame2.Count);

        // Not one object survives the tick — so repainting frame 1's list shows frame 1 forever.
        foreach (var shape in frame2)
            Assert.DoesNotContain(frame1, previous => ReferenceEquals(previous, shape));
    }

    /// <summary>
    /// The frame loop must hand the canvas this frame's shapes before repainting. Ordering matters:
    /// <c>Refresh()</c> paints whatever <c>_currentShapes</c> holds at that moment.
    /// </summary>
    [Fact]
    public void SketchFrameLoop_PushesFrameShapesBeforeRefreshing()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "MainWindow.xaml.cs"));

        var tick = source.IndexOf("SketchRuntime.Instance.Tick()", StringComparison.Ordinal);
        Assert.True(tick >= 0, "Could not find the sketch tick in the frame loop.");

        var push = source.IndexOf("SetFrameShapes", tick, StringComparison.Ordinal);
        Assert.True(push >= 0,
            "The sketch frame loop must call RenderCanvas.SetFrameShapes(...) after Tick(). Without " +
            "it the canvas repaints the snapshot Render() took at Run time and the sketch sits on " +
            "frame 0 — see the class remarks.");

        var refresh = source.IndexOf("RenderCanvas.Refresh()", tick, StringComparison.Ordinal);
        Assert.True(refresh >= 0, "Could not find the repaint following the sketch tick.");

        Assert.True(push < refresh,
            "SetFrameShapes must run before Refresh(); Refresh() paints whatever _currentShapes " +
            "holds when it is called, so pushing afterwards repaints the previous frame.");
    }

    /// <summary>
    /// The per-frame path must not rebuild the spatial index. <c>RedrawAll</c> skips culling while a
    /// sketch or timeline is running, so a rebuild would be pure waste at 60 Hz.
    /// </summary>
    [Fact]
    public void SetFrameShapes_DoesNotRebuildTheSpatialIndex()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Canvas", "RenderCanvas.cs"));

        var body = Regex.Match(source,
            @"internal void SetFrameShapes\s*\([^)]*\)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);

        Assert.True(body.Success, "Could not locate RenderCanvas.SetFrameShapes.");
        Assert.DoesNotContain("RebuildSpatialIndex", body.Groups["body"].Value);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DoodleSharp.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (DoodleSharp.sln)");
        return dir!.FullName;
    }
}
