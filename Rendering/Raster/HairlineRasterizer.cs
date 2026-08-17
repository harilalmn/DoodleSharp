using System;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Draws one-pixel lines into a managed pixel buffer: Cohen–Sutherland clipping, then a DDA walk.
///
/// <para>
/// <b>Why this is hand-written rather than delegated to a library.</b> The requirement is
/// AutoCAD-crisp — one device pixel, no anti-aliasing, opaque over an opaque background. That is
/// the one case where a general rasterizer's machinery is all overhead: no coverage accumulation,
/// no blend-mode resolution, no colour management, no blitter dispatch. The inner loop is an add
/// and a store. Anti-aliasing is where rasterizers get genuinely hard, and this requirement
/// removes it.
/// </para>
///
/// <para>
/// It writes to an <see cref="int"/> array, not a pointer, so the project's deliberate
/// <c>AllowUnsafeBlocks=false</c> policy is untouched, and the output is trivially testable: a
/// small buffer in, an exact set of lit pixels out, with no GPU and no WPF anywhere near it.
/// </para>
/// </summary>
public static class HairlineRasterizer
{
    // Cohen–Sutherland region codes.
    private const int Inside = 0;
    private const int Left = 1;
    private const int Right = 2;
    private const int Bottom = 4;
    private const int Top = 8;

    /// <summary>
    /// Draws a clipped one-pixel line into <paramref name="pixels"/>.
    /// </summary>
    /// <param name="pixels">Row-major buffer of premultiplied BGRA, <paramref name="stride"/> wide.</param>
    /// <param name="clipTop">First row this call may write (inclusive) — the tile's band.</param>
    /// <param name="clipBottom">Last row this call may write (inclusive).</param>
    public static void DrawLine(int[] pixels, int stride, int height,
                                double x0, double y0, double x1, double y1,
                                int color, int clipTop, int clipBottom)
    {
        if (clipTop < 0) clipTop = 0;
        if (clipBottom > height - 1) clipBottom = height - 1;
        if (clipTop > clipBottom) return;

        // Reject non-finite input before clipping: an infinity or NaN would survive the
        // Cohen-Sutherland loop as a region code of Inside and then index the buffer with garbage.
        if (!double.IsFinite(x0) || !double.IsFinite(y0) ||
            !double.IsFinite(x1) || !double.IsFinite(y1)) return;

        if (!ClipToRect(ref x0, ref y0, ref x1, ref y1,
                        0, clipTop, stride - 1, clipBottom)) return;

        var ix0 = (int)Math.Round(x0);
        var iy0 = (int)Math.Round(y0);
        var ix1 = (int)Math.Round(x1);
        var iy1 = (int)Math.Round(y1);

        // Rounding can push a coordinate one past the clip rect it was just fitted into.
        ix0 = Math.Clamp(ix0, 0, stride - 1);
        ix1 = Math.Clamp(ix1, 0, stride - 1);
        iy0 = Math.Clamp(iy0, clipTop, clipBottom);
        iy1 = Math.Clamp(iy1, clipTop, clipBottom);

        var dx = Math.Abs(ix1 - ix0);
        var dy = -Math.Abs(iy1 - iy0);
        var sx = ix0 < ix1 ? 1 : -1;
        var sy = iy0 < iy1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            pixels[iy0 * stride + ix0] = color;

            if (ix0 == ix1 && iy0 == iy1) break;

            var e2 = err << 1;
            if (e2 >= dy) { err += dy; ix0 += sx; }
            if (e2 <= dx) { err += dx; iy0 += sy; }
        }
    }

    /// <summary>Sets a single pixel, bounds-checked. Used for level-of-detail marks.</summary>
    public static void DrawPoint(int[] pixels, int stride, int height,
                                 double x, double y, int color, int clipTop, int clipBottom)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);

        if (ix < 0 || ix >= stride) return;
        if (iy < clipTop || iy > clipBottom || iy < 0 || iy >= height) return;

        pixels[iy * stride + ix] = color;
    }

    /// <summary>
    /// Cohen–Sutherland. Clipping before the DDA rather than testing each pixel is what makes a
    /// line stretching far outside the viewport cost its visible length rather than its true one —
    /// which matters enormously at high zoom, where a line's endpoints can be millions of pixels
    /// off-screen.
    /// </summary>
    private static bool ClipToRect(ref double x0, ref double y0, ref double x1, ref double y1,
                                   double minX, double minY, double maxX, double maxY)
    {
        var code0 = RegionCode(x0, y0, minX, minY, maxX, maxY);
        var code1 = RegionCode(x1, y1, minX, minY, maxX, maxY);

        // Bounded rather than while(true): with adversarial coordinates the classic loop can fail
        // to converge through floating-point rounding, and an infinite loop inside a render pass
        // hangs the UI thread with no diagnostic.
        for (int guard = 0; guard < 8; guard++)
        {
            if ((code0 | code1) == Inside) return true;   // both in
            if ((code0 & code1) != 0) return false;       // both out, same side

            var outCode = code0 != Inside ? code0 : code1;
            double x, y;

            if ((outCode & Top) != 0)
            {
                x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0);
                y = maxY;
            }
            else if ((outCode & Bottom) != 0)
            {
                x = x0 + (x1 - x0) * (minY - y0) / (y1 - y0);
                y = minY;
            }
            else if ((outCode & Right) != 0)
            {
                y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0);
                x = maxX;
            }
            else
            {
                y = y0 + (y1 - y0) * (minX - x0) / (x1 - x0);
                x = minX;
            }

            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;

            if (outCode == code0)
            {
                x0 = x; y0 = y;
                code0 = RegionCode(x0, y0, minX, minY, maxX, maxY);
            }
            else
            {
                x1 = x; y1 = y;
                code1 = RegionCode(x1, y1, minX, minY, maxX, maxY);
            }
        }

        return (code0 | code1) == Inside;
    }

    private static int RegionCode(double x, double y,
                                  double minX, double minY, double maxX, double maxY)
    {
        var code = Inside;
        if (x < minX) code |= Left;
        else if (x > maxX) code |= Right;
        if (y < minY) code |= Bottom;
        else if (y > maxY) code |= Top;
        return code;
    }
}
