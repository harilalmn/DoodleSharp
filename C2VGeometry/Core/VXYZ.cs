using System;

namespace C2VGeometry;

/// <summary>
/// Object representing coordinates in 3-dimensional space.
/// Mimics Autodesk.Revit.DB.XYZ.
/// </summary>
public class VXYZ
{
    // Immutable coordinates
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public VXYZ(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public VXYZ(double x, double y) : this(x, y, 0) { }

    public VXYZ() : this(0, 0, 0) { }

    // Static properties
    public static VXYZ Zero { get; } = new VXYZ(0, 0, 0);
    public static VXYZ BasisX { get; } = new VXYZ(1, 0, 0);
    public static VXYZ BasisY { get; } = new VXYZ(0, 1, 0);
    public static VXYZ BasisZ { get; } = new VXYZ(0, 0, 1);

    // Indexer
    public double this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return X;
                case 1: return Y;
                case 2: return Z;
                default: throw new IndexOutOfRangeException("Index must be between 0 and 2");
            }
        }
    }

    // Methods
    public VXYZ Add(VXYZ source) => new VXYZ(X + source.X, Y + source.Y, Z + source.Z);
    public VXYZ Subtract(VXYZ source) => new VXYZ(X - source.X, Y - source.Y, Z - source.Z);
    public VXYZ Multiply(double value) => new VXYZ(X * value, Y * value, Z * value);
    public VXYZ Divide(double value) => new VXYZ(X / value, Y / value, Z / value);
    public VXYZ Negate() => new VXYZ(-X, -Y, -Z);

    /// <summary>
    /// Creates a deep copy of this VXYZ.
    /// Subclasses can override with covariant return type.
    /// </summary>
    public virtual VXYZ Clone() => new VXYZ(X, Y, Z);

    /// <summary>
    /// Converts this VXYZ to a <see cref="VPoint"/> (drops the Z component).
    ///
    /// <para>
    /// <b>This draws a point marker on the canvas.</b> <c>VPoint</c> is a drawable shape and
    /// auto-registers on construction, so call this only when a visible marker is what you want —
    /// for intermediate coordinates, keep using <c>VXYZ</c>, which never registers.
    /// </para>
    /// </summary>
    // The previous comment here claimed this "uses Internal() to avoid auto-registering
    // intermediate points". It never did, and believing it is how phantom shapes end up on the
    // canvas (CLAUDE.md notes 6, 10 and 27).
    public VPoint AsVPoint() => new VPoint(X, Y);

    public double GetLength() => Math.Sqrt(X * X + Y * Y + Z * Z);

    public VXYZ Normalize()
    {
        double len = GetLength();
        if (IsZero(len)) return Zero; // Handle zero vector gracefully or throw? Revit throws, but safe is better here.
        return Divide(len);
    }

    public double DistanceTo(VXYZ source)
    {
        return Subtract(source).GetLength();
    }

    public double DotProduct(VXYZ source) => X * source.X + Y * source.Y + Z * source.Z;

    public VXYZ CrossProduct(VXYZ source)
    {
        return new VXYZ(
            Y * source.Z - Z * source.Y,
            Z * source.X - X * source.Z,
            X * source.Y - Y * source.X
        );
    }

    public double TripleProduct(VXYZ a, VXYZ b)
    {
        return DotProduct(a.CrossProduct(b));
    }

    /// <summary>
    /// The unsigned angle between this vector and <paramref name="source"/>, <b>in radians</b>
    /// (0 to π). Returns 0 when either vector has zero length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one query in the library that answers in radians, and it is the odd one out:
    /// <c>Shape.Rotate</c>, <c>VXYZ.Rotate</c>, <c>VText.Angle</c>, <c>VArc</c>'s angles and
    /// everything else take <b>degrees</b>. Feeding this straight into one of them is silently
    /// wrong rather than obviously wrong — a direction reversed along X answers π, which lands as a
    /// 3.14° tilt instead of a 180° turn, and only shows up as a label that looks very slightly
    /// crooked. Use <see cref="AngleToDegrees"/> for the library's convention.
    /// </para>
    /// <para>
    /// It is also <b>unsigned</b>, so it cannot tell a direction turned 45° up from one turned 45°
    /// down. To orient something along a 2D direction, use
    /// <c>Math.Atan2(dir.Y, dir.X).ToDegrees()</c>.
    /// </para>
    /// </remarks>
    public double AngleToRadians(VXYZ source)
    {
        double dot = DotProduct(source);
        double lenProd = GetLength() * source.GetLength();
        if (IsZero(lenProd)) return 0;

        double cos = dot / lenProd;
        // Clamp to [-1, 1] to handle precision errors
        if (cos > 1.0) cos = 1.0;
        if (cos < -1.0) cos = -1.0;

        return Math.Acos(cos);
    }

    /// <summary>
    /// The unsigned angle between this vector and <paramref name="source"/>, <b>in degrees</b>
    /// (0 to 180) — the library's convention, so the result can be handed straight to
    /// <c>Rotate</c>, <c>VText.Angle</c> and the rest. Returns 0 when either vector has zero length.
    /// </summary>
    /// <remarks>
    /// Unsigned: a direction 45° above the X axis and one 45° below both answer 45. To orient
    /// something along a 2D direction, where the sign matters, use
    /// <c>Math.Atan2(dir.Y, dir.X).ToDegrees()</c>.
    /// </remarks>
    public double AngleToDegrees(VXYZ source) => AngleToRadians(source).ToDegrees();

    /// <summary>
    /// The unsigned angle between this vector and <paramref name="source"/>, in <b>radians</b>.
    /// </summary>
    /// <remarks>
    /// Deprecated because the name says nothing about its unit while every rotation API in the
    /// library takes degrees, and the mismatch is invisible: <c>text.Angle = dir.AngleTo(BasisX)</c>
    /// on a direction pointing along −X assigns π as 3.14 <i>degrees</i>, which reads as a label
    /// that is very slightly crooked rather than as a wrong answer. Behaviour is unchanged — this
    /// forwards to <see cref="AngleToRadians"/>; only the name is retired, exactly as
    /// <c>VTransform.CreateRotation</c> was.
    /// </remarks>
    [Obsolete("Ambiguous name: this returns RADIANS while the rest of the library works in degrees. " +
              "Use AngleToDegrees for the library's convention, or AngleToRadians if you want radians. " +
              "Behaviour is unchanged.")]
    public double AngleTo(VXYZ source) => AngleToRadians(source);

    // Methods for implementation consistency
    public bool IsZeroLength() => IsZero(GetLength());
    public bool IsUnitLength() => IsZero(GetLength() - 1.0);

    /// <summary>
    /// Returns a new VXYZ rotated around the Z-axis by the given angle in degrees.
    /// </summary>
    public VXYZ Rotate(double angleInDegrees)
    {
        double rad = angleInDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new VXYZ(X * cos - Y * sin, X * sin + Y * cos, Z);
    }

    public bool IsAlmostEqualTo(VXYZ source, double tolerance = GeometryTolerance.Epsilon)
    {
        return GeometryTolerance.AreEqual(X, source.X, tolerance) &&
               GeometryTolerance.AreEqual(Y, source.Y, tolerance) &&
               GeometryTolerance.AreEqual(Z, source.Z, tolerance);
    }

    public static bool IsWithinLengthLimits(VXYZ point)
    {
        // Revit limits are approx +/- 30,000 ft (~9km). Let's say +/- 1e5 for general use.
        return Math.Abs(point.X) < 1e5 && Math.Abs(point.Y) < 1e5 && Math.Abs(point.Z) < 1e5;
    }

    // Operators
    public static VXYZ operator +(VXYZ a, VXYZ b) => a.Add(b);
    public static VXYZ operator -(VXYZ a, VXYZ b) => a.Subtract(b);
    public static VXYZ operator *(VXYZ a, double b) => a.Multiply(b);
    public static VXYZ operator *(double a, VXYZ b) => b.Multiply(a);
    public static VXYZ operator /(VXYZ a, double b) => a.Divide(b);
    public static VXYZ operator -(VXYZ a) => a.Negate();

    // Equality operators using fuzzy logic
    public static bool operator ==(VXYZ? a, VXYZ? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.IsAlmostEqualTo(b);
    }

    public static bool operator !=(VXYZ? a, VXYZ? b) => !(a == b);

    public override bool Equals(object? obj)
    {
        if (obj is VXYZ v) return this == v;
        return false;
    }

    public override int GetHashCode()
    {
        // Round to a precision consistent with IsAlmostEqualTo (approx 9 decimal places)
        // This is "lossy" but allows the fuzzy equality to work better in collections.
        // Using logic: round to 8 decimals for hashing to group "close" values.
        return HashCode.Combine(Math.Round(X, 8), Math.Round(Y, 8), Math.Round(Z, 8));
    }

    public override string ToString()
    {
        return $"({X:F9}, {Y:F9}, {Z:F9})";
    }

    private static bool IsZero(double val) => GeometryTolerance.IsZero(val);
}
