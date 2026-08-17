namespace C2VGeometry;

/// <summary>
/// Angle conversions for <see cref="double"/>.
///
/// <para>
/// Every angle in this library is in degrees — <c>Shape.Rotate</c>, <c>VXYZ.Rotate</c>,
/// <c>VCoordinateSystem.Rotate</c>, <c>GeometryHelper.RotatePoint</c>, and the <c>VArc</c> /
/// <c>VEllipse</c> angle properties. <see cref="System.Math"/> works in radians. These two
/// extensions are for the boundary between them, so the conversion reads as what it is instead of
/// an unexplained <c>* Math.PI / 180.0</c>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// double y = 100 * System.Math.Sin(30.0.ToRadians());   // 50
/// double heading = System.Math.Atan2(dy, dx).ToDegrees();
/// var arc = new VArc(VXYZ.Zero, 50, 0, 90);             // library angles stay in degrees
/// </code>
/// </example>
public static class DoubleExtensions
{
    /// <summary>Converts an angle in degrees to radians, for handing to <see cref="System.Math"/>.</summary>
    public static double ToRadians(this double degrees) => degrees * System.Math.PI / 180.0;

    /// <summary>Converts an angle in radians — typically back from <see cref="System.Math"/> — to degrees.</summary>
    public static double ToDegrees(this double radians) => radians * 180.0 / System.Math.PI;
}
