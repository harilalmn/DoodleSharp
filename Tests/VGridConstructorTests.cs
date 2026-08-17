using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Overload resolution for <see cref="VGrid"/>. The uniform-spacing constructor used to be
/// unreachable: <c>new VGrid(loc, 5, 5, 10)</c> was ambiguous (CS0121) between the six-parameter
/// constructor with two defaulted spacings and the five-parameter uniform one with a defaulted
/// <c>centered</c>. Callers had to pass <c>centered</c> explicitly just to make the code compile.
///
/// <para>
/// These tests are as much a compile-time check as a runtime one — every call shape below has to
/// keep binding to exactly one constructor.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class VGridConstructorTests
{
    private static readonly VXYZ Origin = new(0, 0);

    [Fact]
    public void ThreeArguments_UsesUnitSpacing()
    {
        var grid = new VGrid(Origin, 5, 5);

        Assert.Equal(1.0, grid.XSpacing);
        Assert.Equal(1.0, grid.YSpacing);
        Assert.True(grid.Centered);
    }

    [Fact]
    public void FourArguments_MeansUniformSpacing()
    {
        // The call that did not compile at all before.
        var grid = new VGrid(Origin, 5, 5, 10);

        Assert.Equal(10.0, grid.XSpacing);
        Assert.Equal(10.0, grid.YSpacing);   // not 1.0 — a square grid is what a reader expects
        Assert.True(grid.Centered);
    }

    [Fact]
    public void TwoSpacings_AreIndependent()
    {
        var grid = new VGrid(Origin, 4, 4, 20, 15, false);

        Assert.Equal(20.0, grid.XSpacing);
        Assert.Equal(15.0, grid.YSpacing);
        Assert.False(grid.Centered);
    }

    [Fact]
    public void SpacingPlusCentered_BindsToTheUniformOverload()
    {
        var grid = new VGrid(Origin, 6, 6, 25, false);

        Assert.Equal(25.0, grid.XSpacing);
        Assert.Equal(25.0, grid.YSpacing);
        Assert.False(grid.Centered);
    }

    [Fact]
    public void CenteredOnly_StillBinds()
    {
        var grid = new VGrid(Origin, 3, 3, false);

        Assert.Equal(1.0, grid.XSpacing);
        Assert.Equal(1.0, grid.YSpacing);
        Assert.False(grid.Centered);
    }

    [Fact]
    public void PointCountMatchesTheGridSize()
    {
        Assert.Equal(15, new VGrid(Origin, 5, 3, 10).Count);
    }
}
