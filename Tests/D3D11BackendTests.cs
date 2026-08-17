using System;
using System.Collections.Generic;
using Xunit;
using C2VGeometry;
using DoodleSharp.Rendering.Raster;

namespace DoodleSharp.Tests;

/// <summary>
/// The GPU backend, exercised as far as the machine allows.
///
/// <para>
/// These tests must pass on a runner with no GPU, because that is what builds every release. So
/// they assert the two things that matter regardless of hardware — that the backend either works or
/// reports itself unavailable, and that it never throws — and only assert on rendering when a
/// device was actually created. A test that required a GPU would simply be disabled in CI, which is
/// the same as not having it.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class D3D11BackendTests : IDisposable
{
    public D3D11BackendTests() => Shape.DefaultRegistry = null;
    public void Dispose() => Shape.DefaultRegistry = null;

    [Fact]
    public void InitialiseEitherSucceedsOrExplainsItself()
    {
        using var backend = new D3D11RasterBackend();
        var ok = backend.Initialise();

        Assert.Equal(ok, backend.IsAvailable);

        if (!ok)
        {
            // A failure must say why. "It didn't work" in a user's journal is not actionable.
            Assert.False(string.IsNullOrWhiteSpace(backend.UnavailableReason),
                "An unavailable backend must record a reason for the journal.");
        }
    }

    [Fact]
    public void InitialiseIsIdempotent()
    {
        using var backend = new D3D11RasterBackend();
        var first = backend.Initialise();
        var second = backend.Initialise();

        // Called once a frame by the render path; a second attempt must not rebuild the device,
        // and must not flip its answer.
        Assert.Equal(first, second);
    }

    [Fact]
    public void RenderingWithoutADeviceIsSafeAndReportsFailure()
    {
        using var backend = new D3D11RasterBackend();
        // Deliberately not initialised.
        var drew = backend.Render(800, 600, new Vortice.Mathematics.Color4(0, 0, 0, 1), 1.0, 0, 0);
        Assert.False(drew);
    }

    [Fact]
    public void UploadAndRenderProducesASurface()
    {
        using var backend = new D3D11RasterBackend();
        if (!backend.Initialise())
        {
            // No GPU on this machine — the fallback path is what is under test elsewhere.
            Assert.False(backend.IsAvailable);
            return;
        }

        var shapes = new List<Shape>();
        for (int i = 0; i < 500; i++)
            shapes.Add(new VLine(new VXYZ(i, 0), new VXYZ(i, 100)) { Color = "Cyan" });

        backend.UploadScene(shapes, new C2VGeometry.Rendering.ShapeTessellator());
        Assert.Equal(500, backend.SegmentCount);

        var drew = backend.Render(640, 480, new Vortice.Mathematics.Color4(0.1f, 0.1f, 0.1f, 1f),
                                  scale: 2.0, panX: 0, panY: 0);

        Assert.True(drew, "Render failed on a machine that reported a working device: "
                          + backend.UnavailableReason);
        Assert.NotNull(backend.Output);
    }

    [Fact]
    public void NavigationDoesNotReuploadGeometry()
    {
        using var backend = new D3D11RasterBackend();
        if (!backend.Initialise()) return;

        var shapes = new List<Shape> { new VLine(new VXYZ(0, 0), new VXYZ(10, 10)) };
        backend.UploadScene(shapes, new C2VGeometry.Rendering.ShapeTessellator());
        var uploaded = backend.SegmentCount;

        // The whole premise of this backend: panning and zooming rewrite a 64-byte constant buffer
        // and leave the vertex buffer alone. If a frame re-uploaded, the segment count would be
        // rebuilt from scratch each time and this property would be meaningless.
        for (int i = 0; i < 20; i++)
            backend.Render(400, 300, new Vortice.Mathematics.Color4(0, 0, 0, 1), 1.0 + i, i * 3, i * 2);

        Assert.Equal(uploaded, backend.SegmentCount);
    }

    [Fact]
    public void TextIsNotDrawnByTheGpuPath()
    {
        using var backend = new D3D11RasterBackend();
        if (!backend.Initialise()) return;

        backend.UploadScene(
            new List<Shape> { new VText(new VXYZ(0, 0), "hello") },
            new C2VGeometry.Rendering.ShapeTessellator());

        // Text stays on the vector layer above; the GPU path is for hairline geometry in bulk.
        Assert.Equal(0, backend.SegmentCount);
    }
}
