using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using C2VGeometry;
using C2VGeometry.Rendering;
using DoodleSharp.Canvas;
using DoodleSharp.Rendering;
using DoodleSharp.Rendering.Raster;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Note 145: a <see cref="VPoint"/> was drawn by nothing. Level of detail read its zero-size bounds as
/// sub-pixel and skipped it on every backend, so a point was visible only while selected (the overlay
/// draws its own marker). Behind that, the rasterizers drew a point as one pixel, and Auto flipped
/// backends on alternate repaints of a small scene.
/// </summary>
[Collection("CanvasState")]
public class PointVisibilityTests
{
    // ── Level of detail ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0)]
    [InlineData(1e-6)]     // zoomed right out
    public void LevelOfDetailAlwaysDrawsAPoint(double scale)
    {
        using var _ = Shape.SuspendAutoRegistration();
        var point = new VPoint(3, 4);

        Assert.Equal(LodLevel.Full, LodPolicy.Classify(point, 0, scale));
    }

    [Fact]
    public void LevelOfDetailStillSkipsOtherSubPixelShapes()
    {
        using var _ = Shape.SuspendAutoRegistration();
        var tiny = new VLine(new VXYZ(0, 0), new VXYZ(0.1, 0));

        Assert.Equal(LodLevel.Skip, LodPolicy.Classify(tiny, 0.1, 1.0));
    }

    // ── The rasterizer's marker ──────────────────────────────────────────────────────────────

    private const int W = 32, H = 24;
    private const int Ink = unchecked((int)0xFFFFFFFF);

    private static int Lit(int[] px) => px.Count(p => p != 0);

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(2.0, 13)]    // the default marker: PointMarker.DotRadius plus the half-pixel pen
    public void DrawDiscLightsADisc(double radius, int expected)
    {
        var px = new int[W * H];
        HairlineRasterizer.DrawDisc(px, W, H, 10, 10, radius, Ink, 0, H - 1);
        Assert.Equal(expected, Lit(px));
    }

    [Fact]
    public void DrawDiscHonoursTheBand()
    {
        var px = new int[W * H];
        HairlineRasterizer.DrawDisc(px, W, H, 10, 10, 2.0, Ink, 10, 10);
        Assert.Equal(5, Lit(px));   // only the centre row of the disc
    }

    [Theory]
    [InlineData(1e12, 5)]
    [InlineData(-1e12, 5)]
    [InlineData(double.NaN, 5)]
    public void DrawDiscRejectsFarAndNonFiniteCentres(double x, double y)
    {
        var px = new int[W * H];
        HairlineRasterizer.DrawDisc(px, W, H, x, y, 2.0, Ink, 0, H - 1);
        Assert.Equal(0, Lit(px));
    }

    [Theory]
    [InlineData(false, 13, 13)]
    [InlineData(true, 60, 120)]
    public void TheRasterSinkDrawsAPointAsAMarker(bool patch, int min, int max)
    {
        var settings = ApplicationSettings.Instance;
        var previous = settings.DrawPointAsPatch;
        settings.DrawPointAsPatch = patch;
        try
        {
            using var _ = Shape.SuspendAutoRegistration();
            var buffer = new RasterCommandBuffer();
            var sink = new RasterPrimitiveSink();
            sink.Begin(buffer, 1.0, (x, y) => (x, y));

            Assert.True(new ShapeTessellator().Tessellate(new VPoint(12, 12), sink));

            var px = new int[W * H];
            buffer.Replay(px, W, H, 0, H - 1, new PolygonFiller(), new List<IReadOnlyList<ScreenPoint>>());

            var lit = Lit(px);
            Assert.InRange(lit, min, max);
        }
        finally
        {
            settings.DrawPointAsPatch = previous;
        }
    }

    // ── The real canvas ──────────────────────────────────────────────────────────────────────

    private const int CW = 400, CH = 300;

    private static readonly FieldInfo RasterActive =
        typeof(RenderCanvas).GetField("_rasterActive", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo LastVectorMs =
        typeof(RenderCanvas).GetField("_lastVectorFrameMs", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo ToScreen = typeof(RenderCanvas).GetMethod("WorldToScreen",
        BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(double), typeof(double) })!;

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");

        if (failure != null)
            throw new Xunit.Sdk.XunitException($"{failure.GetType().Name}: {failure.Message}\n{failure.StackTrace}");
    }

    /// <summary>Runs a body against a laid-out canvas with the backend set in memory, then restores the settings.</summary>
    private static void WithCanvas(string backend, Action<RenderCanvas, VPoint> body) => OnStaThread(() =>
    {
        var settings = ApplicationSettings.Instance;
        var (oldBackend, oldPatch, oldWeight) = (settings.RenderBackend, settings.DrawPointAsPatch, settings.DisplayLineWeight);
        settings.RenderBackend = backend;
        settings.DrawPointAsPatch = false;
        settings.DisplayLineWeight = false;
        try
        {
            VPoint point;
            using (Shape.SuspendAutoRegistration())
                point = new VPoint(60, 50);   // clear of both axes

            var canvas = new RenderCanvas { Width = CW, Height = CH };
            canvas.Measure(new System.Windows.Size(CW, CH));
            canvas.Arrange(new Rect(0, 0, CW, CH));
            canvas.UpdateLayout();
            canvas.Render(new List<IDrawable> { point });

            body(canvas, point);
        }
        finally
        {
            (settings.RenderBackend, settings.DrawPointAsPatch, settings.DisplayLineWeight) = (oldBackend, oldPatch, oldWeight);
        }
    });

    /// <summary>Bright pixels within 6 px of the point, from what the canvas actually painted.</summary>
    private static int LitAround(RenderCanvas canvas, VPoint point)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, CW, CH));
            dc.DrawRectangle(new VisualBrush(canvas), null, new Rect(0, 0, CW, CH));
        }
        var bitmap = new RenderTargetBitmap(CW, CH, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dv);
        var pixels = new int[CW * CH];
        bitmap.CopyPixels(pixels, CW * 4, 0);

        var s = (System.Windows.Point)ToScreen.Invoke(canvas, new object[] { point.X, point.Y })!;
        int cx = (int)Math.Round(s.X), cy = (int)Math.Round(s.Y), n = 0;
        for (int y = cy - 6; y <= cy + 6; y++)
            for (int x = cx - 6; x <= cx + 6; x++)
            {
                if (x < 0 || y < 0 || x >= CW || y >= CH) continue;
                int c = pixels[y * CW + x];
                if (((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF) > 600) n++;
            }
        return n;
    }

    [Theory]
    [InlineData("Legacy")]
    [InlineData("Managed")]
    public void APointIsDrawnWithoutBeingSelected(string backend) => WithCanvas(backend, (canvas, point) =>
    {
        // White on the dark background. Before the fix this was 0 on every backend.
        Assert.True(LitAround(canvas, point) >= 4, $"the point was not drawn on the {backend} backend");
    });

    [Fact]
    public void AutoDoesNotFlipBackendsOnASmallScene() => WithCanvas("Auto", (canvas, _) =>
    {
        // One slow vector frame, as the first frame after a run is. With the old bookkeeping the next
        // repaint went raster, recorded that raster frame's time as a vector one, and flipped again
        // every other repaint.
        LastVectorMs.SetValue(canvas, 50.0);

        for (int i = 0; i < 4; i++)
        {
            canvas.Refresh();
            Assert.False((bool)RasterActive.GetValue(canvas)!, $"repaint {i + 1} switched to the rasterizer");
        }
    });
}
