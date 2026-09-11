using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <c>VXYZ(angleInDegrees, distance, fromPoint = null)</c> — the polar constructor.
///
/// <para>
/// It shares its first two parameter types with the Cartesian <c>VXYZ(x, y)</c>, so two plain
/// numbers must keep binding to the Cartesian one; the tests pin both sides of that.
/// </para>
/// </summary>
public class VXYZPolarConstructorTests
{
    private const double Tol = 1e-9;

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(90, 0, 10)]
    [InlineData(180, -10, 0)]
    [InlineData(270, 0, -10)]
    [InlineData(-90, 0, -10)]
    public void AngleIsDegreesCounterClockwiseFromPlusX(double angle, double expectedX, double expectedY)
    {
        var p = new VXYZ(angle, 10, VXYZ.Zero);

        Assert.Equal(expectedX, p.X, Tol);
        Assert.Equal(expectedY, p.Y, Tol);
        Assert.Equal(0, p.Z, Tol);
    }

    [Fact]
    public void MeasuresFromTheGivenPointAndKeepsItsZ()
    {
        var from = new VXYZ(10, 20, 5);

        var p = new VXYZ(45, 100, from);

        double leg = 100 / System.Math.Sqrt(2);
        Assert.Equal(10 + leg, p.X, Tol);
        Assert.Equal(20 + leg, p.Y, Tol);
        Assert.Equal(5, p.Z, Tol);
        Assert.Equal(100, from.DistanceTo(p), Tol);
    }

    [Fact]
    public void NullFromPointMeansZero()
    {
        Assert.True(new VXYZ(30, 50, null) == new VXYZ(30, 50, VXYZ.Zero));
    }

    [Fact]
    public void NamedArgumentsReachTheDefault()
    {
        // Only the polar constructor has parameters with these names, so the default applies.
        var p = new VXYZ(angleInDegrees: 90, distance: 25);

        Assert.Equal(0, p.X, Tol);
        Assert.Equal(25, p.Y, Tol);
    }

    [Fact]
    public void TwoNumbersStayCartesian()
    {
        var p = new VXYZ(45, 100);

        Assert.Equal(45, p.X, Tol);
        Assert.Equal(100, p.Y, Tol);
    }

    [Fact]
    public void NegativeDistanceLandsOnTheOppositeSide()
    {
        var p = new VXYZ(0, -10, new VXYZ(5, 5));

        Assert.Equal(-5, p.X, Tol);
        Assert.Equal(5, p.Y, Tol);
    }

    [Fact]
    public void AgreesWithRotatingBasisX()
    {
        var from = new VXYZ(-3, 7);

        var polar = new VXYZ(123, 40, from);
        var rotated = from + VXYZ.BasisX.Rotate(123) * 40;

        Assert.True(polar.IsAlmostEqualTo(rotated));
    }
}
