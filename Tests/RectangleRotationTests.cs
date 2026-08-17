using System.Linq;
using C2VGeometry;
using DoodleSharp.Animation;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="VRectangle.RotationAngle"/> used to <c>new</c>-shadow <see cref="Shape.RotationAngle"/>,
/// so which property a piece of code touched depended on the static type of the variable holding the
/// rectangle. The renderer holds a <c>VRectangle</c> and read the intrinsic angle; <c>RotateAnimation</c>
/// holds a <c>Shape</c> and wrote the animation one. They never met.
/// </summary>
[Collection("CanvasState")]
public class RectangleRotationTests
{
    [Fact]
    public void SettingThroughABaseReferenceRotatesTheGeometry()
    {
        var rectangle = new VRectangle(new VXYZ(0, 0), 10, 10);
        Shape asShape = rectangle;

        asShape.RotationAngle = 45;

        // One property, so the derived view agrees...
        Assert.Equal(45, rectangle.RotationAngle);
        // ...and the corners really moved.
        Assert.NotEqual(0, rectangle.Vertices[0].X, 6);
    }

    [Fact]
    public void SettingThroughTheDerivedReferenceIsVisibleFromTheBase()
    {
        var rectangle = new VRectangle(new VXYZ(0, 0), 10, 10);

        rectangle.RotationAngle = 90;

        Assert.Equal(90, ((Shape)rectangle).RotationAngle);
    }

    [Fact]
    public void RotationIsAboutTheRectanglesCentre()
    {
        var rectangle = new VRectangle(new VXYZ(0, 0), 10, 10) { RotationAngle = 90 };

        // The centre is invariant under its own rotation.
        var bounds = rectangle.GetBounds();
        Assert.Equal(5, bounds.Center.X, 6);
        Assert.Equal(5, bounds.Center.Y, 6);
    }

    [Fact]
    public void RotateAnimationActuallyRotatesARectangle()
    {
        // The user-visible consequence of the shadowing: this did nothing at all.
        var rectangle = new VRectangle(new VXYZ(0, 0), 10, 10);
        var before = rectangle.Vertices[0];

        var animation = new RotateAnimation(rectangle, rectangle.GetBounds().Center, 90, 1.0);
        animation.Apply(0.0);   // capture the initial state
        animation.Apply(1.0);   // full rotation

        Assert.NotEqual(0.0, rectangle.RotationAngle);
        Assert.True(before.DistanceTo(rectangle.Vertices[0]) > 1.0,
            "the rectangle's corners should have moved");
    }

    [Fact]
    public void ZeroRotationLeavesAnAxisAlignedRectangle()
    {
        var rectangle = new VRectangle(new VXYZ(0, 0), 10, 4);

        Assert.Equal(0, rectangle.RotationAngle);
        Assert.Equal(new[] { 0.0, 10.0, 10.0, 0.0 }, rectangle.Vertices.Select(v => v.X));
        Assert.Equal(new[] { 0.0, 0.0, 4.0, 4.0 }, rectangle.Vertices.Select(v => v.Y));
    }
}
