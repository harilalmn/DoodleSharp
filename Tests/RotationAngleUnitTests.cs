using System;
using C2VGeometry;
using Xunit;

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

    // ── VXYZ.AngleTo: the second radians-in-a-degrees-library trap ───────────────────────────────
    //
    // Reported as "the text mask is slightly off axis when the line points towards negative X".
    // The recipe was `text.Rotate(text.Location, dir.AngleTo(VXYZ.BasisX))` — and for a direction
    // along -X, AngleTo answers pi, which lands in the degrees-taking Angle as a 3.14 DEGREE tilt.
    // A bare label made that invisible; a filled mask rectangle made it obvious.

    [Fact]
    public void AngleToRadiansIsRadians()
    {
        Assert.Equal(Math.PI, new VXYZ(-1, 0).AngleToRadians(VXYZ.BasisX), Tol);
        Assert.Equal(Math.PI / 2, VXYZ.BasisY.AngleToRadians(VXYZ.BasisX), Tol);
        Assert.Equal(0, VXYZ.BasisX.AngleToRadians(VXYZ.BasisX), Tol);
    }

    [Fact]
    public void AngleToDegreesIsTheDegreeSpelling()
    {
        Assert.Equal(180, new VXYZ(-1, 0).AngleToDegrees(VXYZ.BasisX), Tol);
        Assert.Equal(90, VXYZ.BasisY.AngleToDegrees(VXYZ.BasisX), Tol);
        Assert.Equal(45, new VXYZ(1, 1).AngleToDegrees(VXYZ.BasisX), Tol);
    }

    [Fact]
    public void AngleToDegreesTurnsAReversedDirectionRightRoundNotByThreeDegrees()
    {
        // The reported symptom, as an assertion: rotating a label by the angle between its line and
        // the X axis must be a half turn for a reversed line, not an almost-imperceptible tilt.
        var reversed = new VXYZ(-1, 0);

        Assert.Equal(180, reversed.AngleToDegrees(VXYZ.BasisX), Tol);
        Assert.Equal(Math.PI, reversed.AngleToRadians(VXYZ.BasisX), Tol);
        Assert.True(Math.Abs(reversed.AngleToRadians(VXYZ.BasisX) - 3.14) < 0.01,
            "the radians answer really is the ~3.14 that was being read as degrees");
    }

    [Fact]
    public void BothSpellingsAgreeAcrossTheRange()
    {
        for (int deg = 0; deg <= 180; deg += 15)
        {
            var v = VXYZ.BasisX.Rotate(deg);   // VXYZ.Rotate takes DEGREES
            Assert.Equal(deg, v.AngleToDegrees(VXYZ.BasisX), 1e-6);
            Assert.Equal(((double)deg).ToRadians(), v.AngleToRadians(VXYZ.BasisX), 1e-6);
        }
    }

    [Fact]
    public void ZeroLengthVectorsAnswerZeroInBothSpellings()
    {
        Assert.Equal(0, VXYZ.Zero.AngleToRadians(VXYZ.BasisX), Tol);
        Assert.Equal(0, VXYZ.Zero.AngleToDegrees(VXYZ.BasisX), Tol);
    }

    [Fact]
    public void TheAmbiguousNameStillWorksAndIsStillRadians()
    {
        // Deprecated, not redefined: making AngleTo return degrees would silently change every
        // existing `Math.Cos(a.AngleTo(b))` in the wild. Note 70's precedent.
#pragma warning disable CS0618
        Assert.Equal(Math.PI, new VXYZ(-1, 0).AngleTo(VXYZ.BasisX), Tol);
#pragma warning restore CS0618
    }
}
