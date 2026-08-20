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
    /// The shapes in draw order, or null when it has to be re-derived. See <see cref="GetShapes"/>.
    /// </summary>
    private IReadOnlyList<C2VGeometry.IDrawable>? _drawOrder;

    /// <summary>
    /// Which viewport each shape was placed on — <b>only</b> for shapes that are not on the root.
    ///
    /// <para>
    /// A map rather than a field on <see cref="Shape"/> on purpose. A field costs four bytes on
    /// every shape in every document, including the million-shape ones that will never split the
    /// canvas — and note 85 has just finished moving eight fields off <c>Shape</c> to reclaim
    /// exactly that kind of per-shape weight. This costs nothing while the layout is undivided, and
    /// its emptiness is what <see cref="GetShapes(Viewport)"/> checks to skip partitioning entirely.
    /// </para>
    ///
    /// <para>
    /// Keyed on the <see cref="Viewport"/> object, not on a position or an index: subdividing one
    /// cell renumbers the ones after it, so anything positional would silently teleport shapes. A
    /// node's identity survives every resize that does not remove it.
    /// </para>
    /// </summary>
    private readonly Dictionary<Shape, Viewport> _viewportOf = new();

    /// <summary>The per-leaf partition of <see cref="GetShapes()"/>, or null when it must be re-derived.</summary>
    private Dictionary<Viewport, IReadOnlyList<C2VGeometry.IDrawable>>? _byViewport;

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

    private CanvasRenderer()
    {
        // A resize changes which leaf a shape is drawn in without touching a single shape, so the
        // partition has to be re-derived and the per-frame paths told to re-snapshot. Never
        // unsubscribed: this is a process-lifetime singleton.
        Viewport.LayoutChanged += InvalidateDrawOrder;
    }

    #region IShapeRegistry (C2VGeometry)

    void C2VGeometry.IShapeRegistry.Register(Shape shape) => AddShape(shape);

    void C2VGeometry.IShapeRegistry.Unregister(Shape shape) => RemoveShape(shape);

    // Explicit, because the public Clear() on this class means something larger — the between-runs
    // reset, which also rewinds shape ids and stops the timeline. User code reaching this through
    // Canvas.Clear() wants the geometry gone and nothing else. Same pattern as note 33's
    // VLine.ICurve.StartPoint: one name, two audiences.
    void C2VGeometry.IShapeRegistry.Clear() => ClearShapes();

    void C2VGeometry.IShapeRegistry.NotifyOrderChanged(Shape shape) => InvalidateDrawOrder();

    void C2VGeometry.IShapeRegistry.Place(Shape shape, Viewport viewport) => PlaceOnViewport(shape, viewport);

    #endregion

    /// <summary>
    /// Registers <paramref name="shape"/> if it is not already, and moves it onto
    /// <paramref name="viewport"/>. What <c>shape.Place(viewport)</c> reaches.
    /// </summary>
    public void PlaceOnViewport(Shape shape, Viewport viewport)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (viewport == null) throw new ArgumentNullException(nameof(viewport));

        AddShape(shape);

        // The root is the default, so it is stored as absence. That is what keeps the map — and
        // therefore the whole partitioning path — empty on an undivided canvas.
        var isRoot = ReferenceEquals(viewport, Viewport.Root);
        var had = _viewportOf.TryGetValue(shape, out var current);

        if (isRoot)
        {
            if (had) { _viewportOf.Remove(shape); InvalidateDrawOrder(); }
        }
        else if (!had || !ReferenceEquals(current, viewport))
        {
            _viewportOf[shape] = viewport;
            InvalidateDrawOrder();
        }
    }

    /// <summary>
    /// The viewport a shape was placed on, or the root if it was never placed on one. Never returns
    /// a branch — a viewport subdivided after the shape landed on it resolves to its first cell.
    /// </summary>
    public Viewport ViewportOf(IDrawable shape) =>
        shape is Shape s && _viewportOf.TryGetValue(s, out var v) ? v.ResolveVisible() : Viewport.Root.FirstLeaf();

    public void AddShape(IDrawable shape)
    {
        // Prevent duplicate adds - check if shape is already placed
        if (shape is Shape s)
        {
            if (s.IsPlaced) return;
            s.IsPlaced = true;
        }
        _shapes.Add(shape);
        InvalidateDrawOrder();
    }

    /// <summary>
    /// Removes a shape from the canvas.
    /// </summary>
    public void RemoveShape(IDrawable shape)
    {
        if (shape is Shape s)
        {
            s.IsPlaced = false;
            _viewportOf.Remove(s);
        }
        if (_shapes.Remove(shape)) InvalidateDrawOrder();
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
                _viewportOf.Remove(s);
            }
        }
        if (_shapes.RemoveAll(s => shapeSet.Contains(s)) > 0) InvalidateDrawOrder();
    }

    /// <summary>
    /// Every registered shape, <b>in draw order</b>: ascending <see cref="Shape.ZIndex"/>, with
    /// registration order breaking ties. This is the order the renderer, the cull index, the
    /// exporters and hit-testing all consume, so "what is on top" has one answer everywhere.
    ///
    /// <para>
    /// The ordering is derived here rather than kept in <c>_shapes</c> because <c>_shapes</c> is
    /// append-ordered and a <c>ZIndex</c> can change at any time, including from a mouse handler.
    /// It is cached and invalidated by <see cref="InvalidateDrawOrder"/>, and the sort is skipped
    /// entirely — the append-ordered list is handed straight back — when every shape is still at
    /// the default 0, which is the overwhelmingly common case.
    /// </para>
    /// </summary>
    public IReadOnlyList<IDrawable> GetShapes()
    {
        if (_drawOrder != null) return _drawOrder;

        bool needsSort = false;
        foreach (var drawable in _shapes)
        {
            if (drawable is Shape s && s.ZIndex != 0) { needsSort = true; break; }
        }

        // OrderBy is a stable sort, which is what keeps creation order inside a ZIndex band.
        _drawOrder = needsSort
            ? _shapes.OrderBy(d => d is Shape s ? s.ZIndex : 0).ToList()
            : _shapes.AsReadOnly();
        return _drawOrder;
    }

    /// <summary>
    /// The shapes drawn in one leaf viewport, in the same draw order <see cref="GetShapes()"/>
    /// derives — the sort still happens there and nowhere else, and partitioning an already-sorted
    /// list preserves the order inside each part, so every viewport gets the same ZIndex semantics
    /// for free.
    ///
    /// <para>
    /// <b>An undivided canvas costs nothing.</b> While no shape has ever been placed on a viewport,
    /// the root leaf is handed back the <i>same list instance</i> <see cref="GetShapes()"/> returned
    /// — no filter, no dictionary, no allocation — and any other leaf gets an empty array. That is
    /// the same shape as skipping the sort while every ZIndex is still 0, and it is what keeps the
    /// default 1x1 layout identical in cost as well as in behaviour.
    /// </para>
    /// </summary>
    /// <param name="leaf">A leaf viewport. A branch resolves to its first descendant leaf.</param>
    public IReadOnlyList<IDrawable> GetShapes(Viewport leaf)
    {
        if (leaf == null) throw new ArgumentNullException(nameof(leaf));

        var all = GetShapes();

        // ResolveVisible, not FirstLeaf: the caller's viewport may already be DETACHED. Every run
        // begins with Clear(), which calls Viewport.Reset() and installs a brand-new root object,
        // while a ViewportCell still holds the previous one in OwningViewport until the host's
        // Sync() re-keys it -- and Sync() is queued at DispatcherPriority.Render, which is BELOW
        // the Normal-priority await continuation that runs the render. So the render path routinely
        // asks for a leaf that has just left the tree. FirstLeaf() returns that dead node, which
        // matches neither Viewport.Root.FirstLeaf() below nor any key in _byViewport, so every cell
        // was handed Array.Empty and the canvas came up blank while the status bar -- which counts
        // GetShapes() with no viewport -- happily reported the shapes as drawn. ResolveVisible maps
        // a detached node onto the live tree, which is the same rule the shapes themselves follow
        // a few lines below, and is identical to FirstLeaf() for any attached viewport.
        var target = leaf.ResolveVisible();

        if (_viewportOf.Count == 0)
        {
            return ReferenceEquals(target, Viewport.Root.FirstLeaf()) ? all : Array.Empty<IDrawable>();
        }

        if (_byViewport == null)
        {
            var buckets = new Dictionary<Viewport, List<IDrawable>>();
            var orphaned = 0;
            Viewport? orphanHome = null;

            foreach (var drawable in all)
            {
                // Resolved per shape rather than fixed up when the tree changes: a shape placed on a
                // viewport that has since been split draws in its first cell, and one placed on a
                // cell a later resize removed moves to the nearest surviving ancestor. Both stay
                // correct however many times the layout changes again.
                Viewport owner;
                if (drawable is Shape s && _viewportOf.TryGetValue(s, out var v))
                {
                    owner = v.ResolveVisible();
                    if (!v.IsAttached)
                    {
                        // Re-home for real, so the count below is reported once rather than on every
                        // rebuild, and so the map stops referencing a node that has left the tree.
                        _viewportOf[s] = owner;
                        orphaned++;
                        orphanHome = owner;
                    }
                }
                else
                {
                    owner = Viewport.Root.FirstLeaf();
                }

                if (!buckets.TryGetValue(owner, out var bucket))
                {
                    buckets[owner] = bucket = new List<IDrawable>();
                }
                bucket.Add(drawable);
            }

            _byViewport = new Dictionary<Viewport, IReadOnlyList<IDrawable>>(buckets.Count);
            foreach (var pair in buckets) _byViewport[pair.Key] = pair.Value;

            if (orphaned > 0)
            {
                // Said out loud rather than thrown: shrinking your own layout is legitimate, and a
                // running animation must not die because a cell went away. But shapes quietly
                // relocating is not something to leave the user to discover.
                GeometryDiagnostics.Report(
                    $"{orphaned} shape{(orphaned == 1 ? " was" : "s were")} on viewports removed by a " +
                    $"layout change; {(orphaned == 1 ? "it" : "they")} moved to {orphanHome}.");
            }
        }

        return _byViewport.TryGetValue(target, out var shapes) ? shapes : Array.Empty<IDrawable>();
    }

    /// <summary>
    /// Drops the cached draw order and bumps <see cref="RegistryVersion"/> so the per-frame paths
    /// re-snapshot rather than merely re-indexing (note 96). Called on every set change and
    /// whenever a shape's <see cref="Shape.ZIndex"/> is assigned.
    /// </summary>
    private void InvalidateDrawOrder()
    {
        _drawOrder = null;
        _byViewport = null;
        RegistryVersion++;
    }

    /// <summary>
    /// The between-runs reset: removes every shape, rewinds the shape id counter and stops any
    /// running timeline. This is the host's lifecycle clear and is called before each execution.
    ///
    /// <para>
    /// <b>Not what user code gets.</b> <c>Canvas.Clear()</c> routes to
    /// <see cref="ClearShapes"/> through the interface, because rewinding ids and killing a timeline
    /// are not implied by "clear the canvas" and would be a nasty surprise inside a mouse handler.
    /// </para>
    /// </summary>
    public void Clear()
    {
        ClearShapes();
        Shape.ResetIdCounter();
        ActiveTimeline?.Stop();
        ActiveTimeline = null;

        // The layout is part of the run lifecycle, like shape ids: the source says how the canvas is
        // divided, so deleting a `Viewports.Rows = 3` line has to take effect on the next run rather
        // than linger until restart.
        //
        // Deliberately here and NOT in ClearShapes(). Sketch mode calls ClearShapes on every frame,
        // and Canvas.Clear() reaches it from user code — a mouse handler that wiped the layout would
        // be exactly the nasty surprise that split these two methods apart in the first place.
        Viewport.Reset();
    }

    /// <summary>
    /// Removes every shape and nothing else. The geometry half of <see cref="Clear"/>, and what
    /// <c>Canvas.Clear()</c> calls.
    /// </summary>
    public void ClearShapes()
    {
        // IsPlaced is cleared so a shape held by user code can be Place()d again afterwards;
        // AddShape early-returns on an already-placed shape, so without this a re-added shape would
        // silently do nothing.
        foreach (var shape in _shapes)
        {
            if (shape is Shape s)
            {
                s.IsPlaced = false;
            }
        }

        _shapes.Clear();

        // The map holds strong references, so leaving entries behind would pin every shape the
        // canvas has just let go of.
        _viewportOf.Clear();

        // Invalidated (and the version bumped) so the host notices the set changed and re-snapshots
        // rather than re-indexing — RenderCanvas keeps its own list, so without this the display
        // would keep the old shapes (note 96).
        InvalidateDrawOrder();
    }

    public void RenderTo(RenderCanvas canvas)
    {
        var shapes = GetShapes();
        canvas.Render(shapes);
        if (DoodleSharp.ApplicationSettings.Instance.ZoomToFitOnRun)
        {
            canvas.ZoomExtents(shapes);
        }
    }

    /// <summary>
    /// Renders the scene across a grid of viewports: every cell draws the shapes placed on it, and
    /// zoom-to-fit — when the setting is on — fits each cell to its own contents rather than to the
    /// whole drawing.
    ///
    /// <para>
    /// The single-canvas <see cref="RenderTo(RenderCanvas)"/> is unchanged and still means
    /// "everything, to this one canvas"; it is what the benchmark and the offscreen render harness
    /// use, and it is the reason an undivided layout behaves identically.
    /// </para>
    /// </summary>
    public void RenderTo(ViewportHost host)
    {
        var zoomToFit = DoodleSharp.ApplicationSettings.Instance.ZoomToFitOnRun;

        host.ForEach(canvas =>
        {
            var shapes = GetShapes(canvas.OwningViewport ?? Viewport.Root);
            canvas.Render(shapes);
            if (zoomToFit) canvas.ZoomExtents(shapes);
        });
    }
}
