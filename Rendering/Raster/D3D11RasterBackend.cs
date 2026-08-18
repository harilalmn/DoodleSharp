using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using C2VGeometry;
using C2VGeometry.Rendering;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Draws the scene on the GPU and hands WPF the result through a shared surface.
///
/// <para>
/// <b>Why this exists when a software rasterizer already works.</b> The managed backend and the WPF
/// one both pay a cost proportional to how much is on screen. This one does not: the geometry is
/// uploaded to a vertex buffer <i>once</i>, and panning or zooming rewrites a single 64-byte
/// constant buffer while the GPU re-transforms and re-clips everything. Navigation stops scaling
/// with the drawing at all. It is also the only path that survives 4K — the other two copy a
/// full-frame bitmap every frame, which is 8 MB at 1080p and 33 MB at 2160p, already over a 60 Hz
/// budget before anything is drawn.
/// </para>
///
/// <para>
/// <b>No <c>unsafe</c>.</b> Verified by spike before this was written: device creation, render
/// targets, <c>CreateBuffer</c> from a <see cref="ReadOnlySpan{T}"/>, the DXGI shared handle, the
/// D3D9Ex device opened onto it, and <c>D3DImage.SetBackBuffer</c> all work through safe managed
/// APIs. The project's <c>AllowUnsafeBlocks=false</c> policy is untouched.
/// </para>
///
/// <para>
/// <b>It is opt-in and it fails soft.</b> Device creation is tried once; hardware first, then WARP,
/// then the backend reports itself unavailable and the caller uses one of the CPU paths. CI runners
/// have no GPU, so this must never be the automatic choice and must never throw on a machine that
/// cannot support it.
/// </para>
/// </summary>
public sealed class D3D11RasterBackend : IDisposable
{
    // Position in world units, colour as a packed float4. World coordinates on purpose: the view
    // transform lives in the constant buffer, which is what makes a pan cost 64 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public float X, Y;
        public float R, G, B, A;
    }

    private const string ShaderSource = @"
cbuffer View : register(b0)
{
    float4x4 WorldToClip;
};

struct VSIn  { float2 pos : POSITION; float4 col : COLOR0; };
struct VSOut { float4 pos : SV_POSITION; float4 col : COLOR0; };

VSOut VS(VSIn v)
{
    VSOut o;
    o.pos = mul(float4(v.pos, 0.0f, 1.0f), WorldToClip);
    o.col = v.col;
    return o;
}

