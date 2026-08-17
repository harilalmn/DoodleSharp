using System.Linq;
using C2VGeometry;
using DoodleSharp.Animation;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

[Collection("CanvasState")]
public class AutoRegisterAnimationTests
{
    public AutoRegisterAnimationTests()
    {
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        CanvasRenderer.Instance.Clear();
        Shape.AutoRegister = true;
    }

    private static int CanvasCount() => CanvasRenderer.Instance.GetShapes().Count;
    private static bool OnCanvas(Shape s) => CanvasRenderer.Instance.GetShapes().Contains(s);

    [Fact]
    public void DrawAnimation_RegistersTarget_WhenAutoRegisterOff()
    {
        Shape.AutoRegister = false;
        try
        {
            var c = new VCircle(0, 0, 50);
            Assert.Equal(0, CanvasCount()); // not auto-registered

            var anim = new Animator();
            anim.AddToAnimations(new DrawAnimation(c, 1.0)); // Timeline auto-draws target
            Assert.True(OnCanvas(c));
        }
        finally { Shape.AutoRegister = true; }
    }

    [Fact]
    public void TransformAnimation_RegistersProxyAndBothShapes_WhenAutoRegisterOff()
    {
        Shape.AutoRegister = false;
        try
        {
            var from = new VLine(-50, 0, 50, 0);
            var to = new VCircle(0, 0, 40);
            Assert.Equal(0, CanvasCount()); // nothing auto-registered

            _ = new TransformAnimation(from, to, 1.0);

            // The morph proxy AND both input shapes must be on the canvas, otherwise
            // nothing renders when "Auto-Draw Shapes" is disabled.
            var proxy = CanvasRenderer.Instance.GetShapes()
                .OfType<VPolyline>()
                .FirstOrDefault(p => p.Name.StartsWith("__transform_morph_"));
            Assert.NotNull(proxy);
            Assert.True(OnCanvas(from));
            Assert.True(OnCanvas(to));
        }
        finally { Shape.AutoRegister = true; }
    }

    [Fact]
    public void ChainedTransforms_ShowOnlyOneShapeAtEachStage()
    {
        // Mirrors the Flocks sketch: circle -> ellipse, then ellipse -> line.
        var circle = new VCircle(0, 0, 5);
        var ellipse = new VEllipse(VXYZ.Zero, 15, 8);
        var line = new VLine(new VXYZ(-10, 0), new VXYZ(10, 0));

        var animator = new Animator();
        var a1 = new TransformAnimation(circle, ellipse, 2);
        var a2 = new TransformAnimation(ellipse, line, 2);
        animator.AddToAnimations(a1);   // t in [0,1] over 0..2s
        animator.AddToAnimations(a2);   // t in [0,1] over 2..4s

        // Helper to run one frame the way the Timeline does (a1 then a2).
        void Frame(double a1t, double a2t) { a1.Apply(a1t); a2.Apply(a2t); }

        // Initial: only the circle is visible.
        Frame(-1, -1);
        Assert.True(circle.IsVisible);
        Assert.False(ellipse.IsVisible);
        Assert.False(line.IsVisible);

        // Midway through the first morph: ellipse must NOT pop in statically.
        Frame(0.5, -0.75);
        Assert.False(circle.IsVisible);
        Assert.False(ellipse.IsVisible);
        Assert.False(line.IsVisible);

        // Midway through the second morph.
        Frame(1.0, 0.5);
        Assert.False(circle.IsVisible);
        Assert.False(ellipse.IsVisible);
        Assert.False(line.IsVisible);

        // Completed: only the final line is visible.
        Frame(1.0, 1.0);
        Assert.False(circle.IsVisible);
        Assert.False(ellipse.IsVisible);
        Assert.True(line.IsVisible);
    }
}
