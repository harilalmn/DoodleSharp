namespace C2VGeometry;

/// <summary>
/// Defines the stroke style (line pattern) for shape outlines.
/// </summary>
public enum LineType
{
    /// <summary>Solid continuous line (default).</summary>
    Continuous,
    /// <summary>Dashed line pattern (long dashes).</summary>
    Dashed,
    /// <summary>Dotted line pattern (short dots).</summary>
    Dotted,
    /// <summary>Alternating dash and dot pattern.</summary>
    DashDot,
    /// <summary>Alternating dash and two dots pattern.</summary>
    DashDotDot,
    /// <summary>Center line pattern (long-short-long).</summary>
    Center,
    /// <summary>Phantom line pattern (long-short-short).</summary>
    Phantom,
    /// <summary>Hidden line pattern (short dashes).</summary>
    Hidden
}
