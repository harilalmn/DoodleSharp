using System;

namespace C2VGeometry;

/// <summary>
/// Represents a 3D coordinate system with origin and three orthonormal basis vectors.
/// Follows Dynamo API conventions with factory methods.
/// </summary>
public class VCoordinateSystem
{
    /// <summary>
    /// The origin point of the coordinate system.
    /// </summary>
    public VXYZ Origin { get; }

    /// <summary>
    /// The X-axis direction (normalized).
    /// </summary>
    public VXYZ XAxis { get; }

    /// <summary>
    /// The Y-axis direction (normalized).
    /// </summary>
    public VXYZ YAxis { get; }

    /// <summary>
    /// The Z-axis direction (normalized).
    /// </summary>
    public VXYZ ZAxis { get; }

    /// <summary>
    /// Gets the identity coordinate system at origin with standard basis vectors.
    /// </summary>
    public static VCoordinateSystem Identity => new VCoordinateSystem(VXYZ.Zero, VXYZ.BasisX, VXYZ.BasisY, VXYZ.BasisZ);

    private VCoordinateSystem(VXYZ origin, VXYZ xAxis, VXYZ yAxis, VXYZ zAxis)
    {
        Origin = origin;
        XAxis = xAxis;
        YAxis = yAxis;
        ZAxis = zAxis;
    }

    /// <summary>
    /// Creates a coordinate system from an origin and three axis vectors.
    /// The vectors will be normalized.
    /// </summary>
    public static VCoordinateSystem ByOriginVectors(VXYZ origin, VXYZ xAxis, VXYZ yAxis, VXYZ zAxis)
    {
        return new VCoordinateSystem(
            origin,
            xAxis.Normalize(),
            yAxis.Normalize(),
            zAxis.Normalize()
        );
    }

    /// <summary>
    /// Creates a coordinate system from an origin and two vectors.
    /// The Z-axis is computed as the cross product of X and Y.
    /// </summary>
    public static VCoordinateSystem ByOriginXY(VXYZ origin, VXYZ xAxis, VXYZ yAxis)
    {
        var x = xAxis.Normalize();
        var y = yAxis.Normalize();
        var z = x.CrossProduct(y).Normalize();
        // Recompute Y to ensure orthogonality
        y = z.CrossProduct(x).Normalize();
        return new VCoordinateSystem(origin, x, y, z);
    }

    /// <summary>
    /// Creates a coordinate system at the specified origin with standard basis vectors.
    /// </summary>
    public static VCoordinateSystem ByOrigin(VXYZ origin)
    {
        return new VCoordinateSystem(origin, VXYZ.BasisX, VXYZ.BasisY, VXYZ.BasisZ);
    }

    /// <summary>
    /// Creates a coordinate system at the specified origin with standard basis vectors.
    /// </summary>
    public static VCoordinateSystem ByOrigin(double x, double y, double z)
    {
        return ByOrigin(new VXYZ(x, y, z));
    }

    /// <summary>
    /// Creates a coordinate system from a plane.
    /// </summary>
    public static VCoordinateSystem ByPlane(VPlane plane)
    {
        return new VCoordinateSystem(plane.Origin, plane.XVec, plane.YVec, plane.Normal);
    }

    /// <summary>
    /// Creates a coordinate system with Z-axis aligned to the given direction.
    /// </summary>
    public static VCoordinateSystem ByOriginZAxis(VXYZ origin, VXYZ zAxis)
    {
        var z = zAxis.Normalize();

        // Find a vector not parallel to Z to compute X
        var temp = Math.Abs(z.Z) < 0.9 ? VXYZ.BasisZ : VXYZ.BasisX;
        var x = temp.CrossProduct(z).Normalize();
        var y = z.CrossProduct(x).Normalize();

        return new VCoordinateSystem(origin, x, y, z);
    }

    /// <summary>
    /// Transforms a point from world coordinates to this coordinate system's local coordinates.
    /// </summary>
    public VXYZ ToLocal(VXYZ worldPoint)
    {
        var relative = worldPoint.Subtract(Origin);
        return new VXYZ(
            relative.DotProduct(XAxis),
            relative.DotProduct(YAxis),
            relative.DotProduct(ZAxis)
        );
    }

    /// <summary>
    /// Transforms a point from this coordinate system's local coordinates to world coordinates.
    /// </summary>
    public VXYZ ToWorld(VXYZ localPoint)
    {
        return Origin
            .Add(XAxis.Multiply(localPoint.X))
            .Add(YAxis.Multiply(localPoint.Y))
            .Add(ZAxis.Multiply(localPoint.Z));
    }

    /// <summary>
    /// Transforms a point from this coordinate system's local coordinates to world coordinates.
    /// </summary>
    public VXYZ ToWorld(double x, double y, double z)
    {
        return ToWorld(new VXYZ(x, y, z));
    }

    /// <summary>
    /// Returns a new coordinate system translated by the given vector.
    /// </summary>
    public VCoordinateSystem Translate(VXYZ translation)
    {
        return new VCoordinateSystem(Origin.Add(translation), XAxis, YAxis, ZAxis);
    }

    /// <summary>
    /// Returns a new coordinate system with its axes rotated around the given axis. The origin
    /// stays put. The angle is in <b>degrees</b>, like every other rotation in this library.
    /// </summary>
    public VCoordinateSystem Rotate(VXYZ axis, double angleDegrees)
    {
        // This used to call the radians overload with degrees, so Rotate(BasisZ, 90) turned by
        // 90 *radians* (~237 degrees) while the parameter name promised otherwise.
        var transform = VTransform.CreateRotationDegrees(axis, angleDegrees);
        return new VCoordinateSystem(
            Origin,
            transform.OfVector(XAxis),
            transform.OfVector(YAxis),
            transform.OfVector(ZAxis)
        );
    }

    public override string ToString()
    {
        return $"CoordinateSystem(Origin={Origin}, X={XAxis}, Y={YAxis}, Z={ZAxis})";
    }
}
