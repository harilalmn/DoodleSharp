namespace C2VGeometry;

/// <summary>
/// Interface for shapes that can be drawn/rendered.
/// </summary>
public interface IDrawable
{
    /// <summary>
    /// Puts the shape on the canvas and keeps it there — see <c>Shape.Place</c>.
    ///
    /// <para>
    /// Declared here so the recommended name is reachable through an <see cref="IDrawable"/> or
    /// <c>ICurve</c> reference, which is what <c>CanvasRenderer.GetShapes()</c> hands back. Without
    /// it, code holding the interface could only call <see cref="Draw"/>, and "prefer Place()"
    /// would fail to compile in exactly the place the docs send people.
    /// </para>
    /// </summary>
    void Place() => Draw();

    /// <summary>
    /// The historical name for <see cref="Place"/>, and exactly equivalent to it. Registers the
    /// shape for rendering if a registry is set.
    /// </summary>
    void Draw();

    /// <summary>
    /// The stroke color name (e.g., "Cyan", "Red", "#FF0000").
    /// </summary>
    string Color { get; set; }

    /// <summary>
    /// The fill color name (e.g., "Transparent", "Blue").
    /// </summary>
    string FillColor { get; set; }

    /// <summary>
    /// The stroke thickness in pixels.
    /// </summary>
    double LineWeight { get; set; }

    /// <summary>
    /// The line pattern style (solid, dashed, dotted, etc.).
    /// </summary>
    LineType LineType { get; set; }

    /// <summary>
    /// Scale factor for stroke pattern (dash/gap lengths). Default is 1.0.
    /// Values greater than 1.0 create longer dashes/gaps, less than 1.0 create shorter ones.
    /// </summary>
    double LineTypeScale { get; set; }
}
