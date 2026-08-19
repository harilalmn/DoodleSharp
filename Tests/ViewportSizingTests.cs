using System;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Row heights and column widths, written the way XAML writes a grid length: <c>"*"</c>,
/// <c>"3*"</c>, or a number of pixels.
/// </summary>
[Collection("CanvasState")]
public class ViewportSizingTests : IDisposable
{
    public ViewportSizingTests() => Viewport.Reset();
    public void Dispose() => Viewport.Reset();

    /// <summary>The two spellings from the request, exactly as written.</summary>
    [Fact]
    public void TheRequestedSpellingsWork()
    {
        Viewport.Root.Rows = 3;
        Viewport.Root.Columns = 4;

        Viewport.Root[1].Height = "3*";
        Viewport.Root[2][3].Width = "4*";

        Assert.Equal("3*", Viewport.Root[1].Height);
        Assert.Equal("4*", Viewport.Root[2][3].Width);
    }

    [Fact]
    public void EverythingStartsAtAnEqualShare()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;

        Assert.Equal("*", Viewport.Root[0].Height);
        Assert.Equal("*", Viewport.Root[0][0].Height);
        Assert.Equal("*", Viewport.Root[0][0].Width);
    }

    /// <summary>
    /// A height belongs to the row, not to one cell — the same rule a XAML <c>RowDefinition</c>
    /// follows. Setting it through any cell in the row, or through the row itself, must be the same
    /// act, or two cells in one row could disagree about how tall their row is.
    /// </summary>
    [Fact]
    public void HeightAddressesTheRowAndWidthAddressesTheColumn()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;

        Viewport.Root[0][0].Height = "2*";
        Assert.Equal("2*", Viewport.Root[0][1].Height);      // its neighbour in the same row
        Assert.Equal("2*", Viewport.Root[0].Height);         // and the row itself
        Assert.Equal("*", Viewport.Root[1][0].Height);       // the other row is untouched

        Viewport.Root[0][1].Width = "5*";
        Assert.Equal("5*", Viewport.Root[1][1].Width);       // its neighbour in the same column
        Assert.Equal("*", Viewport.Root[0][0].Width);
    }

    [Fact]
    public void NestedViewportsAreSizedWithinTheirOwnParent()
    {
        Viewport.Root.Columns = 2;
        var right = Viewport.Root[0][1];
        right.Rows = 3;

        right[0].Height = "4*";

        Assert.Equal("4*", right[0][0].Height);
        Assert.Equal("*", right[1][0].Height);
        Assert.Equal("*", Viewport.Root[0][0].Width);        // the outer grid is unaffected
    }

    [Theory]
    [InlineData("*", "*")]
    [InlineData("3*", "3*")]
    [InlineData("1.5*", "1.5*")]
    [InlineData(" 2* ", "2*")]          // trimmed
    [InlineData("240", "240")]          // fixed pixels
    [InlineData("1*", "*")]             // one share is just "*"
    public void SizesRoundTripThroughTheirCanonicalSpelling(string set, string readBack)
    {
        Viewport.Root.Rows = 2;
        Viewport.Root[0].Height = set;

        Assert.Equal(readBack, Viewport.Root[0].Height);
    }

    /// <summary>
    /// A canvas has no natural size, so an auto-sized viewport collapses to nothing — the drawing
    /// simply disappears. Rejected by name, with the alternatives in the message, rather than
    /// leaving that to be discovered.
    /// </summary>
    [Fact]
    public void AutoIsRejectedAndSaysWhy()
    {
        Viewport.Root.Rows = 2;

        var ex = Assert.Throws<ArgumentException>(() => Viewport.Root[0].Height = "Auto");

        Assert.Contains("collapse to nothing", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\"*\"", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wide")]
    [InlineData("-3*")]
    [InlineData("0*")]
    [InlineData("-40")]
    [InlineData("**")]
    public void AMalformedSizeThrowsWhereItIsWritten(string bad)
    {
        Viewport.Root.Rows = 2;

        Assert.Throws<ArgumentException>(() => Viewport.Root[0].Height = bad);
        Assert.Equal("*", Viewport.Root[0].Height);          // and changes nothing
    }

    /// <summary>The root always fills the pane, so it has no size of its own to set.</summary>
    [Fact]
    public void TheRootHasNoSizeOfItsOwn()
    {
        var height = Assert.Throws<InvalidOperationException>(() => Viewport.Root.Height = "2*");
        Assert.Throws<InvalidOperationException>(() => Viewport.Root.Width = "2*");
        Assert.Throws<InvalidOperationException>(() => _ = Viewport.Root.Height);

        Assert.Contains("always fills the pane", height.Message, StringComparison.Ordinal);
        Assert.Contains("Viewports.Rows = 2", height.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sizes survive a resize that keeps the row or column. Losing them would mean a script that
    /// sets a height and then adds a column silently reverts to equal shares.
    /// </summary>
    [Fact]
    public void ResizingKeepsTheSizesOfWhatSurvives()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;
        Viewport.Root[0].Height = "3*";
        Viewport.Root[0][0].Width = "2*";

        Viewport.Root.Columns = 3;

        Assert.Equal("3*", Viewport.Root[0].Height);
        Assert.Equal("2*", Viewport.Root[0][0].Width);
        Assert.Equal("*", Viewport.Root[0][2].Width);        // the new column starts at an equal share
    }

    [Fact]
    public void ResetRestoresEqualShares()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root[0].Height = "3*";

        Viewport.Reset();
        Viewport.Root.Rows = 2;

        Assert.Equal("*", Viewport.Root[0].Height);
    }

    /// <summary>
    /// A size change has to reach the host, or the layout on screen keeps the old proportions. It is
    /// the same event subdivision raises, because a host has to re-lay-out for either.
    /// </summary>
    [Fact]
    public void ASizeChangeRaisesLayoutChanged()
    {
        Viewport.Root.Rows = 2;

        var raised = 0;
        Action handler = () => raised++;
        Viewport.LayoutChanged += handler;
        try
        {
            Viewport.Root[0].Height = "3*";
            Viewport.Root[0].Height = "3*";      // re-stating it is a no-op, as on every re-run
        }
        finally { Viewport.LayoutChanged -= handler; }

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Sizing a row of an undivided viewport looks like it does nothing — a single row always fills
    /// the space. It is not a no-op: the value is kept, and takes effect the moment the grid grows.
    /// Worth pinning, because "silently does nothing" is what it looks like from outside.
    /// </summary>
    [Fact]
    public void ASizeSetBeforeTheGridGrowsIsRememberedNotDiscarded()
    {
        Viewport.Root[0].Height = "2*";
        Assert.Equal("2*", Viewport.Root[0].Height);

        Viewport.Root.Rows = 3;

        Assert.Equal("2*", Viewport.Root[0].Height);
        Assert.Equal("*", Viewport.Root[1].Height);
    }

    /// <summary>
    /// The parsed accessors report an out-of-range index the same way every neighbouring member of
    /// this type does. They used to index the array raw, so they were the one pair that answered
    /// with a bare <c>IndexOutOfRangeException</c> and no idea what the layout was.
    /// </summary>
    [Fact]
    public void TheParsedAccessorsRejectAnOutOfRangeIndexLikeEverythingElse()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;

        var row = Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root.RowHeightAt(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Root.ColumnWidthAt(-1));

        Assert.Contains("2 rows x 2 columns", row.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*", 1, true)]
    [InlineData("3*", 3, true)]
    [InlineData("0.5*", 0.5, true)]
    [InlineData("240", 240, false)]
    [InlineData("0", 0, false)]
    public void TheParsedFormIsWhatAHostLaysOutWith(string text, double value, bool isStar)
    {
        var length = ViewportLength.Parse(text);

        Assert.Equal(value, length.Value);
        Assert.Equal(isStar, length.IsStar);
    }
}
