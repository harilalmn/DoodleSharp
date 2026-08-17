namespace DoodleSharp.Animation;

/// <summary>
/// Decides whether a mouse event reaches user code, and whether the canvas's own gesture for that
/// event is suppressed.
///
/// <para>
/// This lives outside <c>RenderCanvas</c> on purpose. It is the part of the feature most likely to be
/// got wrong — and the part a unit test could not otherwise reach, because the handlers it would
/// otherwise live in need a real window and real input. Everything here is a pure function of a few
/// booleans, so <c>Tests/MouseCallbackTests.cs</c> can enumerate the whole truth table.
/// </para>
/// </summary>
internal static class MouseGate
{
    /// <summary>
    /// Whether user handlers should receive this event.
    ///
    /// <para>
    /// The two exclusions are the modal host tools: a drawing tool (P/L/C/R) or the measuring tape.
    /// Those are states the user deliberately armed with a shortcut, they own the click while armed,
    /// and user code cannot override them — letting a script break the line tool has no upside. They
    /// already return early in the canvas's handlers, so this is belt and braces.
    /// </para>
    ///
    /// <para>
    /// Note what is deliberately <i>not</i> excluded: selection. <c>IsSelectionMode</c> defaults to
    /// true and its branch consumes every left click, so gating on it would mean a click handler never
    /// fired in the default configuration — the single most likely "my handler doesn't work" report.
    /// In interactive mode selection is suppressed instead, which is what makes the click available.
    /// </para>
    ///
    /// <para>
    /// Panning is excluded because middle-drag stays the canvas's own gesture: it is the only way to
    /// pan, and handing it to a script would leave a drawing larger than the viewport unreachable.
    /// </para>
    /// </summary>
    /// <param name="interactive">Whether any user handler is registered (<c>Mouse.HasHandlers</c>).</param>
    /// <param name="panning">Whether a middle-button pan is in progress.</param>
    /// <param name="drawingToolActive">Whether a drawing tool is armed.</param>
    /// <param name="measuring">Whether the measuring tape is armed.</param>
    internal static bool Allow(bool interactive, bool panning, bool drawingToolActive, bool measuring)
        => interactive && !panning && !drawingToolActive && !measuring;

    /// <summary>
    /// Whether the canvas should suppress its own built-in gesture — click-to-select, wheel zoom,
    /// double-click zoom-to-fit — for this event.
    ///
    /// <para>
    /// Identical to <see cref="Allow"/> today, and kept as a separate name because the two answer
    /// different questions and are read at different points in the handlers: one asks "does user code
    /// hear about this?", the other "does the app still act on it?". Keeping them distinct is what
    /// stops a later change to one silently changing the other.
    /// </para>
    /// </summary>
    /// <param name="interactive">Whether any user handler is registered (<c>Mouse.HasHandlers</c>).</param>
    /// <param name="panning">Whether a middle-button pan is in progress.</param>
    /// <param name="drawingToolActive">Whether a drawing tool is armed.</param>
    /// <param name="measuring">Whether the measuring tape is armed.</param>
    internal static bool SuppressCanvasGesture(bool interactive, bool panning, bool drawingToolActive, bool measuring)
        => Allow(interactive, panning, drawingToolActive, measuring);
}
