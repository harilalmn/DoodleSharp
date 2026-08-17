using System.Collections.Generic;

namespace C2VGeometry;

/// <summary>
/// Supplies vector outlines (contours) for the glyphs of a <see cref="VText"/>.
/// </summary>
/// <remarks>
/// C2VGeometry is intentionally WPF-free, so it cannot rasterize fonts itself.
/// The host application (which has platform font APIs) implements this and assigns it to
/// <see cref="VText.GlyphOutlineProvider"/> at startup — the same injection pattern as
/// <see cref="Shape.DefaultRegistry"/>. With no provider set, glyph-to-shape conversion
/// (e.g. <see cref="VText.ToCharShape"/>) simply returns null.
/// </remarks>
public interface IGlyphOutlineProvider
{
    /// <summary>
    /// Returns the glyph contours for the character at <paramref name="charIndex"/> of
    /// <paramref name="text"/>, expressed in world coordinates that match where the
    /// character is currently rendered (honoring font, height, anchor, and rotation).
    /// Each inner list is one closed contour — the outer outline plus any holes
    /// (e.g. the bowl of an 'O' or the counters of 'A'/'B').
    /// Returns null when the character has no outline (whitespace) or no data is available.
    /// </summary>
    List<List<VXYZ>>? GetCharContours(VText text, int charIndex);
}