float4 PS(VSOut i) : SV_TARGET { return i.col; }
";

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _layout;
    private ID3D11Buffer? _constants;

    private ID3D11Texture2D? _target;
    private ID3D11RenderTargetView? _rtv;
    private ID3D11Buffer? _vertexBuffer;
    private int _vertexCapacity;
    private int _vertexCount;

    private Vortice.Direct3D9.IDirect3D9Ex? _d3d9;
    private Vortice.Direct3D9.IDirect3DDevice9Ex? _device9;
    private Vortice.Direct3D9.IDirect3DTexture9? _texture9;
    private Vortice.Direct3D9.IDirect3DSurface9? _surface9;

    private int _width, _height;
    private bool _initialised;

    /// <summary>The WPF-facing surface. Null until a frame has been rendered.</summary>
    public D3DImage? Output { get; private set; }

    /// <summary>False when no usable device could be created; the caller must fall back.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Why the device is unavailable, for the journal.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>Line segments in the current vertex buffer.</summary>
    public int SegmentCount => _vertexCount / 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Creates the device. Safe to call repeatedly; only the first attempt does work, and a failure
    /// is remembered rather than retried on every frame.
    /// </summary>
    public bool Initialise()
    {
        if (_initialised) return IsAvailable;
        _initialised = true;

        try
        {
            var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

            var hr = D3D11.D3D11CreateDevice(null, DriverType.Hardware,
                DeviceCreationFlags.BgraSupport, levels, out _device, out _context);

            if (hr.Failure)
            {
                // WARP is Microsoft's software rasterizer. Slower than hardware, but it keeps the
                // path exercisable on a machine with no usable GPU rather than silently untested.
                hr = D3D11.D3D11CreateDevice(null, DriverType.Warp,
                    DeviceCreationFlags.BgraSupport, levels, out _device, out _context);
            }

            if (hr.Failure || _device == null || _context == null)
            {
                UnavailableReason = "no Direct3D 11 device (" + hr.Description + ")";
                return false;
            }

            CompileShaders();

            _constants = _device.CreateBuffer(new BufferDescription(
                64, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

            _d3d9 = Vortice.Direct3D9.D3D9.Direct3DCreate9Ex();
            var pp = new Vortice.Direct3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                DeviceWindowHandle = GetDesktopWindow(),
                PresentationInterval = Vortice.Direct3D9.PresentInterval.Immediate,
                BackBufferFormat = Vortice.Direct3D9.Format.Unknown,
                BackBufferWidth = 1,
                BackBufferHeight = 1,
            };

            _device9 = _d3d9.CreateDeviceEx(0, Vortice.Direct3D9.DeviceType.Hardware,
                GetDesktopWindow(),
                Vortice.Direct3D9.CreateFlags.HardwareVertexProcessing |
                Vortice.Direct3D9.CreateFlags.Multithreaded |
                Vortice.Direct3D9.CreateFlags.FpuPreserve,
                pp);

            IsAvailable = true;
            return true;
        }
        catch (Exception ex)
        {
            // A missing runtime, a headless session, a driver that refuses -- all of them mean
            // "use the CPU path", none of them mean "take the application down".
            UnavailableReason = ex.GetType().Name + ": " + ex.Message;
            IsAvailable = false;
            return false;
        }
    }

    private void CompileShaders()
    {
        var vsBlob = Compiler.Compile(ShaderSource, "VS", "logo.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(ShaderSource, "PS", "logo.hlsl", "ps_4_0");

        _vs = _device!.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);

        _layout = _device.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0),
        }, vsBlob.Span);
    }

    /// <summary>
    /// Uploads the scene. Call only when the geometry actually changes — the whole point of this
    /// backend is that navigating does not.
    /// </summary>
    /// <summary>
    /// Shapes the tessellator or this sink declined during the last <see cref="UploadScene"/> call,
    /// so the caller can hand them to the vector layer. Persists between uploads, like the vertex
    /// buffer, because the upload is only redone when the scene version moves.
    /// </summary>
    public IReadOnlyCollection<Shape> DeclinedShapes => _declined;

    private readonly HashSet<Shape> _declined = new();

    public void UploadScene(IReadOnlyList<Shape> shapes, ShapeTessellator tessellator)
    {
        // Populated by the upload and read every frame, so it persists between uploads exactly as
        // the vertex buffer does.

        if (!IsAvailable || _device == null) return;

        var vertices = new List<Vertex>(Math.Max(1024, shapes.Count * 4));

        _declined.Clear();

        var sink = new GpuSink(vertices);
        for (int i = 0; i < shapes.Count; i++)
        {
            // The return value is not optional (note 81). Shapes the tessellator declines —
            // dimensions, arrows, grids, infinite construction lines — and shapes this sink itself
            // refuses, such as text, must be handed back for the vector layer to draw. Discarding it
            // made them silently vanish on the GPU backend alone, which is the same defect
            // ManagedRasterBackend was fixed for and the reason it collects them into Deferred.
            if (!tessellator.Tessellate(shapes[i], sink))
                _declined.Add(shapes[i]);
        }

        _vertexCount = vertices.Count;
        if (_vertexCount == 0) return;

        if (_vertexBuffer == null || _vertexCapacity < _vertexCount)
        {
            _vertexBuffer?.Dispose();
            _vertexCapacity = Math.Max(_vertexCount, _vertexCapacity * 2);
            _vertexBuffer = _device.CreateBuffer(
                new BufferDescription(
                    (uint)(_vertexCapacity * Marshal.SizeOf<Vertex>()),
                    BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        var mapped = _context!.Map(_vertexBuffer!, 0, MapMode.WriteDiscard);
        try
        {
            var span = vertices.ToArray().AsSpan();
            MemoryMarshal.AsBytes(span).CopyTo(mapped.AsSpan<byte>(
                _vertexCapacity * Marshal.SizeOf<Vertex>()));
        }
        finally
        {
            _context.Unmap(_vertexBuffer!, 0);
        }
    }

    /// <summary>
    /// Draws the uploaded scene under the given view. This is the per-frame cost, and it is
    /// dominated by a 64-byte constant-buffer write regardless of how much geometry there is.
    /// </summary>
    public bool Render(int width, int height, Color4 background,
                       double scale, double panX, double panY)
    {
        if (!IsAvailable || _device == null || _context == null) return false;

        try
        {
            if (!EnsureTarget(width, height)) return false;

            _context.OMSetRenderTargets(_rtv!);
            _context.RSSetViewport(0, 0, width, height);
            _context.ClearRenderTargetView(_rtv!, background);

            if (_vertexCount > 0 && _vertexBuffer != null)
            {
                WriteViewMatrix(width, height, scale, panX, panY);

                _context.IASetInputLayout(_layout!);
                _context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
                _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<Vertex>());
                _context.VSSetShader(_vs!);
                _context.VSSetConstantBuffer(0, _constants!);
                _context.PSSetShader(_ps!);
                _context.Draw((uint)_vertexCount, 0);
            }

            _context.Flush();

            if (Output != null && _surface9 != null)
            {
                Output.Lock();
                Output.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface9.NativePointer);
                Output.AddDirtyRect(new Int32Rect(0, 0, width, height));
                Output.Unlock();
            }

            return true;
        }
        catch (Exception ex)
        {
            // A device reset -- driver update, sleep/resume, a GPU hang -- surfaces here. Mark the
            // backend unavailable so the caller falls back for the rest of the session rather than
            // throwing once a frame.
            UnavailableReason = "device lost: " + ex.Message;
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// World-to-clip. Mirrors <c>ViewportTransform</c> exactly, including the Y flip: world is Y-up
    /// with the origin at the viewport centre, clip space is Y-up with the origin in the middle and
    /// a range of -1..1.
    /// </summary>
    private void WriteViewMatrix(int width, int height, double scale, double panX, double panY)
    {
        // screenX = w/2 + worldX*scale + panX     ->  clipX = (screenX / w) * 2 - 1
        // screenY = h/2 - worldY*scale + panY     ->  clipY = 1 - (screenY / h) * 2
        var sx = (float)(2.0 * scale / width);
        var sy = (float)(2.0 * scale / height);
        var tx = (float)(2.0 * panX / width);
        var ty = (float)(-2.0 * panY / height);

        var m = new Matrix4x4(
            sx, 0, 0, 0,
            0, sy, 0, 0,
            0, 0, 1, 0,
            tx, ty, 0, 1);

        var mapped = _context!.Map(_constants!, 0, MapMode.WriteDiscard);
        try
        {
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref m, 1))
                .CopyTo(mapped.AsSpan<byte>(64));
        }
        finally
        {
            _context.Unmap(_constants!, 0);
        }
    }

    private bool EnsureTarget(int width, int height)
    {
        if (_target != null && _width == width && _height == height) return true;
        if (width <= 0 || height <= 0) return false;

        _rtv?.Dispose(); _target?.Dispose();
        _surface9?.Dispose(); _texture9?.Dispose();

        _width = width; _height = height;

        _target = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.Shared,
        });

        _rtv = _device.CreateRenderTargetView(_target);

        using var dxgi = _target.QueryInterface<IDXGIResource>();
        var shared = dxgi.SharedHandle;

        _texture9 = _device9!.CreateTexture((uint)width, (uint)height, 1,
            Vortice.Direct3D9.Usage.RenderTarget,
            Vortice.Direct3D9.Format.A8R8G8B8,
            Vortice.Direct3D9.Pool.Default,
            ref shared);

        _surface9 = _texture9.GetSurfaceLevel(0);
        Output ??= new D3DImage();
        return true;
    }

    public void Dispose()
    {
        _surface9?.Dispose(); _texture9?.Dispose();
        _device9?.Dispose(); _d3d9?.Dispose();
        _vertexBuffer?.Dispose(); _constants?.Dispose();
        _layout?.Dispose(); _ps?.Dispose(); _vs?.Dispose();
        _rtv?.Dispose(); _target?.Dispose();
        _context?.Dispose(); _device?.Dispose();
    }

    /// <summary>
    /// Collects world-space line segments for the vertex buffer. Fills and text are not drawn by
    /// this backend; it exists for the case that defeats the others, which is hairline geometry in
    /// bulk, and everything else stays on the vector layer above.
    /// </summary>
    private sealed class GpuSink : IPrimitiveSink
    {
        private readonly List<Vertex> _out;
        private float _r, _g, _b, _a;

        public GpuSink(List<Vertex> output) { _out = output; }

        public TessellationHints Hints { get; } = new() { Scale = 1.0 };

        public bool BeginShape(Shape shape, in PenSpec pen)
        {
            if (shape is VText) return false;

            var packed = ColorTable.Resolve(pen.Color);
            var a = ((packed >> 24) & 0xFF) / 255f;
            // ColorTable premultiplies; undo it so the shader gets straight colour.
            _a = a * (float)Math.Clamp(pen.Opacity, 0, 1);
            _r = a > 0 ? ((packed >> 16) & 0xFF) / 255f / a : 0;
            _g = a > 0 ? ((packed >> 8) & 0xFF) / 255f / a : 0;
            _b = a > 0 ? (packed & 0xFF) / 255f / a : 0;
            return true;
        }

        public void EndShape() { }

        public void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed)
        {
            if (points == null || points.Count < 2) return;
            var last = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < last; i++)
            {
                Add(points[i]);
                Add(points[(i + 1) % points.Count]);
            }
        }

        // A filled area still gets its outline from the EmitPolyline that follows it; solid fills
        // need a triangulator, which is a different piece of work.
        public void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, C2VGeometry.Rendering.FillRule rule) { }

        public void EmitPoint(VXYZ point)
        {
            // A line list cannot express a point; a degenerate segment is one pixel, which is what
            // a point marker is anyway.
            Add(point);
            Add(new VXYZ(point.X, point.Y));
        }

        public void EmitText(VText text) { }

        private void Add(VXYZ p) =>
            _out.Add(new Vertex { X = (float)p.X, Y = (float)p.Y, R = _r, G = _g, B = _b, A = _a });
    }
}
