namespace C2VGeometry;

/// <summary>
/// Types of control points for shape editing.
/// </summary>
public enum ControlPointType
{
    /// <summary>Move the entire shape.</summary>
    Move,
    /// <summary>Vertex/endpoint of the shape.</summary>
    Vertex,
    /// <summary>Control point for radius/size.</summary>
    Radius,
    /// <summary>Control point for rotation.</summary>
    Rotation,
    /// <summary>Control point for curve control.</summary>
    CurveControl
}
