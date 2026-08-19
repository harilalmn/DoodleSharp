namespace C2VGeometry;

/// <summary>
/// Interface for optional shape registration with a canvas or rendering system.
/// Implement this interface to receive callbacks when shapes are created or removed.
/// </summary>
public interface IShapeRegistry
{
    /// <summary>
    /// Called when a new shape is created and should be registered.
    /// </summary>
    /// <param name="shape">The shape to register.</param>
    void Register(Shape shape);

    /// <summary>
    /// Called when a shape should be removed from the registry.
    /// </summary>
    /// <param name="shape">The shape to unregister.</param>
    void Unregister(Shape shape);

    /// <summary>
    /// Removes every registered shape.
    ///
    /// <para>
    /// <b>Geometry only.</b> This is what <c>Canvas.Clear()</c> calls, and it means exactly "take
    /// everything off the canvas" — it must not reset shape ids, stop a running timeline, or touch
    /// anything else belonging to the host's run lifecycle. A host that also needs those has its own
    /// entry point for them; <c>CanvasRenderer</c> keeps a separate public <c>Clear()</c> for the
    /// between-runs reset and implements this one explicitly.
    /// </para>
    /// </summary>
    void Clear();

    /// <summary>
    /// Called when a shape's <see cref="Shape.ZIndex"/> changes, so the host knows the draw order
    /// it is holding is stale and has to be re-derived before the next paint.
    ///
    /// <para>
    /// This replaced the old <c>MoveAbove</c>/<c>MoveBehind</c> pair. Those reordered the host's
    /// list directly, which meant the answer to "what is on top" depended on the order the calls
    /// happened to be made in and was undone by the next shape to be created. Order is now a
    /// property of the shape (<c>ZIndex</c>, ascending, creation order breaking ties) and the
    /// registry is merely told to re-sort.
    /// </para>
    /// </summary>
    void NotifyOrderChanged(Shape shape);

    /// <summary>
    /// Called by <see cref="Shape.Place(Viewport)"/>: registers the shape if it is not already, and
    /// assigns it to <paramref name="viewport"/>, moving it off whichever viewport it was on.
    ///
    /// <para>
    /// A shape auto-registers when it is constructed, long before any <c>Place(viewport)</c> call,
    /// so this is almost always a <b>move</b> rather than a first registration. That makes it a
    /// change to the shape <i>set</i> from each viewport's point of view — a host that caches
    /// per-viewport lists must drop them and bump whatever version counter its per-frame paths
    /// compare, or the shape will keep drawing in the cell it came from.
    /// </para>
    ///
    /// <para>
    /// A separate member rather than a second meaning for <see cref="NotifyOrderChanged"/>: that one
    /// names one precise question, and overloading it would let every existing implementation
    /// compile while silently dropping viewport assignment. A compile error in an implementer is the
    /// cheaper failure.
    /// </para>
    /// </summary>
    void Place(Shape shape, Viewport viewport);
}
