using System;

namespace C2VGeometry;

public class VTransform
{
    // 4x4 Matrix representation or similar logic
    // For this limited implementation, we can store BasisX, BasisY, BasisZ and Origin

    public VXYZ BasisX { get; set; }
    public VXYZ BasisY { get; set; }
    public VXYZ BasisZ { get; set; }
    public VXYZ Origin { get; set; }

    public VTransform()
    {
        BasisX = VXYZ.BasisX;
        BasisY = VXYZ.BasisY;
        BasisZ = VXYZ.BasisZ;
        Origin = VXYZ.Zero;
    }

    public static VTransform Identity => new VTransform();

    /// <summary>
    /// Builds a rotation about an arbitrary axis (Rodrigues' formula), from an angle in
    /// <b>radians</b>. The axis is normalised for you and <see cref="Origin"/> is left at zero.
    ///
    /// <para>
    /// Everything else in this library takes degrees — <c>Shape.Rotate</c>, <c>VXYZ.Rotate</c>,
    /// <c>VCoordinateSystem.Rotate</c>, <c>GeometryHelper.RotatePoint</c>. Use
    /// <see cref="CreateRotationDegrees"/> unless you already hold radians.
    /// </para>
    /// </summary>
    public static VTransform CreateRotationRadians(VXYZ axis, double angleRadians)
    {
        // Rodrigues' rotation formula or constructing a rotation matrix
        // axis should be normalized
        axis = axis.Normalize();
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);
        double oneMinusCos = 1.0 - cos;

        double x = axis.X, y = axis.Y, z = axis.Z;

        // Column 0 (BasisX)
        double r00 = cos + x * x * oneMinusCos;
        double r10 = y * x * oneMinusCos + z * sin;
        double r20 = z * x * oneMinusCos - y * sin;

        // Column 1 (BasisY)
        double r01 = x * y * oneMinusCos - z * sin;
        double r11 = cos + y * y * oneMinusCos;
        double r21 = z * y * oneMinusCos + x * sin;

        // Column 2 (BasisZ)
        double r02 = x * z * oneMinusCos + y * sin;
        double r12 = y * z * oneMinusCos - x * sin;
        double r22 = cos + z * z * oneMinusCos;

        VTransform t = new VTransform();
        t.BasisX = new VXYZ(r00, r10, r20);
        t.BasisY = new VXYZ(r01, r11, r21);
        t.BasisZ = new VXYZ(r02, r12, r22);
        t.Origin = VXYZ.Zero;
        return t;
    }

    /// <summary>
    /// Same as <see cref="CreateRotationRadians"/>, but the angle is in degrees — matching the
    /// convention every other rotation in this library uses. Prefer this one.
    /// </summary>
    public static VTransform CreateRotationDegrees(VXYZ axis, double angleDegrees)
        => CreateRotationRadians(axis, angleDegrees * Math.PI / 180.0);

    /// <summary>
    /// The original name for <see cref="CreateRotationRadians"/>, kept working unchanged.
    ///
    /// <para>
    /// Deprecated because the name says nothing about its units while every neighbouring API is in
    /// degrees — an ambiguity that already produced one shipped bug, where
    /// <c>VCoordinateSystem.Rotate</c> forwarded degrees into it and turned a documented quarter
    /// turn into roughly 237°. The name is retired rather than redefined: making it mean degrees
    /// would leave existing calls compiling and silently rotating by 1/57th of what they used to.
    /// </para>
    /// </summary>
    [Obsolete("Ambiguous name: this takes RADIANS while the rest of the library takes degrees. " +
              "Use CreateRotationDegrees for the usual convention, or CreateRotationRadians if you " +
              "already hold radians. Behaviour is unchanged.")]
    public static VTransform CreateRotation(VXYZ axis, double angleRadians)
        => CreateRotationRadians(axis, angleRadians);

    public static VTransform CreateReflection(VPlane plane)
    {
        // Reflection matrix across a plane passing through Origin with normal N:
        // R = I - 2 * N * N^T
        // If plane has an origin P, the transformation for a point X is:
        // X' = R(X - P) + P
        // This suggests a general affine transform is needed.
        // But Revit's CreateReflection creates a transform where:
        // Basis vectors are reflected, and Origin logic handles the plane position.

        // Let's implement full transform logic.
        // X' = M * X + T

        // For reflection:
        // V' = V - 2(V.N)N (for vectors)
        // P' = P - 2((P-PlaneOrigin).N)N (for points)

        VXYZ n = plane.Normal;

        // Basis Reflection
        VXYZ bx = VXYZ.BasisX - n.Multiply(2 * VXYZ.BasisX.DotProduct(n));
        VXYZ by = VXYZ.BasisY - n.Multiply(2 * VXYZ.BasisY.DotProduct(n));
        VXYZ bz = VXYZ.BasisZ - n.Multiply(2 * VXYZ.BasisZ.DotProduct(n));

        VTransform t = new VTransform();
        t.BasisX = bx;
        t.BasisY = by;
        t.BasisZ = bz;

        // Origin Reflection:
        // 0' = 0 - 2((0 - PlaneOrigin).N)N
        //    = -2((-PlaneOrigin).N)N
        //    = 2(PlaneOrigin.N)N
        t.Origin = n.Multiply(2 * plane.Origin.DotProduct(n));

        return t;
    }

    public VXYZ OfVector(VXYZ vec)
    {
        // Linear transformation only (ignoring translation)
        // V' = x*BasisX + y*BasisY + z*BasisZ
        return BasisX.Multiply(vec.X)
            .Add(BasisY.Multiply(vec.Y))
            .Add(BasisZ.Multiply(vec.Z));
    }

    public VXYZ OfPoint(VXYZ point)
    {
        // Affine transformation
        // P' = M*P + Origin
        return OfVector(point).Add(Origin);
    }
}
