using System;
using System.Linq;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The viewport tree: recursive subdivision, 0-based row-first indexing, and the rule that a leaf's
/// only cell is itself.
///
/// <para>
/// Structural only — nothing here touches the shape registry, so it needs no canvas. The
/// registry-facing half lives in <c>ViewportPlacementTests</c>.
/// </para>
/// </summary>
// The viewport tree is process-wide static state, exactly like the shape registry — so these have
// to be serialised against every other test that touches it, or they race the placement, host and
// run suites. Note 9's rule.
[Collection("CanvasState")]
public class ViewportTreeTests : IDisposable
{
    public ViewportTreeTests() => Viewport.Reset();
    public void Dispose() => Viewport.Reset();

    [Fact]
    public void TheDefaultLayoutIsOneUndividedViewport()
    {
        Assert.Equal(1, Viewport.Root.Rows);
        Assert.Equal(1, Viewport.Root.Columns);
        Assert.True(Viewport.Root.IsLeaf);
        Assert.Single(Viewport.Leaves());
    }

    /// <summary>
    /// The rule the whole design rests on: a 1x1 viewport's single cell <i>is</i> that viewport. It
    /// is what makes a bare <c>Place()</c>, an auto-registered shape and
    /// <c>Place(Viewports[0][0])</c> the same thing without a special case anywhere.
    /// </summary>
    [Fact]
    public void ALeafsOnlyCellIsItself()
    {
        Assert.Same(Viewport.Root, Viewport.Root[0][0]);
    }

    /// <summary>
    /// The negative control for the test above: once the root is divided, its cells are genuinely
    /// different objects. Without this, "a leaf's cell is itself" would pass just as happily against
    /// an indexer that ignored its arguments entirely.
    /// </summary>
    [Fact]
    public void OnceDividedTheCellsAreDistinctFromTheRoot()
    {
        Viewport.Root.Columns = 2;

        Assert.NotSame(Viewport.Root, Viewport.Root[0][0]);
        Assert.NotSame(Viewport.Root[0][0], Viewport.Root[0][1]);
        Assert.False(Viewport.Root.IsLeaf);
    }

    [Fact]
    public void IndexingIsZeroBasedAndRowFirst()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 3;

        var leaves = Viewport.Leaves();
        Assert.Equal(6, leaves.Count);

