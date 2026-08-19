namespace C2VGeometry;

/// <summary>
/// Exists for one reason: to make <c>Viewports</c> usable as a bare name.
///
/// <para>
/// The intended spelling is <c>Viewports.Rows = 2</c> and <c>Place(Viewports[0][1])</c>, which
/// requires <c>Viewports</c> to be an <i>expression</i>. It cannot be a static class — C# has no
/// static indexers (CS0720, "cannot declare indexers in a static class") and no namespace-level
/// members, so a bare type name can never be indexed. A static <i>property</i> can, and
/// <c>using static</c> brings it into scope unqualified.
/// </para>
///
/// <para>
/// The host injects <c>global using static C2VGeometry.ViewportRoot;</c> as its own syntax tree
/// into every compilation, rather than putting the directive in the project templates: a template
/// only covers the files it generates, so a bare <c>Viewports</c> in a hand-written second file
/// would fail to compile. Being a separate tree, it shifts no offsets in the user's own files,
/// which is what keeps the offset-faithful editor paths correct.
/// </para>
/// </summary>
public static class ViewportRoot
{
    /// <summary>
    /// The whole drawing surface, undivided until code divides it. Split it with
    /// <c>Viewports.Rows</c> and <c>Viewports.Columns</c>, and reach a cell with
    /// <c>Viewports[row][column]</c> — 0-based, row first.
    /// </summary>
    /// <example>
    /// <code>
    /// Viewports.Rows = 2;
    /// Viewports.Columns = 3;
    ///
    /// new VCircle(new VXYZ(0, 0), 10).Place(Viewports[0][0]);
    /// new VLine(new VXYZ(0, 0), new VXYZ(10, 0)).Place(Viewports[1][2]);
    /// </code>
    /// </example>
    public static Viewport Viewports => Viewport.Root;
}
