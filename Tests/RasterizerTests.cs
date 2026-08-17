using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DoodleSharp.Rendering.Raster;

namespace DoodleSharp.Tests;

/// <summary>
/// The rasterizer is pure: an <see cref="int"/> array in, an <see cref="int"/> array out, with no
/// GPU, no WPF visual and no device. That is deliberate — it means these run identically on a
/// developer machine and on a GPU-less CI runner, which is exactly what a golden-image test of a
/// hardware-backed renderer could never promise.
/// </summary>
public class RasterizerTests
{
    private const int W = 32;
    private const int H = 24;
    private const int Ink = unchecked((int)0xFFFFFFFF);

    private static int[] Blank() => new int[W * H];

    private static int Lit(int[] px) => px.Count(p => p != 0);

    private static bool IsLit(int[] px, int x, int y) => px[y * W + x] != 0;

    /// <summary>Renders the buffer as text, so a failure shows the picture rather than an index.</summary>
    private static string Render(int[] px)
    {
        var sb = new System.Text.StringBuilder("\n");
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++) sb.Append(IsLit(px, x, y) ? '#' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ── Lines ────────────────────────────────────────────────────────────────

    [Fact]
    public void HorizontalLine_IsExactlyOnePixelTall()
    {
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, 4, 10, 20, 10, Ink, 0, H - 1);

        for (int x = 4; x <= 20; x++)
            Assert.True(IsLit(px, x, 10), $"expected ({x},10) lit{Render(px)}");

        // One pixel tall at every zoom is the whole point of hairline mode.
        Assert.Equal(17, Lit(px));
    }

    [Fact]
    public void VerticalLine_IsExactlyOnePixelWide()
    {
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, 7, 2, 7, 18, Ink, 0, H - 1);

