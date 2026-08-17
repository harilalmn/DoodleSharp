using System.Linq;
using C2VGeometry;
using DoodleSharp.Animation;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

[Collection("CanvasState")]
public class TransformAnimationTests
{
    public TransformAnimationTests()
    {
        // Reset shared canvas state — these tests mutate the singleton registry.
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        CanvasRenderer.Instance.Clear();
    }

    [Fact]
    public void Transform_HidesDestinationUntilComplete()
    {
        var from = new VLine(-50, 0, 50, 0);
        var to = new VCircle(0, 0, 40);

        // Destination is hidden as soon as the animation is created.
        var anim = new TransformAnimation(from, to, 1.0);
        Assert.False(to.IsVisible);

        // Before the transform's turn: only the source is shown.
        anim.Apply(-0.5);
        Assert.True(from.IsVisible);
        Assert.False(to.IsVisible);

        // While running: both inputs hidden, a morph proxy is shown.
        anim.Apply(0.5);
        Assert.False(from.IsVisible);
        Assert.False(to.IsVisible);

        // Completed: the real destination is revealed, the source stays hidden.
        anim.Apply(1.0);
        Assert.False(from.IsVisible);
        Assert.True(to.IsVisible);
    }

    [Fact]
    public void Transform_RegistersAMorphProxyOnTheCanvas()
    {
        var from = new VLine(-50, 0, 50, 0);
        var to = new VCircle(0, 0, 40);

        int before = CanvasRenderer.Instance.GetShapes().Count;
        var anim = new TransformAnimation(from, to, 1.0);
        int after = CanvasRenderer.Instance.GetShapes().Count;

        // from + to are already registered; the morph proxy adds exactly one more.
        Assert.Equal(before + 1, after);

        // The proxy carries a non-empty Name so HideUnnamedShapes won't strip it.
        var proxy = CanvasRenderer.Instance.GetShapes()
            .OfType<VPolyline>()
            .Single(p => p.Name.StartsWith("__transform_morph_"));
        Assert.NotEmpty(proxy.Name);
    }

    [Fact]
    public void Transform_MorphProxyInterpolatesBetweenOutlines()
    {
        var from = new VLine(-50, 0, 50, 0);
        var to = new VCircle(0, 0, 40);
        var anim = new TransformAnimation(from, to, 1.0);

        var proxy = CanvasRenderer.Instance.GetShapes()
            .OfType<VPolyline>()
            .Single(p => p.Name.StartsWith("__transform_morph_"));

        // At t≈0 the proxy matches the source line's endpoints.
        anim.Apply(0.0001);
        Assert.Equal(-50, proxy.Points.First().X, precision: 1);
        Assert.Equal(50, proxy.Points.Last().X, precision: 1);

        // Midway the proxy lies strictly between the line and the circle:
        // its bounding height grows away from the line's flat y=0.
        anim.Apply(0.5);
        double midHeight = proxy.Points.Max(p => p.Y) - proxy.Points.Min(p => p.Y);
        Assert.True(midHeight > 1.0, $"expected the half-morph to bow off the axis, got height {midHeight}");
    }

    [Fact]
    public void Transform_NonCurveShapeFallsBackToBoundingBox()
    {
        var from = new VText(new VXYZ(0, 0), "hello");
        var to = new VCircle(0, 0, 40);

        // Should not throw for a non-ICurve source; the bbox fallback handles it.
        var anim = new TransformAnimation(from, to, 1.0);
        anim.Apply(0.5);

        var proxy = CanvasRenderer.Instance.GetShapes()
            .OfType<VPolyline>()
            .Single(p => p.Name.StartsWith("__transform_morph_"));
        Assert.True(proxy.IsVisible);
    }
}
