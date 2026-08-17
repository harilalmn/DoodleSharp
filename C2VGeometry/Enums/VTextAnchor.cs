namespace C2VGeometry;

/// <summary>
/// Specifies the anchor point of text relative to its location.
/// </summary>
public enum VTextAnchor
{
    /// <summary>Text is anchored at the bottom-left (default). Location is the bottom-left corner.</summary>
    BottomLeft,
    /// <summary>Text is anchored at the bottom-center. Location is the bottom-center point.</summary>
    BottomCenter,
    /// <summary>Text is anchored at the bottom-right. Location is the bottom-right corner.</summary>
    BottomRight,
    /// <summary>Text is anchored at the middle-left. Location is the left-center point.</summary>
    MiddleLeft,
    /// <summary>Text is anchored at the middle-center. Location is the center point.</summary>
    MiddleCenter,
    /// <summary>Text is anchored at the middle-right. Location is the right-center point.</summary>
    MiddleRight,
    /// <summary>Text is anchored at the top-left. Location is the top-left corner.</summary>
    TopLeft,
    /// <summary>Text is anchored at the top-center. Location is the top-center point.</summary>
    TopCenter,
    /// <summary>Text is anchored at the top-right. Location is the top-right corner.</summary>
    TopRight
}
