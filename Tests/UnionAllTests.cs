using System.Linq;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// <c>BooleanOps.UnionAll</c> — union any number of polygons and get every piece back.
///
/// <para>
/// <c>Union</c> insists on a single polygon and returns null when the inputs do not all overlap or
/// touch, which left no public way to union a set of separate shapes and simply see the result. The
/// diagnostic used to send people to <c>UnionWithHoles</c>, which only takes two.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class UnionAllTests
{
    private static VPolygon Square(double x, double y, double size) => new(new[]
    {
        new VXYZ(x, y),
        new VXYZ(x + size, y),
        new VXYZ(x + size, y + size),
        new VXYZ(x, y + size)
    });

    private static double TotalArea(System.Collections.Generic.List<VPolygon> pieces)
        => pieces.Sum(p => p.Area);

    [Fact]
    public void OverlappingSquaresMergeIntoOnePiece()
    {
        var pieces = BooleanOps.UnionAll(Square(0, 0, 10), Square(5, 5, 10));

        Assert.Single(pieces);
        // 100 + 100 - 25 overlap.
        Assert.Equal(175, TotalArea(pieces), 6);
    }

    [Fact]
    public void DisjointSquaresComeBackAsSeparatePieces()
    {
        // The case Union returns null for.
        var pieces = BooleanOps.UnionAll(Square(0, 0, 10), Square(100, 100, 10));

        Assert.Equal(2, pieces.Count);
        Assert.Equal(200, TotalArea(pieces), 6);
        Assert.Null(BooleanOps.Union(Square(0, 0, 10), Square(100, 100, 10)));
    }

    [Fact]
    public void AChainThatOnlyConnectsThroughAThirdPolygonBecomesOnePiece()
    {
        // A and C do not touch each other; B bridges them. Order matters to the algorithm, so this
        // is the case that catches a naive fold that only compares against the running result.
        var a = Square(0, 0, 10);
        var c = Square(16, 0, 10);
        var bridge = Square(8, 0, 10);

        var pieces = BooleanOps.UnionAll(a, c, bridge);

        Assert.Single(pieces);
    }

    [Fact]
    public void MixedOverlappingAndDisjointGroupsCorrectly()
    {
        var pieces = BooleanOps.UnionAll(
            Square(0, 0, 10), Square(5, 5, 10),        // group 1, overlapping
            Square(100, 0, 10), Square(105, 5, 10),    // group 2, overlapping
            Square(500, 500, 10));                     // alone

        Assert.Equal(3, pieces.Count);
    }

    [Fact]
    public void EmptyInputGivesAnEmptyListNotNull()
    {
        Assert.Empty(BooleanOps.UnionAll());
        Assert.Empty(BooleanOps.UnionAll(new VPolygon[0]));
    }

    [Fact]
    public void ASingleInputComesBackAsOnePiece()
    {
        var pieces = BooleanOps.UnionAll(Square(0, 0, 10));

        Assert.Single(pieces);
        Assert.Equal(100, TotalArea(pieces), 6);
    }

    [Fact]
    public void TouchingEdgeToEdgeCountsAsMerged()
    {
        // Shared full edge — the degenerate case the hand-rolled clipper used to get wrong
        // (CLAUDE.md note 32).
        var pieces = BooleanOps.UnionAll(Square(0, 0, 10), Square(10, 0, 10));

        Assert.Single(pieces);
        Assert.Equal(200, TotalArea(pieces), 6);
    }
}