        for (int y = 2; y <= 18; y++) Assert.True(IsLit(px, 7, y));
        Assert.Equal(17, Lit(px));
    }

    [Fact]
    public void DiagonalLine_IsConnected()
    {
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, 0, 0, 20, 15, Ink, 0, H - 1);

        // A DDA line must have no gaps: every lit pixel needs a lit 8-neighbour.
        var lit = new List<(int x, int y)>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsLit(px, x, y)) lit.Add((x, y));

        Assert.NotEmpty(lit);
        foreach (var (x, y) in lit)
        {
            var hasNeighbour = lit.Any(o =>
                (o.x != x || o.y != y) && Math.Abs(o.x - x) <= 1 && Math.Abs(o.y - y) <= 1);
            Assert.True(hasNeighbour, $"({x},{y}) is isolated — the line has a gap{Render(px)}");
        }
    }

    [Fact]
    public void LineFarOutsideTheViewport_IsClippedNotWalked()
    {
        var px = Blank();

        // Endpoints millions of pixels away. Without clipping before the DDA this would walk every
        // one of those steps — which is the normal state of affairs at high zoom, where a line's
        // true endpoints are far off screen.
        var start = DateTime.UtcNow;
        HairlineRasterizer.DrawLine(px, W, H, -5_000_000, 12, 5_000_000, 12, Ink, 0, H - 1);
        var elapsed = DateTime.UtcNow - start;

        Assert.Equal(W, Lit(px));            // the whole row, and nothing else
        Assert.True(elapsed.TotalMilliseconds < 50,
            $"took {elapsed.TotalMilliseconds:F1} ms — the line was walked, not clipped");
    }

    [Fact]
    public void LineCompletelyOffscreen_DrawsNothing()
    {
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, -100, -100, -50, -50, Ink, 0, H - 1);
        HairlineRasterizer.DrawLine(px, W, H, 500, 500, 900, 900, Ink, 0, H - 1);
        Assert.Equal(0, Lit(px));
    }

    [Fact]
    public void NonFiniteCoordinates_AreRejectedNotIndexed()
    {
        var px = Blank();

        // Degenerate geometry reaches the renderer routinely — a zero-length normal, a divide by a
        // zero radius. It must not index the buffer with garbage.
        HairlineRasterizer.DrawLine(px, W, H, double.NaN, 5, 10, 10, Ink, 0, H - 1);
        HairlineRasterizer.DrawLine(px, W, H, 0, 0, double.PositiveInfinity, 5, Ink, 0, H - 1);
        HairlineRasterizer.DrawLine(px, W, H, double.NegativeInfinity, double.NaN, 1, 1, Ink, 0, H - 1);

        Assert.Equal(0, Lit(px));
    }

    [Fact]
    public void ClipBand_ConfinesWritesToItsOwnRows()
    {
        // Tiling draws the same scene into disjoint row bands in parallel. If a band could write
        // outside its rows, two threads would race on the same pixels.
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, 0, 0, 31, 23, Ink, 8, 15);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsLit(px, x, y))
                    Assert.InRange(y, 8, 15);
    }

    [Fact]
    public void SinglePointLine_LightsOnePixel()
    {
        var px = Blank();
        HairlineRasterizer.DrawLine(px, W, H, 9, 9, 9, 9, Ink, 0, H - 1);
        Assert.Equal(1, Lit(px));
        Assert.True(IsLit(px, 9, 9));
    }

    // ── Fills ────────────────────────────────────────────────────────────────

    private static ScreenPoint[] Rect(double x0, double y0, double x1, double y1) =>
        new[]
        {
            new ScreenPoint(x0, y0), new ScreenPoint(x1, y0),
            new ScreenPoint(x1, y1), new ScreenPoint(x0, y1),
        };

    [Fact]
    public void FilledRectangle_CoversItsInteriorExactly()
    {
        var px = Blank();
        var filler = new PolygonFiller();
        filler.Fill(px, W, H, new[] { Rect(4, 4, 12, 10) }, Ink, evenOdd: true, 0, H - 1);

        Assert.True(IsLit(px, 8, 7), $"centre should be filled{Render(px)}");
        Assert.False(IsLit(px, 2, 7), "outside-left should be clear");
        Assert.False(IsLit(px, 20, 7), "outside-right should be clear");
        Assert.False(IsLit(px, 8, 1), "above should be clear");
        Assert.False(IsLit(px, 8, 20), "below should be clear");

        // 8 x 6 interior, allowing a row/column either way for pixel-centre sampling.
        Assert.InRange(Lit(px), 35, 63);
    }

    [Fact]
    public void HoleIsNotFilled_EvenOdd()
    {
        var px = Blank();
        var filler = new PolygonFiller();

        var loops = new IReadOnlyList<ScreenPoint>[]
        {
            Rect(2, 2, 24, 20),      // outer
            Rect(8, 7, 16, 14),      // hole
        };
        filler.Fill(px, W, H, loops, Ink, evenOdd: true, 0, H - 1);

        Assert.True(IsLit(px, 5, 11), $"outer ring should be filled{Render(px)}");
        Assert.False(IsLit(px, 12, 10), $"hole should be empty{Render(px)}");
    }

    [Fact]
    public void FillRespectsTheClipBand()
    {
        var px = Blank();
        var filler = new PolygonFiller();
        filler.Fill(px, W, H, new[] { Rect(2, 2, 28, 20) }, Ink, evenOdd: true, 10, 12);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsLit(px, x, y))
                    Assert.InRange(y, 10, 12);
    }

    [Fact]
    public void DegenerateOutlines_AreIgnored()
    {
        var px = Blank();
        var filler = new PolygonFiller();

        filler.Fill(px, W, H, Array.Empty<IReadOnlyList<ScreenPoint>>(), Ink, true, 0, H - 1);
        filler.Fill(px, W, H, new[] { new[] { new ScreenPoint(1, 1) } }, Ink, true, 0, H - 1);
        filler.Fill(px, W, H,
            new[] { new[] { new ScreenPoint(1, double.NaN), new ScreenPoint(5, 5), new ScreenPoint(9, 1) } },
            Ink, true, 0, H - 1);

        Assert.Equal(0, Lit(px));
    }

    [Fact]
    public void FillIsAllocationFreeAfterWarmUp()
    {
        var px = Blank();
        var filler = new PolygonFiller();
        var loops = new[] { Rect(2, 2, 28, 20) };

        filler.Fill(px, W, H, loops, Ink, true, 0, H - 1);   // grow the crossing buffers

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            filler.Fill(px, W, H, loops, Ink, true, 0, H - 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0,
            $"filling allocated {allocated} bytes over 100 calls; the per-frame path must allocate none");
    }

    // ── Colour ───────────────────────────────────────────────────────────────

    [Fact]
    public void NamedColours_MatchWpfExactly()
    {
        // The rasterizer must not develop its own opinion of what a colour name means; there is one
        // definition and both renderers use it.
        foreach (var name in new[] { "Cyan", "DodgerBlue", "Red", "Black", "White", "Goldenrod" })
        {
            var wpf = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(name)!;
            var packed = ColorTable.Resolve(name);

            Assert.Equal(wpf.A, (byte)((packed >> 24) & 0xFF));
            Assert.Equal(wpf.R, (byte)((packed >> 16) & 0xFF));
            Assert.Equal(wpf.G, (byte)((packed >> 8) & 0xFF));
            Assert.Equal(wpf.B, (byte)(packed & 0xFF));
        }
    }

    [Fact]
    public void HexColours_Parse()
    {
        Assert.Equal(unchecked((int)0xFFFF0000), ColorTable.Resolve("#FF0000"));
        Assert.Equal(unchecked((int)0xFF00FF00), ColorTable.Resolve("#00FF00"));
    }

    [Fact]
    public void UnparseableColour_FallsBackToWhite()
    {
        // Matches the legacy renderer's catch-and-return-white. Sketches unknowingly rely on a
        // typo'd colour still drawing something rather than throwing mid-frame.
        Assert.Equal(ColorTable.Fallback, ColorTable.Resolve("nonsense-not-a-colour"));
        Assert.Equal(ColorTable.Fallback, ColorTable.Resolve(""));
        Assert.Equal(ColorTable.Fallback, ColorTable.Resolve(null));
    }

    [Fact]
    public void OpacityScalesAllChannels_BecauseTheFormatIsPremultiplied()
    {
        var opaque = ColorTable.Resolve("#FFFFFF");
        var half = ColorTable.WithOpacity(opaque, 0.5);

        Assert.Equal(127, (half >> 24) & 0xFF);
        Assert.Equal(127, (half >> 16) & 0xFF);
        Assert.Equal(127, (half >> 8) & 0xFF);
        Assert.Equal(127, half & 0xFF);

        Assert.True(ColorTable.IsFullyTransparent(ColorTable.WithOpacity(opaque, 0)));
    }
}