        // Depth-first, left to right — the order the cells appear on screen.
        Assert.Same(leaves[0], Viewport.Root[0][0]);
        Assert.Same(leaves[2], Viewport.Root[0][2]);
        Assert.Same(leaves[3], Viewport.Root[1][0]);
        Assert.Same(leaves[5], Viewport.Root[1][2]);
    }

    [Fact]
    public void ACellCanBeSubdividedAgain()
    {
        Viewport.Root.Columns = 2;
        var right = Viewport.Root[0][1];
        right.Rows = 3;

        Assert.False(right.IsLeaf);
        Assert.Equal(1, right.Depth);          // root is 0, its cells are 1
        Assert.Equal(2, right[0][0].Depth);
        Assert.Equal(4, Viewport.Leaves().Count);          // left, plus three on the right
        Assert.Same(Viewport.Root[0][0], Viewport.Leaves()[0]);
        Assert.Same(right[1][0], Viewport.Leaves()[2]);
    }

    [Fact]
    public void SubdividingOneCellLeavesItsSiblingAlone()
    {
        Viewport.Root.Columns = 2;
        var left = Viewport.Root[0][0];
        Viewport.Root[0][1].Rows = 3;

        Assert.Same(left, Viewport.Root[0][0]);
        Assert.True(left.IsLeaf);
    }

    /// <summary>
    /// Widening a parent must not discard a child's own subdivision, and must not move the cells
    /// that did not need to move. A host keys its canvases on node identity, so a needless new node
    /// throws away that cell's pan and zoom.
    /// </summary>
    [Fact]
    public void ResizingAParentReusesTheCellsThatSurvive()
    {
        Viewport.Root.Columns = 2;
        var first = Viewport.Root[0][0];
        var second = Viewport.Root[0][1];
        second.Rows = 4;

        Viewport.Root.Columns = 3;

        Assert.Same(first, Viewport.Root[0][0]);
        Assert.Same(second, Viewport.Root[0][1]);
        Assert.Equal(4, second.Rows);          // its own subdivision survived
        Assert.True(Viewport.Root[0][2].IsLeaf);
    }

    [Fact]
    public void ShrinkingDetachesTheCellsItRemoves()
    {
        Viewport.Root.Rows = 3;
        var survivor = Viewport.Root[0][0];
        var removed = Viewport.Root[2][0];

        Viewport.Root.Rows = 2;

        Assert.True(survivor.IsAttached);
        Assert.False(removed.IsAttached);
        Assert.Same(survivor, Viewport.Root[0][0]);
    }

    [Fact]
    public void ResetReturnsToOneUndividedViewport()
    {
        Viewport.Root.Rows = 3;
        Viewport.Root.Columns = 2;
        var before = Viewport.Root[1][1];

        Viewport.Reset();

        Assert.True(Viewport.Root.IsLeaf);
        Assert.Single(Viewport.Leaves());
        Assert.False(before.IsAttached);
    }

    /// <summary>
    /// Re-declaring the layout it already has must raise nothing. <c>Main()</c> runs again on every
    /// F5 and re-states <c>Viewports.Rows = 2</c>, so treating that as a change would rebuild the
    /// grid — and throw away every cell's pan and zoom — on every single run.
    /// </summary>
    [Fact]
    public void AssigningTheSameSizeChangesNothingAndRaisesNothing()
    {
        Viewport.Root.Columns = 2;
        var cell = Viewport.Root[0][1];

        var raised = 0;
        Action handler = () => raised++;
        Viewport.LayoutChanged += handler;
        try
        {
            Viewport.Root.Columns = 2;
            Viewport.Root.Rows = 1;
        }
        finally { Viewport.LayoutChanged -= handler; }

        Assert.Equal(0, raised);
        Assert.Same(cell, Viewport.Root[0][1]);
    }

    [Fact]
    public void AGenuineChangeRaisesLayoutChanged()
    {
        var raised = 0;
        Action handler = () => raised++;
        Viewport.LayoutChanged += handler;
        try { Viewport.Root.Rows = 2; }
        finally { Viewport.LayoutChanged -= handler; }

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// The message is the feature here: indexing past the current size almost always means the code
    /// indexed before it set <c>Rows</c>, and the fix is unguessable unless the error says how big
    /// the grid actually is.
    /// </summary>
    [Fact]
    public void IndexingPastTheEndThrowsAndSaysWhatTheLayoutIs()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root[2]);

        Assert.Contains("Viewports[2] is out of range", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2 rows x 2 columns", ex.Message, StringComparison.Ordinal);
        Assert.Contains("valid rows 0..1, columns 0..1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Set Viewports.Rows before placing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexingPastTheEndOfARowThrowsToo()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root[0][1]);

        Assert.Contains("1 row x 1 column", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Set Viewports.Columns before placing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedViewportNamesItselfInTheMessage()
    {
        Viewport.Root.Columns = 2;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root[0][1][3]);

        Assert.Contains("Viewports[0][1][3] is out of range", ex.Message, StringComparison.Ordinal);
        Assert.Contains("That viewport is 1 row x 1 column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeIndicesThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root[0][-1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(Viewport.MaxDimension + 1)]
    public void RowsAndColumnsAreBounded(int bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root.Rows = bad);
        Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root.Columns = bad);
        Assert.True(Viewport.Root.IsLeaf);   // a rejected assignment changes nothing
    }

    [Fact]
    public void PathIsHowTheViewportIsWrittenInCode()
    {
        Viewport.Root.Columns = 2;
        Viewport.Root[0][1].Rows = 2;

        Assert.Equal("Viewports", Viewport.Root.Path);
        Assert.Equal("Viewports[0][1]", Viewport.Root[0][1].Path);
        Assert.Equal("Viewports[0][1][1][0]", Viewport.Root[0][1][1][0].Path);
        Assert.Equal("Viewports[0][1]", Viewport.Root[0][1].ToString());
    }

    /// <summary>
    /// A shape placed on a cell that is later split keeps drawing in that cell's first sub-cell —
    /// "it stayed where it was, it just got split" — however many times it is split again.
    /// </summary>
    [Fact]
    public void FirstLeafFollowsSubdivisionAllTheWayDown()
    {
        Assert.Same(Viewport.Root, Viewport.Root.FirstLeaf());

        Viewport.Root.Columns = 2;
        Assert.Same(Viewport.Root[0][0], Viewport.Root.FirstLeaf());

        Viewport.Root[0][0].Rows = 3;
        Assert.Same(Viewport.Root[0][0][0][0], Viewport.Root.FirstLeaf());
    }

    /// <summary>
    /// The row accessor must stay a top-level type. Project creation and the shadowed-name
    /// diagnostic both build their reserved-name set from <c>GetExportedTypes()</c>, which includes
    /// public <i>nested</i> types and reports their <c>Namespace</c> as the enclosing one — so a
    /// nested <c>Viewport.Row</c> would silently reserve the bare word "Row" for every project.
    /// </summary>
    [Fact]
    public void TheGeometryAssemblyExposesNoPublicNestedTypes()
    {
        var nested = typeof(Shape).Assembly.GetExportedTypes()
            .Where(t => t.IsNested)
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(nested);
    }
}
