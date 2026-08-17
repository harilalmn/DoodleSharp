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
    /// Moves a shape so it renders above (after) another shape in the draw order.
    /// </summary>
    void MoveAbove(Shape shape, Shape referenceShape);

    /// <summary>
    /// Moves a shape so it renders behind (before) another shape in the draw order.
    /// </summary>
    void MoveBehind(Shape shape, Shape referenceShape);
}
