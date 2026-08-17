namespace C2VGeometry;

/// <summary>
/// Where the geometry library reports something the caller should know about but that is not
/// exceptional — a boolean operation that could not produce a single polygon, for instance.
///
/// <para>
/// C2VGeometry is UI-free, so it cannot write to the app's console directly. It used to call
/// <c>System.Console.WriteLine</c> instead, which in a WPF process goes nowhere at all: the
/// explanation for a null result was written to a stream nobody reads, and the user just saw null.
/// The host sets <see cref="Sink"/> once at start-up — the same seam as
/// <see cref="Shape.DefaultRegistry"/> and <see cref="VText.GlyphOutlineProvider"/>.
/// </para>
/// </summary>
public static class GeometryDiagnostics
{
    /// <summary>
    /// Receives diagnostic messages. Null (the default) discards them, so a library consumer with no
    /// console pays nothing.
    /// </summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Reports a message, if anyone is listening. Never throws.</summary>
    public static void Report(string message)
    {
        var sink = Sink;
        if (sink == null) return;

        try { sink(message); }
        catch { /* a broken diagnostics sink must not break the geometry operation */ }
    }
}
