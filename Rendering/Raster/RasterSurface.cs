using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// The pixel buffer the scene is drawn into, and the bridge onto a WPF <see cref="WriteableBitmap"/>.
///
/// <para>
/// <b>No <c>unsafe</c>, deliberately.</b> <c>WriteableBitmap.BackBuffer</c> is an
/// <see cref="IntPtr"/> and <see cref="Marshal.Copy(int[], int, IntPtr, int)"/> is an ordinary safe
/// overload, so the whole raster path stays inside the project's twice-documented
/// <c>AllowUnsafeBlocks=false</c> policy. Keeping the buffer managed also means the rasterizer is
/// testable as a pure function — an array in, an array out — on a CI runner with no GPU.
/// </para>
///
/// <para>
/// <b>Not a <c>HwndHost</c> or GL control.</b> A <c>WriteableBitmap</c> is ordinary WPF content, so
/// the semi-transparent animation-controls panel that sits over the canvas keeps compositing
/// correctly, and <c>RenderTargetBitmap</c> capture for PNG/GIF/MP4 export still works. An
/// HWND-based surface would render above all WPF content and hide that overlay.
/// </para>
/// </summary>
public sealed class RasterSurface
{
    private int[] _pixels = Array.Empty<int>();
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;

    /// <summary>
    /// Rows per tile. Tiles are horizontal bands so each thread owns a contiguous, non-overlapping
    /// run of the buffer — no locking, no false sharing beyond the band edges, and the same
    /// rasterizer code with a different clip range.
    /// </summary>
    internal const int TileRows = 64;

    /// <summary>Below this, threading costs more than it saves.</summary>
    private const int MinRowsToParallelise = 256;

    public int Width => _width;
    public int Height => _height;
    public int[] Pixels => _pixels;
    public WriteableBitmap? Bitmap => _bitmap;

    /// <summary>Resizes if needed. Returns false if the size is unusable.</summary>
    public bool Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        if (width == _width && height == _height && _bitmap != null) return true;

        _width = width;
        _height = height;
        _pixels = new int[width * height];
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        return true;
    }

    /// <summary>Fills the whole buffer with one packed colour.</summary>
    public void Clear(int packedBgra)
    {
        if (packedBgra == 0) Array.Clear(_pixels);
        else _pixels.AsSpan().Fill(packedBgra);
    }

    /// <summary>
    /// Runs <paramref name="drawBand"/> once per horizontal band, in parallel. The callback receives
    /// the inclusive row range it owns and must not write outside it — which is exactly the clip
    /// range the rasterizer primitives already take.
    ///
    /// <para>
    /// Bands replay an already-tessellated command buffer, so the redundancy is a bounds check per
    /// command rather than re-tessellating the scene — which is what made the first version of this
    /// slower than the renderer it replaces.
    /// </para>
    /// </summary>
    public void RenderTiled(Action<int, int> drawBand)
    {
        if (_height <= 0) return;

        if (_height < MinRowsToParallelise)
        {
            drawBand(0, _height - 1);
            return;
        }

        var tiles = (_height + TileRows - 1) / TileRows;
        Parallel.For(0, tiles, tile =>
        {
            var top = tile * TileRows;
            var bottom = Math.Min(top + TileRows - 1, _height - 1);
            drawBand(top, bottom);
        });
    }

    /// <summary>
    /// Copies the buffer into the bitmap. One <see cref="Marshal.Copy"/> of the whole frame, which
    /// is the cost that ultimately caps this backend: about 8 MB at 1080p, and four times that at
    /// 4K, on the UI thread. Getting past <i>that</i> needs the GPU backend, not a faster rasterizer.
    /// </summary>
    public void Present()
    {
        if (_bitmap == null || _pixels.Length == 0) return;

        _bitmap.Lock();
        try
        {
            Marshal.Copy(_pixels, 0, _bitmap.BackBuffer, _pixels.Length);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }
}
