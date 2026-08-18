using System.Collections.Generic;
using System.Linq;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// <c>VPolygon.Slice</c> — cutting a polygon along an infinite line must be <b>area-preserving</b>.
///
/// <para>
/// The original hand-rolled implementation walked the perimeter, paired intersections in perimeter
/// order, and closed each piece with a single chord — which assumes every output piece is one
/// boundary arc plus one chord. That is only true of a convex cut with exactly two crossings. A
/// concave polygon whose notch straddles the line is crossed four times: the walker emitted the arcs
/// between intersections 0-1 and 2-3, silently dropped arcs 1-2 and 3-0, and could not represent the
/// remaining piece at all (it is bounded by <i>two</i> arcs). The reported parcel lost 94% of its
/// area — two thin slivers came back where three real pieces were expected.
/// </para>
///
/// <para>
/// The area-sum assertions are the point of this file. A slice that returns plausible-looking
/// polygons which do not add back up to the original is exactly the failure that shipped.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class PolygonSliceTests
{
    private const int Precision = 6;

    private static double TotalArea(IEnumerable<VPolygon> pieces) => pieces.Sum(p => p.Area);

    private static VPolygon Square(double x, double y, double size) => new(
        new VXYZ(x, y),
        new VXYZ(x + size, y),
        new VXYZ(x + size, y + size),
        new VXYZ(x, y + size));

    /// <summary>
    /// A polygon with a deep notch cut into its top edge. A horizontal line drawn across the notch
    /// crosses the boundary four times, which is the configuration the old walker got wrong.
    ///
    /// <code>
    ///   (0,100)                        (100,100)
    ///      +------+          +------------+
    ///      |      |  notch   |            |
    ///      |      +----------+  (60,40)   |     &lt;- notch floor at y = 40
    ///      |     (40,40)                  |
    ///      +------------------------------+
    ///   (0,0)                          (100,0)
    /// </code>
    /// </summary>
    private static VPolygon NotchedPolygon() => new(
        new VXYZ(0, 0),
        new VXYZ(100, 0),
        new VXYZ(100, 100),
        new VXYZ(60, 100),
        new VXYZ(60, 40),
        new VXYZ(40, 40),
        new VXYZ(40, 100),
        new VXYZ(0, 100));

    [Fact]
    public void SlicingAcrossAConcaveNotchPreservesTheWholeArea()
    {
        // The regression. The cut at y = 70 passes through the notch, so it crosses the boundary
        // four times and the correct answer is three pieces:
        //   left tower above the cut   (0..40)  x (70..100) = 1200
        //   right tower above the cut  (60..100) x (70..100) = 1200
        //   everything below the cut                          = 10000 - 1200 - 2400 = 6400
        var polygon = NotchedPolygon();
        double originalArea = polygon.Area;

        var pieces = polygon.Slice(new VXYZ(-50, 70), new VXYZ(150, 70));

        Assert.Equal(3, pieces.Count);
        Assert.Equal(originalArea, TotalArea(pieces), Precision);

        var areas = pieces.Select(p => p.Area).OrderBy(a => a).ToList();
        Assert.Equal(1200, areas[0], Precision);
        Assert.Equal(1200, areas[1], Precision);
        Assert.Equal(6400, areas[2], Precision);
    }

    /// <summary>
    /// The parcel from the original report, verbatim. The slice line is near-horizontal and passes
    /// through the notch between the (-286.67, 139.43) spike and the (-3.67, 181.43) shoulder, so it
    /// crosses the boundary four times. The old walker returned only the two slivers above the line —
    /// 2542.55 and 10402.77 against a parcel of 225561.5, losing 94% of the area.
    /// </summary>
    [Fact]
    public void TheReportedParcelSlicesWithoutLosingArea()
    {
        var parcel = new VPolygon(
            new VXYZ(-232.67, -167.57),
            new VXYZ(-324.67, -32.57),
            new VXYZ(-286.67, 139.43),
            new VXYZ(-192.67, 106.43),
            new VXYZ(-3.67, 181.43),
            new VXYZ(373.33, 181.43),
            new VXYZ(373.33, -103.57),
            new VXYZ(132.33, -211.57));

        var pieces = parcel.Slice(new VXYZ(-289.24, 127.80), new VXYZ(373.33, 154.71));

        // Two slivers above the cut plus the body below it — the body is what used to go missing.
        Assert.Equal(3, pieces.Count);
        Assert.Equal(parcel.Area, TotalArea(pieces), 4);

        // The body must dominate; the old result totalled under 6% of the parcel.
        var body = pieces.OrderByDescending(p => p.Area).First();
        Assert.True(body.Area > parcel.Area * 0.9,
            $"expected the body below the cut, got {body.Area} of {parcel.Area}");
    }

    [Fact]
    public void TheBigPieceBelowTheCutIsActuallyReturned()
    {
        // The old implementation returned only the two slivers above the line; the body of the
        // polygon was never emitted. Assert it exists and contains an interior point of the body.
        var pieces = NotchedPolygon().Slice(new VXYZ(-50, 70), new VXYZ(150, 70));

        var body = pieces.OrderByDescending(p => p.Area).First();

        Assert.True(body.Contains(new VXYZ(50, 20)), "the body below the cut should be returned");
        Assert.Equal(6400, body.Area, Precision);
    }

    [Fact]
    public void ASimpleConvexCutStillSplitsInTwo()
    {
        // The two-crossing path the old walker handled correctly — must not regress.
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(-10, 30), new VXYZ(110, 30));

        Assert.Equal(2, pieces.Count);
        Assert.Equal(10000, TotalArea(pieces), Precision);
        Assert.Equal(new[] { 3000d, 7000d }, pieces.Select(p => p.Area).OrderBy(a => a).ToArray());
    }

    [Fact]
    public void ADiagonalCutIsAreaPreservingToo()
    {
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(0, 0), new VXYZ(100, 100));

        Assert.Equal(2, pieces.Count);
        Assert.Equal(10000, TotalArea(pieces), Precision);
        Assert.All(pieces, p => Assert.Equal(5000, p.Area, Precision));
    }

    [Fact]
    public void TheSliceLineIsInfiniteNotASegment()
    {
        // Both defining points sit far off to one side; the line through them still cuts.
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(500, 50), new VXYZ(600, 50));

        Assert.Equal(2, pieces.Count);
        Assert.Equal(10000, TotalArea(pieces), Precision);
    }

    [Fact]
    public void ALineThatMissesThePolygonReturnsTheOriginal()
    {
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(-10, 500), new VXYZ(110, 500));

        Assert.Single(pieces);
        Assert.Equal(10000, pieces[0].Area, Precision);
    }

    [Fact]
    public void ALineAlongAnEdgeGrazesRatherThanSlices()
    {
        // Collinear with the bottom edge: one side clips to nothing, so there is nothing to split.
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(-10, 0), new VXYZ(110, 0));

        Assert.Single(pieces);
        Assert.Equal(10000, pieces[0].Area, Precision);
    }

    [Fact]
    public void ALineTouchingASingleVertexGrazesRatherThanSlices()
    {
        // The diagonal through one corner of a triangle only touches it at that corner.
        var triangle = new VPolygon(new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(100, 100));

        var pieces = triangle.Slice(new VXYZ(-100, 100), new VXYZ(100, -100));

        Assert.Single(pieces);
        Assert.Equal(5000, pieces[0].Area, Precision);
    }

    [Fact]
    public void CoincidentDefiningPointsDefineNoLineAndReturnTheOriginal()
    {
        var square = Square(0, 0, 100);

        var pieces = square.Slice(new VXYZ(50, 50), new VXYZ(50, 50));

        Assert.Single(pieces);
        Assert.Equal(10000, pieces[0].Area, Precision);
    }

    [Fact]
    public void ResultPiecesInheritTheSourceStyling()
    {
        var square = Square(0, 0, 100);
        square.Color = "Gray";
        square.FillColor = "Beige";
        square.LineWeight = 2.5;

        var pieces = square.Slice(new VXYZ(-10, 30), new VXYZ(110, 30));

        Assert.All(pieces, p =>
        {
            Assert.Equal("Gray", p.Color);
            Assert.Equal("Beige", p.FillColor);
            Assert.Equal(2.5, p.LineWeight);
        });
    }

    [Fact]
    public void SlicingDoesNotLeaveTheHalfPlaneScratchGeometryOnTheCanvas()
    {
        // The half-plane clip rectangles are built with `new VPolygon`, which auto-registers with
        // Shape.DefaultRegistry — they must be constructed under SuspendAutoRegistration or every
        // slice drops two enormous phantom rectangles onto the canvas (CLAUDE.md notes 6/10/64).
        var registry = new RecordingRegistry();
        var previous = Shape.DefaultRegistry;
        Shape.DefaultRegistry = registry;
        try
        {
            var polygon = NotchedPolygon();
            registry.Registered.Clear();

            var pieces = polygon.Slice(new VXYZ(-50, 70), new VXYZ(150, 70));

            // Only the resulting pieces may register; nothing may be larger than the source.
            Assert.Equal(pieces.Count, registry.Registered.Count);
            Assert.All(registry.Registered, s => Assert.True(s.GetBounds().Max.X <= 100 + 1e-6));
        }
        finally
        {
            Shape.DefaultRegistry = previous;
        }
    }

    private sealed class RecordingRegistry : IShapeRegistry
    {
        public List<Shape> Registered { get; } = new();

        public void Register(Shape shape) => Registered.Add(shape);

        public void Unregister(Shape shape) => Registered.Remove(shape);

        public void Clear() => Registered.Clear();

        public void MoveAbove(Shape shape, Shape referenceShape) { }

        public void MoveBehind(Shape shape, Shape referenceShape) { }
    }
}
