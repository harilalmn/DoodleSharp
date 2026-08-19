using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards two defects found by reading the public surface against its documentation: chrome leaking
/// into exported images, and a boolean operation quietly ignoring the precision it was asked for.
/// </summary>
// Constructing Regions runs through Shape.SuspendAutoRegistration and Region(ICurve) removes its
// source curve from the registry, so this touches the process-wide statics note 9 describes.
[Collection("CanvasState")]
public class ExportFidelityTests
{
    /// <summary>
    /// The overlay layer — F10 frame-timing readout, selection handles, rubber band, snap markers,
    /// measuring overlay — is a visual child of <c>RenderCanvas</c>, and every image and video export
    /// renders the canvas itself. So all of it was being baked into exported PNGs, GIFs and MP4s.
    /// </summary>
    [Fact]
    public void CanvasOffersAnOverlaySuppressionScope()
    {
        var method = typeof(RenderCanvas).GetMethod("SuppressOverlayForCapture",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(IDisposable), method!.ReturnType);
    }

    /// <summary>
    /// Every capture path must actually use it. Reflection cannot see inside a method body and these
    /// paths need a real window to run, so a source scan is what is available — and it is the check
    /// that matters, since the bug was not a missing API but three call sites that never suppressed
    /// anything.
    /// </summary>
    [Theory]
    [InlineData("ExportCanvasToPng")]
    [InlineData("ExportCanvasToGif")]
    [InlineData("ExportCanvasToVideo")]
    public void EveryCapturePathSuppressesTheOverlay(string methodName)
    {
        var source = File.ReadAllText(
            Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

        var start = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodName} not found in MainWindow.xaml.cs");

        // Scan to the start of the next method declaration at the same indentation.
        var next = source.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var body = next > start ? source[start..next] : source[start..];

        Assert.Contains("SuppressOverlayForCapture", body);

        // The whole container is captured, not one cell, so a divided drawing exports tiled exactly
        // as it appears. Also confirms the scan is still looking at a capture path.
        Assert.Contains("rtb.Render(ViewportHost)", body);

        // A capture reads the canvas's own ActualWidth/Height, and a dockable pane that is hidden — or
        // sitting on a non-selected tab, which AvalonDock unloads — reports zero. Before the panels
        // were dockable that took deliberate effort to reach; now it is one click away.
        Assert.Contains("EnsureCanvasReadyForCapture();", body);
    }

    /// <summary>
    /// <c>Intersect</c>, <c>Difference</c> and <c>Xor</c> all took <c>segmentsPerCurve</c> on their
    /// collection folds; <c>Union</c> did not, so it was the one operation that silently ignored the
    /// caller's chosen precision and always sampled curves at the default.
    /// </summary>
    [Fact]
    public void UnionFoldAcceptsSegmentsPerCurveLikeItsSiblings()
    {
        var fold = typeof(RegionBooleanOps)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "Union"
                      && m.GetParameters().Length == 2
                      && m.GetParameters()[0].ParameterType == typeof(IEnumerable<Region>));

        var segments = fold.GetParameters()[1];

        Assert.Equal("segmentsPerCurve", segments.Name);
        Assert.Equal(typeof(int), segments.ParameterType);
        Assert.True(segments.HasDefaultValue);
    }

    /// <summary>
    /// And it must reach the clipper rather than merely being accepted: a coarser sampling of two
    /// overlapping circles produces a visibly different (smaller) union area than a fine one.
    /// </summary>
    [Fact]
    public void UnionFoldActuallyUsesSegmentsPerCurve()
    {
        var coarse = RegionBooleanOps.Union(TwoOverlappingCircles(), segmentsPerCurve: 8);
        var fine = RegionBooleanOps.Union(TwoOverlappingCircles(), segmentsPerCurve: 256);

        Assert.NotNull(coarse);
        Assert.NotNull(fine);

        // A polygon inscribed in a circle always understates its area, so more segments means more
        // area. If the parameter were ignored both calls would return an identical figure.
        Assert.True(fine!.Area > coarse!.Area,
            $"segmentsPerCurve had no effect (coarse {coarse.Area:F4}, fine {fine.Area:F4})");
    }

    private static List<Region> TwoOverlappingCircles()
    {
        using (Shape.SuspendAutoRegistration())
        {
            return new List<Region>
            {
                new Region(new VCircle(new VXYZ(0, 0), 10)),
                new Region(new VCircle(new VXYZ(12, 0), 10)),
            };
        }
    }
}
