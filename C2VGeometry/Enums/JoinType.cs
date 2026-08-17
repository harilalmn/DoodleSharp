namespace C2VGeometry;

/// <summary>
/// Specifies how polygon corners are joined during offset operations.
/// </summary>
public enum JoinType
{
    /// <summary>
    /// Extends edges until they meet at a point. Uses miter limit to prevent
    /// excessively long miters at sharp corners (default limit is 2.0).
    /// </summary>
    Miter,

    /// <summary>
    /// Rounds corners with a circular arc.
    /// </summary>
    Round,

    /// <summary>
    /// Creates a squared-off corner perpendicular to the bisector direction.
    /// </summary>
    Square
}
