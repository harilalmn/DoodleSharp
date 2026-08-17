using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using C2VGeometry;
using DoodleSharp.Canvas;
using DoodleSharp.Rendering;

namespace DoodleSharp.Bench;

public sealed record BenchResult(
    string Scene,
    string Path,
    int ShapeCount,
    int Width,
    int Height,
    bool Rasterized,
    double BuildMs,
    FrameSummary Frames,
    double SceneBuildSeconds,
    double IndexBuildMs,
    long WorkingSetBytes,
    double HitTestP99Ms);

/// <summary>
/// Drives a scene through a camera path offscreen and records what it cost.
///
/// <para>
/// Two costs are measured separately, because they fail for different reasons.
/// <b>Build</b> is <c>RedrawAll</c> — culling, tessellation, and emitting the instruction list, all
/// managed code we control. <b>Rasterize</b> additionally renders through
/// <see cref="RenderTargetBitmap"/>, which is where WPF's own stroke tessellation happens in
/// unmanaged MilCore. Reporting only the first would flatter the legacy backend enormously; the
/// second is what the user actually waits for.
/// </para>
/// </summary>
public sealed class BenchRunner
{
    private readonly int _width;
    private readonly int _height;
    private readonly bool _rasterize;

    public BenchRunner(int width, int height, bool rasterize)
    {
        _width = width;
        _height = height;
        _rasterize = rasterize;
    }

    public BenchResult Run(SceneGenerator.Scene scene, CameraPath path, int shapeBudget)
    {
        var sceneWatch = Stopwatch.StartNew();
        SceneGenerator.Build(scene.Name, shapeBudget);
        sceneWatch.Stop();

        var shapes = CanvasRenderer.Instance.GetShapes();
        var shapeCount = shapes.Count;

        var canvas = new RenderCanvas
        {
            Width = _width,
            Height = _height,
        };
        canvas.Measure(new Size(_width, _height));
        canvas.Arrange(new Rect(0, 0, _width, _height));
        canvas.UpdateLayout();

        var indexWatch = Stopwatch.StartNew();
        canvas.Render(shapes);
        indexWatch.Stop();

        var bounds = WorldBounds(shapes);

        var metrics = FrameMetrics.Instance;
        metrics.IsEnabled = true;
        metrics.Reset();

        // Warm up: the first frames pay for JIT, brush and pen cache population, and WPF's own
        // lazy initialisation. Including them would make every run's p99 a measure of start-up.
        for (int i = 0; i < 10; i++)
        {
            path.Apply(canvas.Viewport, i, bounds);
            canvas.Refresh();
            if (_rasterize) Rasterize(canvas);
        }
        metrics.Reset();

        var buildWatch = Stopwatch.StartNew();
        for (int frame = 0; frame < path.FrameCount; frame++)
        {
            path.Apply(canvas.Viewport, frame, bounds);
            canvas.Refresh();
            if (_rasterize) Rasterize(canvas);
        }
        buildWatch.Stop();

        var summary = metrics.Summarize();
        metrics.IsEnabled = false;

        var hitTestP99 = MeasureHitTest(canvas, bounds);

        var result = new BenchResult(
            Scene: scene.Name,
            Path: path.Name,
            ShapeCount: shapeCount,
            Width: _width,
            Height: _height,
            Rasterized: _rasterize,
            BuildMs: buildWatch.Elapsed.TotalMilliseconds / path.FrameCount,
            Frames: summary,
            SceneBuildSeconds: sceneWatch.Elapsed.TotalSeconds,
            IndexBuildMs: indexWatch.Elapsed.TotalMilliseconds,
            WorkingSetBytes: Environment.WorkingSet,
            HitTestP99Ms: hitTestP99);

        CanvasRenderer.Instance.Clear();
        return result;
    }

    private void Rasterize(RenderCanvas canvas)
    {
        var rtb = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
    }

    /// <summary>
    /// Renders one frame of a scene to a PNG. Level of detail and the dense-hatch substitution are
    /// visible changes, not just faster ones — a number saying the frame got quicker tells you
    /// nothing about whether it still shows the right drawing.
    /// </summary>
    public void RenderSnapshot(string sceneName, int shapeBudget, double zoomFraction, string outPath, bool hud = false)
    {
        SceneGenerator.Build(sceneName, shapeBudget);
        var shapes = CanvasRenderer.Instance.GetShapes();

        var canvas = new RenderCanvas { Width = _width, Height = _height };
        canvas.Measure(new System.Windows.Size(_width, _height));
        canvas.Arrange(new Rect(0, 0, _width, _height));
        canvas.UpdateLayout();
        canvas.Render(shapes);

        var bounds = WorldBounds(shapes);
        var span = Math.Max(Math.Max(bounds.Width, bounds.Height), 1e-6);
        canvas.Viewport.SetZoom(_width / (span * zoomFraction));
        canvas.Viewport.CenterOnWorldPoint(
            (bounds.Min.X + bounds.Max.X) * 0.5,
            (bounds.Min.Y + bounds.Max.Y) * 0.5);
        // Exercise the frame-timing readout through the real overlay path, so a snapshot proves it
        // renders rather than merely compiles.
        if (hud)
        {
            canvas.ShowPerformanceHud = true;
            for (int i = 0; i < 12; i++) canvas.Refresh();
        }

        canvas.Refresh();
        canvas.UpdateLayout();

        // The canvas paints no background of its own, so composite it over an opaque rectangle —
        // otherwise the PNG is transparent and impossible to judge (CLAUDE.md note 69).
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, _width, _height));
            dc.DrawRectangle(new VisualBrush(canvas), null, new Rect(0, 0, _width, _height));
        }

        var rtb = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        System.IO.Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath))!);
        using var stream = System.IO.File.Create(outPath);
        encoder.Save(stream);

        CanvasRenderer.Instance.Clear();
    }

    /// <summary>
    /// Clicks 200 scattered points and reports the p99. This is the gate for hit-testing: the old
    /// path was a reverse linear scan over the whole document, so a click in a large drawing cost
    /// one geometry test per shape.
    /// </summary>
    private static double MeasureHitTest(RenderCanvas canvas, BoundingBox bounds)
    {
        var tool = canvas.SelectionTool;
        var index = canvas.SceneIndex;
        var scale = canvas.Viewport.Scale;

        var samples = new double[200];
        for (int i = 0; i < samples.Length; i++)
        {
            var t = i / (double)samples.Length;
            var x = bounds.Min.X + bounds.Width * t;
            var y = bounds.Min.Y + bounds.Height * ((i * 37 % 100) / 100.0);

            var watch = Stopwatch.StartNew();
            tool.HitTest(new VXYZ(x, y), index, scale);
            watch.Stop();
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[(int)(samples.Length * 0.99) - 1];
    }

    private static BoundingBox WorldBounds(IReadOnlyList<IDrawable> shapes)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var drawable in shapes)
        {
            if (drawable is not Shape shape) continue;
            try
            {
                var b = shape.GetBounds();
                if (!double.IsFinite(b.Min.X) || !double.IsFinite(b.Max.X)) continue;
                if (b.Min.X < minX) minX = b.Min.X;
                if (b.Min.Y < minY) minY = b.Min.Y;
                if (b.Max.X > maxX) maxX = b.Max.X;
                if (b.Max.Y > maxY) maxY = b.Max.Y;
            }
            catch { }
        }

        if (minX > maxX) return new BoundingBox(new VXYZ(-100, -100), new VXYZ(100, 100));
        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }
}
