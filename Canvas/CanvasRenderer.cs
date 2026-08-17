using C2VGeometry;
using DoodleSharp.Animation;
using DoodleSharp.Services;

namespace DoodleSharp.Canvas;

public class CanvasRenderer : ICanvasRenderer, C2VGeometry.IShapeRegistry
{
    private static CanvasRenderer? _instance;
    private static readonly object _lock = new();

    private readonly List<C2VGeometry.IDrawable> _shapes = new();

    /// <summary>
    /// The currently active timeline for animation playback.
    /// Internal use only - users should use the Animator class.
    /// </summary>
    internal Timeline? ActiveTimeline { get; set; }

    /// <summary>
    /// Bumped whenever the shape *set* changes (add, remove, reorder) — not when an existing shape
    /// is merely mutated.
    ///
    /// <para>
    /// This exists because <see cref="RenderCanvas"/> does not share this list: it keeps its own
    /// <c>_currentShapes</c> snapshot, assigned only by <c>Render()</c> and <c>SetFrameShapes()</c>.
    /// So a per-frame path that calls <c>Refresh()</c> repaints the snapshot and a shape *created*
    /// after the run silently never appears, while a shape *mutated* in place appears fine. The
    /// per-frame paths compare this counter to decide between the cheap
    /// <c>ReindexForAnimationFrame()</c> (boxes went stale) and the full <c>SetFrameShapes()</c>
    /// (the set itself changed, so the snapshot has to be retaken).
    /// </para>
    /// </summary>
    internal int RegistryVersion { get; private set; }

    public static CanvasRenderer Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new CanvasRenderer();
                        // App code (`new VCircle(...)`) auto-registers onto the canvas.
                        C2VGeometry.Shape.DefaultRegistry = _instance;
                        // Enable VText glyph→shape extraction (text[0], ToCharShape, …).
                        C2VGeometry.VText.GlyphOutlineProvider = new GlyphOutlineProvider();
                        // Give the geometry library somewhere to report non-exceptional failures.
                        // It has no UI of its own, and previously wrote them to System.Console —
                        // which in a WPF process is nowhere, so a null result had no explanation.
                        C2VGeometry.GeometryDiagnostics.Sink ??= message =>
                            Console.ConsoleOutput.Instance.WriteLine("Geometry", 0, message);
                    }
                }
            }
            return _instance;
        }
    }

    private CanvasRenderer() { }

    #region IShapeRegistry (C2VGeometry)

    void C2VGeometry.IShapeRegistry.Register(Shape shape) => AddShape(shape);

    void C2VGeometry.IShapeRegistry.Unregister(Shape shape) => RemoveShape(shape);

    void C2VGeometry.IShapeRegistry.MoveAbove(Shape shape, Shape referenceShape)
        => MoveShapeAbove(shape, referenceShape);

    void C2VGeometry.IShapeRegistry.MoveBehind(Shape shape, Shape referenceShape)
        => MoveShapeBehind(shape, referenceShape);

    #endregion

    public void AddShape(IDrawable shape)
    {
        // Prevent duplicate adds - check if shape is already placed
        if (shape is Shape s)
        {
            if (s.IsPlaced) return;
            s.IsPlaced = true;
        }
        _shapes.Add(shape);
        RegistryVersion++;
    }

    /// <summary>
    /// Removes a shape from the canvas.
    /// </summary>
    public void RemoveShape(IDrawable shape)
    {
        if (shape is Shape s)
        {
            s.IsPlaced = false;
        }
        if (_shapes.Remove(shape)) RegistryVersion++;
    }

    /// <summary>
    /// Removes multiple shapes from the canvas efficiently.
    /// </summary>
    public void RemoveShapes(IEnumerable<IDrawable> shapes)
    {
        var shapeSet = new HashSet<IDrawable>(shapes);
        foreach (var shape in shapeSet)
        {
            if (shape is Shape s)
            {
                s.IsPlaced = false;
            }
        }
        if (_shapes.RemoveAll(s => shapeSet.Contains(s)) > 0) RegistryVersion++;
    }

    /// <summary>
    /// Moves a shape so it renders above (after) the reference shape in the draw order.
    /// </summary>
    public void MoveShapeAbove(IDrawable shape, IDrawable referenceShape)
    {
        if (shape == referenceShape) return;
        int refIndex = _shapes.IndexOf(referenceShape);
        if (refIndex < 0) return;
        if (!_shapes.Remove(shape)) return;
        // Re-find reference index after removal (it may have shifted)
        refIndex = _shapes.IndexOf(referenceShape);
        _shapes.Insert(refIndex + 1, shape);
        RegistryVersion++;
    }

    /// <summary>
    /// Moves a shape so it renders behind (before) the reference shape in the draw order.
    /// </summary>
    public void MoveShapeBehind(IDrawable shape, IDrawable referenceShape)
    {
        if (shape == referenceShape) return;
        int refIndex = _shapes.IndexOf(referenceShape);
        if (refIndex < 0) return;
        if (!_shapes.Remove(shape)) return;
        refIndex = _shapes.IndexOf(referenceShape);
        _shapes.Insert(refIndex, shape);
        RegistryVersion++;
    }

    public IReadOnlyList<IDrawable> GetShapes() => _shapes.AsReadOnly();

    public void Clear()
    {
        // Reset IsPlaced for all shapes so they can be re-added in next run
        foreach (var shape in _shapes)
        {
            if (shape is Shape s)
            {
                s.IsPlaced = false;
            }
        }
        _shapes.Clear();
        RegistryVersion++;
        Shape.ResetIdCounter();
        ActiveTimeline?.Stop();
        ActiveTimeline = null;
    }

    public void RenderTo(RenderCanvas canvas)
    {
        canvas.Render(_shapes);
        if (DoodleSharp.ApplicationSettings.Instance.ZoomToFitOnRun)
        {
            canvas.ZoomExtents(_shapes);
        }
    }
}
