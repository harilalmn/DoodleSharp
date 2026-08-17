using System;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Rotation angle units. Everything user-facing in this library takes degrees; the one exception
/// is <see cref="VTransform.CreateRotation"/>, which is radians and says so.
///
/// <para>
/// <c>VCoordinateSystem.Rotate(axis, angleDegrees)</c> used to pass its degrees straight into
/// <c>CreateRotation</c>, so a documented "quarter turn" of 90 actually rotated by 90 *radians*
/// — about 237 degrees. The parameter name promised degrees, so nothing could have depended on
/// the old behaviour on purpose.
/// </para>
/// </summary>
public class RotationAngleUnitTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void CoordinateSystemRotateTakesDegrees()
    {
        var cs = VCoordinateSystem.Identity;

        var turned = cs.Rotate(VXYZ.BasisZ, 90);

        // X goes to Y, Y goes to -X.
        Assert.Equal(0, turned.XAxis.X, Tol);
        Assert.Equal(1, turned.XAxis.Y, Tol);
        Assert.Equal(-1, turned.YAxis.X, Tol);
        Assert.Equal(0, turned.YAxis.Y, Tol);
    }

    [Fact]
    public void CoordinateSystemRotateLeavesTheOriginAlone()
    {
        var origin = new VXYZ(10, 20, 30);
        var cs = VCoordinateSystem.ByOrigin(origin);

        var turned = cs.Rotate(VXYZ.BasisZ, 45);

        Assert.Equal(origin.X, turned.Origin.X, Tol);
        Assert.Equal(origin.Y, turned.Origin.Y, Tol);
        Assert.Equal(origin.Z, turned.Origin.Z, Tol);
    }

    [Fact]
    public void CoordinateSystemRotateAgreesWithVectorRotate()
    {
        // The two rotation APIs a user is most likely to mix must not disagree.
        var cs = VCoordinateSystem.Identity;

        var viaSystem = cs.Rotate(VXYZ.BasisZ, 30).XAxis;
        var viaVector = VXYZ.BasisX.Rotate(30);

        Assert.Equal(viaVector.X, viaSystem.X, Tol);
        Assert.Equal(viaVector.Y, viaSystem.Y, Tol);
    }

    [Fact]
    public void CreateRotationIsStillRadians()
    {
        // Deliberately unchanged: flipping it to degrees would silently break any user code
        // already passing Math.PI / 2. The parameter is now named angleRadians.
        var t = VTransform.CreateRotation(VXYZ.BasisZ, Math.PI / 2);
        var x = t.OfVector(VXYZ.BasisX);

        Assert.Equal(0, x.X, Tol);
        Assert.Equal(1, x.Y, Tol);
    }

    [Fact]
    public void CreateRotationDegreesIsTheDegreeSpelling()
    {
        var radians = VTransform.CreateRotation(VXYZ.BasisZ, Math.PI / 3);
        var degrees = VTransform.CreateRotationDegrees(VXYZ.BasisZ, 60);

        Assert.Equal(radians.OfVector(VXYZ.BasisX).X, degrees.OfVector(VXYZ.BasisX).X, Tol);
        Assert.Equal(radians.OfVector(VXYZ.BasisX).Y, degrees.OfVector(VXYZ.BasisX).Y, Tol);
    }
}
