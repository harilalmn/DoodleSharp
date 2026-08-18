using System.Collections.Generic;
using System.Linq;

namespace C2VGeometry;

/// <summary>
/// The drawing surface, as user code sees it.
///
/// <para>
/// Shapes register themselves as they are constructed, so most sketches never need this. It exists
/// for the case that had no answer before: a callback that redraws, and therefore needs to take the
/// previous frame's shapes back off. <c>Frame.Clear()</c> reads like it should do that and does not —
/// it drops queued per-frame callbacks — and the only real option was to fully qualify an
/// app-internal type, which is not something a user should have to discover.
/// </para>
///
/// <para>
/// Everything here goes through <see cref="Shape.DefaultRegistry"/>, so it is null-safe: with no
/// registry attached — a unit test, or a headless host — these are no-ops rather than exceptions.
/// </para>
/// </summary>
public static class Canvas
{
    /// <summary>
    /// Removes every shape from the canvas.
    ///
    /// <para>
    /// Geometry only. It does <b>not</b> rewind shape ids, stop a running timeline, or reset the
    /// view — those belong to the host's between-runs reset, and having them fire from inside a
    /// mouse handler would be a genuinely nasty surprise.
    /// </para>
    ///
    /// <para>
    /// Usually you do not want this. Creating a fresh shape per mouse move and clearing the canvas
    /// each time allocates a whole scene per event; building the shapes once in <c>Main</c> and
    /// assigning their positions in the handler is both simpler and much faster. Reach for
    /// <c>Clear</c> when the *set* of shapes changes, not merely their positions.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // Rebuild a scene whose shape count varies with the cursor.
    /// Mouse.OnMove(e =>
    /// {
    ///     Canvas.Clear();
    ///     var rings = (int)(e.X / 40);
    ///     for (var i = 1; i &lt;= rings; i++)
    ///         new VCircle(new VXYZ(0, 0), i * 20) { Color = "Cyan" };
    /// });
    /// </code>
    /// </example>
    /// </summary>
    public static void Clear() => Shape.DefaultRegistry?.Clear();

    /// <summary>
    /// Removes the given shapes. Nulls are skipped, and a shape that is not on the canvas is
    /// ignored, so this is safe to call with a list you are also rebuilding.
    /// </summary>
    public static void Remove(params Shape[] shapes)
    {
        if (shapes == null) return;

        foreach (var shape in shapes)
            shape?.Remove();
    }

    /// <summary>Removes the given shapes.</summary>
    public static void Remove(IEnumerable<Shape> shapes)
    {
        if (shapes == null) return;

        // Materialised first: Remove mutates the registry, and callers reasonably pass a live view
        // of it — GetShapes() is the obvious argument — which would otherwise throw mid-iteration.
        foreach (var shape in shapes.ToList())
            shape?.Remove();
    }
}
