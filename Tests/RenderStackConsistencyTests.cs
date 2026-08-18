using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using C2VGeometry;
using C2VGeometry.Rendering;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Invariants that keep the three render backends and the exporters telling the same story.
///
/// <para>
/// Each of these guards a defect that was live: a property honoured by one backend and ignored by
/// another, a pattern table duplicated and allowed to drift, a mutation the GPU path never saw.
/// They are the same class of failure as note 92's five arrowhead implementations — nothing throws,
/// the drawing just looks different depending on which code path happened to run.
/// </para>
/// </summary>
public class RenderStackConsistencyTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), Path.Combine(parts)));

    // ── One dash definition ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LineType.Dashed)]
    [InlineData(LineType.Dotted)]
    [InlineData(LineType.DashDot)]
    [InlineData(LineType.DashDotDot)]
    [InlineData(LineType.Center)]
    [InlineData(LineType.Phantom)]
    [InlineData(LineType.Hidden)]
    public void EveryNonContinuousLineTypeHasAPattern(LineType lineType)
    {
        // The raster table used to end in a null default arm, so Center, Phantom and Hidden drew as
        // solid lines on that backend while dashing correctly on the other. A missing pattern must
        // be impossible, not merely unlikely.
        var pattern = LineTypePatterns.DevicePixels(lineType);

        Assert.False(pattern.IsEmpty, $"{lineType} has no dash pattern and would render solid");
        Assert.True(pattern.Length % 2 == 0, "runs alternate dash/gap, so the count must be even");

        foreach (var run in pattern)
            Assert.True(run > 0, $"{lineType} has a zero-length run, which rasterises as nothing");
    }

    [Fact]
    public void ContinuousIsTheOnlySolidLineType()
    {
        Assert.True(LineTypePatterns.DevicePixels(LineType.Continuous).IsEmpty);
        Assert.True(LineTypePatterns.IsSolid(LineType.Continuous, 1.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void ADegenerateScaleIsTreatedAsSolid(double scale)
    {
        // Scaling every run to zero produces nothing at all on screen, which reads as a missing
        // line rather than a solid one. Solid is the safe interpretation.
        Assert.True(LineTypePatterns.IsSolid(LineType.Dashed, scale));
    }

    [Fact]
    public void NoBackendCarriesItsOwnDashTable()
    {
        // A behavioural test cannot catch a further copy appearing in a path the tests do not
        // render, which is how five arrowhead implementations accumulated (note 92). Scan instead.
        foreach (var file in new[]
        {
            Path.Combine("Canvas", "RenderCanvas.cs"),
            Path.Combine("Rendering", "Raster", "RasterPrimitiveSink.cs"),
            Path.Combine("Canvas", "SvgExporter.cs"),
        })
        {
            var code = Read(file);

            Assert.Contains("LineTypePatterns", code);

            // The literal shape of both retired tables.
            Assert.DoesNotContain("LineType.DashDotDot => new", code);
            Assert.DoesNotContain("LineType.Phantom => new", code);
        }
    }

    // ── Every mutation reaches the GPU upload ────────────────────────────────────────────────

    [Fact]
    public void EveryCanvasMutatorBumpsTheSceneVersion()
    {
        // _sceneVersion tells the GPU backend its vertex buffer is stale. UpdateShapePosition and
        // Refresh did not bump it, so a dragged or edited shape moved its hit-testing and its
        // selection handles while the geometry stayed painted where it was.
        var code = Read("Canvas", "RenderCanvas.cs");

        foreach (var method in new[]
        {
            "public void Refresh()",
            "public void UpdateShapePosition(IDrawable shape)",
        })
        {
            var at = code.IndexOf(method, StringComparison.Ordinal);
            Assert.True(at > 0, $"{method} must exist");

            var body = code[at..Math.Min(at + 1400, code.Length)];
            Assert.Contains("_sceneVersion++", body);
        }
    }

    [Fact]
    public void BothRasterBackendsHonourTessellateReturn()
    {
        // Note 81: the return value is not optional. The GPU path discarded it, so dimensions,
        // arrows, grids and construction lines silently did not exist on that backend, while the
        // managed backend deferred them correctly.
        foreach (var file in new[]
        {
            Path.Combine("Rendering", "Raster", "ManagedRasterBackend.cs"),
            Path.Combine("Rendering", "Raster", "D3D11RasterBackend.cs"),
        })
        {
            var code = Read(file);
            Assert.Matches(new Regex(@"if\s*\(\s*!\s*\w+\.Tessellate\("), code);
        }
    }

    // ── Layer order ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheGridIsBeneathTheGeometry()
    {
        // The grid was drawn into the vector layer, which composites ABOVE the raster bitmap, so
        // with any raster backend active it painted straight over the drawing. Grid under geometry
        // under annotation under overlay is the order a drafting viewport needs.
        var code = Read("Canvas", "RenderCanvas.cs");

        var at = code.IndexOf("GetVisualChild(int index)", StringComparison.Ordinal);
        Assert.True(at > 0);

        var body = code[at..(at + 400)];
        var order = Regex.Matches(body, @"\d+\s*=>\s*(_\w+)").Select(m => m.Groups[1].Value).ToArray();

        // Index order IS z-order, bottom first. The vector layer is plain `_visual` with no suffix,
        // which is why this captures the whole identifier rather than a "…Visual" shape.
        Assert.Equal(new[] { "_gridVisual", "_rasterVisual", "_visual", "_overlayVisual" }, order);
    }

    // ── The line weight setting is not silently discarded ────────────────────────────────────

    [Fact]
    public void DisplayLineWeightKeepsTheVectorBackend()
    {
        // Neither raster backend reads PenSpec.LineWeight, and Auto switches to raster on frame time
        // and shape count — so on a large drawing the setting turned itself off with nothing said.
        var code = Read("Canvas", "RenderCanvas.cs");

        var at = code.IndexOf("private bool ShouldUseRasterBackend()", StringComparison.Ordinal);
        Assert.True(at > 0);

        var body = code[at..(at + 1600)];
        Assert.Contains("DisplayLineWeight", body);

        // ...and an explicitly named backend must still win, so the check has to come after them.
        var explicitGpu = body.IndexOf("GPU", StringComparison.Ordinal);
        var displayCheck = body.IndexOf("DisplayLineWeight", StringComparison.Ordinal);
        Assert.True(explicitGpu > 0 && explicitGpu < displayCheck,
            "naming a backend is a deliberate choice and must take priority");
    }

    // ── SVG stroke units ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SvgPinsStrokeWidthToDevicePixels()
    {
        // The viewBox is world coordinates at 1:1, so a bare stroke-width was read as world units —
        // LineWeight = 2 became two world units, invisible on a large drawing.
        var code = Read("Canvas", "SvgExporter.cs");

        Assert.Contains("non-scaling-stroke", code);

        // Every stroke width the exporter emits must carry the extras helper with it, so a new
        // element cannot reintroduce the world-unit stroke.
        var widths = Regex.Matches(code, @"stroke-width=").Count;
        var extras = Regex.Matches(code, @"\{StrokeExtras\(").Count;

        Assert.True(widths > 0, "the exporter must still emit stroke widths");
        Assert.Equal(widths, extras);
    }
}
