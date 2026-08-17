namespace C2VGeometry;

/// <summary>
/// Specifies how path ends are treated during offset operations.
/// </summary>
public enum EndType
{
    /// <summary>
    /// Path is treated as a closed polygon. Ends are connected.
    /// </summary>
    Polygon,

    /// <summary>
    /// Path ends are rounded with a semicircular arc.
    /// </summary>
    OpenRound,

    /// <summary>
    /// Path ends are squared off perpendicular to the path direction.
    /// </summary>
    OpenSquare,

    /// <summary>
    /// Path ends are cut off flat at the exact endpoint (no extension).
    /// </summary>
    OpenButt
}
