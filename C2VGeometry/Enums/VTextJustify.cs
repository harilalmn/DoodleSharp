namespace C2VGeometry;

/// <summary>
/// How the lines of a multi-line <see cref="VText"/> line up with each other inside the text block.
///
/// <para>
/// <b>This is not <see cref="VTextAnchor"/>, and the two compose.</b> The anchor places the block
/// as a whole against <see cref="VText.Location"/> — it decides where the block's corner or centre
/// sits. The justification decides what the ragged edge inside the block looks like once it is
/// there. A four-line label with <c>Anchor = MiddleCenter</c> is centred on its point either way;
/// with <c>Justify = Center</c> its short lines are also centred against its long ones instead of
/// hanging off to the left.
/// </para>
///
/// <para>
/// It has no visible effect on single-line text, where the block is exactly as wide as its one
/// line and every justification puts that line in the same place.
/// </para>
/// </summary>
public enum VTextJustify
{
    /// <summary>Lines share a left edge; the ragged edge is on the right. The default.</summary>
    Left,
    /// <summary>Lines are centred on the block's vertical midline; both edges are ragged.</summary>
    Center,
    /// <summary>Lines share a right edge; the ragged edge is on the left.</summary>
    Right
}
