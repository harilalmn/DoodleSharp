using System;
using System.Collections.Generic;

namespace C2VGeometry;

/// <summary>
/// A hatch fill shape that applies a pattern within a closed boundary.
/// The boundary is defined by a polygon (list of points).
/// The pattern is defined by a HatchType.
/// </summary>
public class VHatch : Shape
{
    private List<VXYZ> _boundary;
    private HatchType _pattern;
    private double _patternScale;
    private double _patternAngle;

    /// <summary>The closed boundary polygon points.</summary>
    public List<VXYZ> Boundary
    {
        get => _boundary;
        set => _boundary = value ?? new List<VXYZ>();
    }

    /// <summary>The hatch pattern definition.</summary>
    public HatchType Pattern
    {
        get => _pattern;
        set => _pattern = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Scale factor applied to the pattern. Default 1.0.</summary>
    public double PatternScale
    {
        get => _patternScale;
        set => _patternScale = value;
    }

    /// <summary>Additional rotation angle in degrees applied to the entire pattern. Default 0.</summary>
    public double PatternAngle
    {
        get => _patternAngle;
        set => _patternAngle = value;
    }

    /// <summary>
    /// Creates a hatch from a built-in pattern enum applied to a polygon boundary.
    /// </summary>
    public VHatch(VPolygon boundary, BuiltInHatch pattern, double scale = 1.0, double angle = 0.0)
        : this(boundary.Points.ToList(), HatchType.GetBuiltIn(pattern), scale, angle) { }

    /// <summary>
    /// Creates a hatch from a built-in pattern name applied to a polygon boundary.
    /// </summary>
    public VHatch(VPolygon boundary, string patternName, double scale = 1.0, double angle = 0.0)
        : this(boundary.Points.ToList(), HatchType.GetBuiltIn(patternName), scale, angle) { }

    /// <summary>
    /// Creates a hatch from a HatchType applied to a polygon boundary.
    /// </summary>
    public VHatch(VPolygon boundary, HatchType pattern, double scale = 1.0, double angle = 0.0)
        : this(boundary.Points.ToList(), pattern, scale, angle) { }

    /// <summary>
    /// Creates a hatch from a built-in pattern enum applied to boundary points.
    /// </summary>
    public VHatch(List<VXYZ> boundary, BuiltInHatch pattern, double scale = 1.0, double angle = 0.0)
        : this(boundary, HatchType.GetBuiltIn(pattern), scale, angle) { }

    /// <summary>
    /// Creates a hatch from a built-in pattern name applied to boundary points.
    /// </summary>
    public VHatch(List<VXYZ> boundary, string patternName, double scale = 1.0, double angle = 0.0)
        : this(boundary, HatchType.GetBuiltIn(patternName), scale, angle) { }

    /// <summary>
    /// Creates a hatch from a custom HatchType applied to boundary points.
    /// </summary>
    public VHatch(List<VXYZ> boundary, HatchType pattern, double scale = 1.0, double angle = 0.0)
    {
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        _pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        _patternScale = scale;
        _patternAngle = angle;
        Color = ShapeDefaults.GlobalColor ?? "Cyan";
        LineWeight = 1.0;
    }

    /// <summary>
    /// Creates a hatch from a custom pattern definition string (AutoCAD .pat format).
    /// </summary>
    public VHatch(VPolygon boundary, HatchType pattern, double scale, double angle, bool _)
        : this(boundary.Points.ToList(), pattern, scale, angle) { }

    /// <summary>
    /// Creates a hatch using a custom pattern definition string in AutoCAD .pat format.
    /// </summary>
    /// <example>
    /// var hatch = VHatch.FromDefinition(polygon, @"
    ///   *CUSTOM, My custom pattern
    ///   45, 0,0, 0,10
    ///   135, 0,0, 0,10
    /// ", scale: 1.0);
    /// </example>
    public static VHatch FromDefinition(VPolygon boundary, string patDefinition, double scale = 1.0, double angle = 0.0)
    {
        var pattern = HatchType.Parse(patDefinition);
        return new VHatch(boundary.Points.ToList(), pattern, scale, angle);
    }

    /// <summary>
    /// Creates a hatch using a custom pattern definition string applied to boundary points.
    /// </summary>
    public static VHatch FromDefinition(List<VXYZ> boundary, string patDefinition, double scale = 1.0, double angle = 0.0)
    {
        var pattern = HatchType.Parse(patDefinition);
        return new VHatch(boundary, pattern, scale, angle);
    }

    /// <summary>
    /// Generates the hatch line segments clipped to the boundary.
    /// Returns a list of line segments as (start, end) point pairs.
    /// </summary>
    public List<(VXYZ Start, VXYZ End)> GenerateLines()
    {
        return HatchGenerator.Generate(_boundary, _pattern, _patternScale, _patternAngle);
    }

    #region Shape overrides

    public override Shape Clone()
    {
        var clonedBoundary = _boundary.Select(pt => pt.Clone()).ToList();
        var clone = new VHatch(clonedBoundary, _pattern, _patternScale, _patternAngle);
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        for (int i = 0; i < _boundary.Count; i++)
            _boundary[i] = _boundary[i] + vector;
    }

    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        for (int i = 0; i < _boundary.Count; i++)
            _boundary[i] = GeometryHelper.RotatePoint(_boundary[i], pivot, angleDegrees);
        _patternAngle += angleDegrees;
    }

    public override void Flip(VLine mirrorLine)
    {
        for (int i = 0; i < _boundary.Count; i++)
            _boundary[i] = GeometryHelper.FlipPoint(_boundary[i], mirrorLine);
    }

    public override void Scale(VXYZ center, double factor)
    {
        for (int i = 0; i < _boundary.Count; i++)
            _boundary[i] = GeometryHelper.ScalePoint(_boundary[i], center, factor);
        _patternScale *= Math.Abs(factor);
    }

    public override BoundingBox GetBounds()
    {
        if (_boundary.Count == 0)
            return new BoundingBox(new VXYZ(0, 0), new VXYZ(0, 0));

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var pt in _boundary)
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y > maxY) maxY = pt.Y;
        }

        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }

    public override List<ControlPoint> GetControlPoints()
    {
        var bounds = GetBounds();
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move,
                (bounds.Min.X + bounds.Max.X) / 2,
                (bounds.Min.Y + bounds.Max.Y) / 2,
                "Center")
        };
    }

    /// <summary>
    /// Whether <paramref name="point"/> lies inside the hatch boundary.
    /// </summary>
    /// <remarks>
    /// Uses the same <see cref="PolygonClipper.PointInPolygonTest"/> as every other area shape.
    /// This used to carry its own private crossing-number copy — equivalent for interior points, but
    /// a second implementation to keep correct, and one that could disagree on boundary-exact points.
    /// </remarks>
    public override bool Contains(VXYZ point)
    {
        return PolygonClipper.PointInPolygonTest(point, _boundary);
    }

    #endregion

    public override string ToString() => $"VHatch({_pattern.Name}, Scale:{_patternScale}, Angle:{_patternAngle})";

    /// <summary>
    /// Shortest distance from <paramref name="point"/> to the hatch boundary. Zero on the outline,
    /// positive both inside and outside. <see cref="Contains"/> already tested the interior exactly;
    /// this used to fall through to the bounding-box centre.
    /// </summary>
    public override double DistanceTo(VXYZ point) =>
        CurveGeometry.DistanceToPath(point, _boundary, closed: true);
}
