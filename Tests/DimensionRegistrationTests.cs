using System.Linq;
using DoodleSharp.Canvas;
using C2VGeometry;

namespace DoodleSharp.Tests;

// VDimension endpoints and the helper points returned by GetDimensionGeometry are
// VXYZ value coordinates in C2VGeometry, so they can never auto-register with the
// canvas. These tests assert that constructing a dimension only ever places the
// dimension itself — no phantom endpoint shapes leak onto the canvas.
[Collection("CanvasState")]
public class DimensionRegistrationTests : System.IDisposable
{
    public DimensionRegistrationTests()
    {
        // Bind auto-registration to the canvas (other tests may have nulled it).
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        Shape.AutoRegister = true;
        CanvasRenderer.Instance.Clear();
    }

    public void Dispose()
    {
        CanvasRenderer.Instance.Clear();
        Shape.DefaultRegistry = CanvasRenderer.Instance;
    }

    [Fact]
    public void DoubleConstructor_OnlyPlacesTheDimension()
    {
        var dim = new VDimension(0, 0, 10, 0);

        var shapes = CanvasRenderer.Instance.GetShapes();
        Assert.Single(shapes);
        Assert.Same(dim, shapes[0]);
    }

    [Fact]
    public void GetDimensionGeometry_DoesNotPlaceHelperPoints()
    {
        var dim = new VDimension(0, 0, 10, 0);

        var geom = dim.GetDimensionGeometry();

        // The helper geometry are plain VXYZ values, not Shapes; only the dimension is on the canvas.
        Assert.Single(CanvasRenderer.Instance.GetShapes());
        Assert.NotNull(geom.dimStart);
        Assert.NotNull(geom.dimEnd);
    }
}
