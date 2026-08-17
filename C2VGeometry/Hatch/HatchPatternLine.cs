namespace C2VGeometry;

/// <summary>
/// Represents a single line definition within a hatch pattern.
/// Follows the AutoCAD .pat format:
/// angle, x-origin, y-origin, delta-x, delta-y [, dash1, dash2, ...]
/// </summary>
public class HatchPatternLine
{
    /// <summary>Angle of the line family in degrees.</summary>
    public double Angle { get; set; }

    /// <summary>X coordinate of the line origin.</summary>
    public double OriginX { get; set; }

    /// <summary>Y coordinate of the line origin.</summary>
    public double OriginY { get; set; }

    /// <summary>Delta X offset between successive parallel lines (along the line direction).</summary>
    public double DeltaX { get; set; }

    /// <summary>Delta Y offset between successive parallel lines (perpendicular to line direction).</summary>
    public double DeltaY { get; set; }

    /// <summary>
    /// Dash pattern. Positive = dash length, negative = gap length, 0 = dot.
    /// Empty array means continuous line.
    /// </summary>
    public double[] Dashes { get; set; } = [];

    public HatchPatternLine() { }

    public HatchPatternLine(double angle, double originX, double originY, double deltaX, double deltaY, params double[] dashes)
    {
        Angle = angle;
        OriginX = originX;
        OriginY = originY;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Dashes = dashes;
    }

    /// <summary>Deep copy, including the dash array.</summary>
    public HatchPatternLine Clone() =>
        new HatchPatternLine(Angle, OriginX, OriginY, DeltaX, DeltaY, (double[])Dashes.Clone());
}
