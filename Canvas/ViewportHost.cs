using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using C2VGeometry;

namespace DoodleSharp.Canvas;

/// <summary>
/// The drawing surface as a whole: a nested grid of <see cref="RenderCanvas"/>, one per leaf of the
/// viewport tree, hosted in the single docked pane titled "Canvas".
///
/// <para>
/// The container is what docks, not the individual canvases — so the docking layout keeps its
/// one-object-per-pane model however the drawing is divided, and every image export renders this
/// one element and gets the tiling for free.
/// </para>
///
/// <para>
/// It is also the fan-out point. Anything that is a property of <i>the drawing</i> — the background,
/// the grid, snapping, selection mode — is set here and applied to every canvas, including ones
/// created later by a resize. Anything that is a property of <i>where the user is working</i> — the
/// drawing tool, the measuring tape, the selection — stays on <see cref="ActiveCanvas"/>.
/// </para>
/// </summary>
public sealed class ViewportHost : Border
{
    private readonly Dictionary<Viewport, RenderCanvas> _canvasFor = new();
    private readonly Dictionary<Viewport, ViewportCell> _cellFor = new();
    private readonly List<ViewportCell> _cells = new();

    private ViewportCell _activeCell;
    private int _syncQueued;

    private Brush _canvasBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private bool _showGrid = true;
    private double _gridSpacing = 10;
    private bool _snapToGrid;
    private bool _isSelectionMode = true;

    public ViewportHost()
    {
        // Built here, not on Loaded: MainWindow reaches ActiveCanvas the moment InitializeComponent
        // returns, and a null there would take the window down before it ever appeared.
        // BuildCell registers the cell and announces its canvas, so nothing is repeated here.
        Child = BuildTree(Viewport.Root);
        _activeCell = _cells[0];
        _activeCell.SetActive(true, layoutIsDivided: false);
        PointHudAtActiveCell();

        Viewport.LayoutChanged += OnLayoutChanged;
    }

    #region What the host exposes

    /// <summary>
    /// The canvas the user is working in — the one the pointer last entered or clicked. Never null.
    /// Tools, the selection and the keyboard shortcuts all act on this one.
    /// </summary>
    public RenderCanvas ActiveCanvas => _activeCell.Canvas;

    /// <summary>The viewport <see cref="ActiveCanvas"/> draws.</summary>
    public Viewport ActiveViewport => _activeCell.Canvas.OwningViewport ?? Viewport.Root;

    /// <summary>Every canvas on screen, in the order the cells appear.</summary>
    public IReadOnlyList<RenderCanvas> Canvases => _cells.Select(c => c.Canvas).ToList();

    /// <summary>True once the drawing is divided into more than one cell.</summary>
    public bool IsDivided => _cells.Count > 1;

    /// <summary>Raised when a canvas is created, so the host window can wire its per-canvas events.</summary>
    public event EventHandler<RenderCanvas>? CanvasCreated;

    /// <summary>Raised when the pointer moves into a different cell.</summary>
    public event EventHandler<RenderCanvas>? ActiveCanvasChanged;

    /// <summary>The canvas drawing a given viewport, resolving a branch to its first cell.</summary>
    public RenderCanvas CanvasFor(Viewport viewport)
    {
        var leaf = (viewport ?? Viewport.Root).ResolveVisible();
        return _canvasFor.TryGetValue(leaf, out var canvas) ? canvas : ActiveCanvas;
    }

    /// <summary>
    /// A canvas's rectangle within this host, taken from WPF's own layout rather than computed from
    /// rows and columns — which is what makes nesting depth irrelevant to the exporters.
    /// </summary>
    public Rect RectOf(RenderCanvas canvas)
    {
        var origin = canvas.TransformToAncestor(this).Transform(new Point(0, 0));
        return new Rect(origin, canvas.RenderSize);
    }

    public void ForEach(Action<RenderCanvas> action)
    {
        foreach (var cell in _cells) action(cell.Canvas);
    }

    /// <summary>
    /// One cell of the drawing as an exporter needs to see it: which shapes, where on the page, and
    /// at what view.
    /// </summary>
    /// <param name="Leaf">The viewport this cell draws.</param>
    /// <param name="Canvas">The canvas drawing it.</param>
    /// <param name="DeviceRect">The cell's rectangle within this host, in device pixels.</param>
    /// <param name="WorldRect">The part of world space the cell is currently showing.</param>
    /// <param name="Scale">
    /// Screen pixels per world unit in that cell — the same quantity as <c>MouseInfo.Scale</c>, and
    /// the multiplier <c>ViewportTransform.WorldToScreenDistance</c> applies. Not its reciprocal.
    /// </param>
    /// <param name="Shapes">The shapes placed on that cell, in draw order.</param>
    public readonly record struct ViewportTile(
        Viewport Leaf,
        RenderCanvas Canvas,
        Rect DeviceRect,
        Rect WorldRect,
        double Scale,
        IReadOnlyList<IDrawable> Shapes);

    /// <summary>
    /// Every cell, with its rectangle on the exported page and the world it is currently showing —
    /// what a vector exporter needs to reproduce the screen.
    ///
    /// <para>
    /// The rectangles come from WPF's own layout rather than from rows-and-columns arithmetic, which
    /// is what makes nesting depth and star sizing irrelevant to every exporter: however the drawing
    /// is divided, a tile is just a rectangle with a view in it.
    /// </para>
    /// </summary>
    public IReadOnlyList<ViewportTile> GetTiles()
    {
        var tiles = new List<ViewportTile>(_cells.Count);
        foreach (var cell in _cells)
        {
            var canvas = cell.Canvas;
            var leaf = canvas.OwningViewport ?? Viewport.Root;
            tiles.Add(new ViewportTile(
                leaf,
                canvas,
                RectOf(canvas),
                canvas.Viewport.GetVisibleWorldBounds(),
                canvas.Viewport.Scale,
                CanvasRenderer.Instance.GetShapes(leaf)));
        }
        return tiles;
    }

    #endregion

    #region Settings that belong to the drawing, not to a cell

    /// <summary>
    /// The canvas colour, for every cell.
    ///
    /// <para>
    /// Single writer on purpose. Each canvas publishes its background to
    /// <c>VText.CanvasBackgroundColor</c>, the static the SVG and PDF exporters resolve a default
    /// text mask against — so several canvases writing different colours would be last-writer-wins.
    /// Going through here means they all publish the same value and the question does not arise.
    /// </para>
    /// </summary>
    public Brush CanvasBackground
    {
        get => _canvasBackground;
        set { _canvasBackground = value; ForEach(c => c.CanvasBackground = value); }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; ForEach(c => c.ShowGrid = value); }
    }

    public double GridSpacing
    {
        get => _gridSpacing;
        set { _gridSpacing = value; ForEach(c => c.GridSpacing = value); }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set { _snapToGrid = value; ForEach(c => c.SnapToGrid = value); }
    }

    /// <summary>
    /// Selection mode is a modal application state, like an armed CAD command, so it applies to the
    /// whole drawing. The drawing tool's own mode is not: a half-finished polyline is mid-gesture
    /// and belongs to the one cell it was started in.
    /// </summary>
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set { _isSelectionMode = value; ForEach(c => c.IsSelectionMode = value); }
    }

    public void CenterOrigin() => ForEach(c => c.CenterOrigin());

    public void ClearShapes() => ForEach(c => c.ClearShapes());

    public void RefreshSnapSettings() => ForEach(c => c.RefreshToolSnapSettings());

    /// <summary>
    /// Repaints every cell.
    ///
    /// <para>
    /// Deliberately unconditional. A cell that does not hold the changed shape repaints an identical
    /// scene, which nobody can see; a cell that does hold it and was skipped is a drawing that has
    /// stopped updating. One redundant repaint is invisible, a missed one reads as a broken app.
    /// </para>
    /// </summary>
    public void Refresh() => ForEach(c => c.Refresh());

    #endregion

    #region Routing a shape to the canvas that draws it

    /// <summary>Adds a shape to the canvas of whichever viewport it was placed on.</summary>
    public void AddShape(IDrawable shape) => CanvasFor(CanvasRenderer.Instance.ViewportOf(shape)).AddShape(shape);

    /// <summary>Removes a shape from whichever canvas is displaying it.</summary>
    public void RemoveShape(IDrawable shape)
    {
        // Asked of every cell rather than resolved: by the time a delete is undone the shape's
        // viewport may have been re-homed, and a shape left in a canvas's own display list while
        // gone from the registry is the desync that makes deleted shapes reappear.
        ForEach(c => c.RemoveShape(shape));
    }

    /// <summary>Every selected shape, across every cell, in cell order.</summary>
    public IReadOnlyList<Shape> SelectedShapes =>
        _cells.SelectMany(c => c.Canvas.SelectionTool.SelectedShapes).ToList();

    public void ClearSelection() => ForEach(c => c.SelectionTool.ClearSelection());

    /// <summary>
    /// Zooms whichever cell holds the shape, and makes that cell active — so the view actually moves
    /// to the shape rather than appearing to do nothing because it is in another cell.
    /// </summary>
    public bool ZoomToShape(long id)
    {
        foreach (var cell in _cells)
        {
            if (!cell.Canvas.ZoomToShape(id)) continue;
            Activate(cell);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Hides every cell's tool overlay <b>and</b> its navigation chrome for the duration of a
    /// capture. The chrome is now a child of the captured element, so unlike the old sibling panel
    /// it would otherwise be baked into every exported image and video frame.
    /// </summary>
    public IDisposable SuppressOverlayForCapture()
    {
        var scopes = new List<IDisposable>();
        foreach (var cell in _cells)
        {
            scopes.Add(cell.Canvas.SuppressOverlayForCapture());
            scopes.Add(cell.HideChromeForCapture());
            cell.SetActive(false, IsDivided);          // no "cell 3 was hovered" highlight in the export
        }

        return new CompositeScope(() =>
        {
            foreach (var scope in scopes) scope.Dispose();
            foreach (var cell in _cells) cell.SetActive(ReferenceEquals(cell, _activeCell), IsDivided);
        });
    }

    /// <summary>
    /// Every cell's shapes, transformed into one flat coordinate space laid out like the screen —
    /// for formats that have no concept of a viewport at all, DXF being the one that matters here.
    ///
    /// <para>
    /// <b>The coordinates are no longer the user's.</b> Each cell is scaled by its own zoom and moved
    /// into place, so a distance measured in the exported file is a distance on screen, not a
    /// distance in the drawing. That is unavoidable — two cells at different zooms cannot share one
    /// 1:1 space — and it is why an undivided drawing must keep taking the untransformed path.
    /// </para>
    ///
    /// <para>
    /// The clones are built inside <c>Shape.SuspendAutoRegistration()</c>: <c>Clone()</c> registers,
    /// so without it an export would leave a full copy of the drawing on the canvas.
    /// </para>
    /// </summary>
    /// <param name="frame">
    /// When true, adds a rectangle around each cell so the tiling is legible in a CAD package.
    /// </param>
    public IReadOnlyList<IDrawable> FlattenForModelSpace(bool frame = true)
    {
        var height = ActualHeight;
        var flattened = new List<IDrawable>();

        using (Shape.SuspendAutoRegistration())
        {
            foreach (var tile in GetTiles())
            {
                var r = tile.DeviceRect;
                var dx = r.X + r.Width / 2 + tile.Canvas.Viewport.PanX;

                // Model space is Y-up and the container's rectangles are Y-down, so the cell's
                // vertical position is measured from the bottom.
                var dy = height - (r.Y + r.Height / 2 + tile.Canvas.Viewport.PanY);
                var offset = new VXYZ(dx, dy, 0);

                foreach (var drawable in tile.Shapes)
                {
                    if (drawable is not Shape shape || !shape.IsVisible) continue;

                    var clone = shape.Clone();
                    clone.Scale(new VXYZ(0, 0, 0), tile.Scale);
                    clone.Move(offset);
                    flattened.Add(clone);
                }

                if (frame && _cells.Count > 1)
                {
                    var bottom = height - (r.Y + r.Height);
                    flattened.Add(new VPolygon(
                        new VXYZ(r.X, bottom),
                        new VXYZ(r.X + r.Width, bottom),
                        new VXYZ(r.X + r.Width, bottom + r.Height),
                        new VXYZ(r.X, bottom + r.Height)));
                }
            }
        }

        return flattened;
    }

    #endregion

    #region Rebuilding when the layout changes

    /// <summary>
    /// The layout changed — subdivided, or a row height or column width was set. Coalesced and
    /// marshalled, because user code runs <c>Main()</c> on a thread-pool thread and a script that
    /// sets <c>Rows</c>, then <c>Columns</c>, then a couple of sizes raises this several times for
    /// one intended layout.
    /// </summary>
    private void OnLayoutChanged()
    {
        if (System.Threading.Interlocked.Exchange(ref _syncQueued, 1) == 1) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            System.Threading.Interlocked.Exchange(ref _syncQueued, 0);
            Sync();
        }));
    }

    /// <summary>
    /// Rebuilds the visual tree to match the viewport tree, reusing the canvas of every cell whose
    /// viewport survived.
    ///
    /// <para>
    /// Reuse is the point. A surviving cell keeps its own canvas object, and with it that cell's pan,
    /// zoom, tool state and GPU upload — so re-running a sketch that declares the same layout does
    /// not slam every view back to the origin.
    /// </para>
    /// </summary>
    public void Sync()
    {
        // Each existing canvas follows its viewport to wherever that viewport is now drawn: down to
        // the first cell if it was subdivided, up to the nearest survivor if it was removed. That is
        // the same rule the shapes on it follow, so a canvas and its contents stay together.
        var survivors = new Dictionary<Viewport, RenderCanvas>();
        foreach (var pair in _canvasFor)
        {
            var destination = pair.Key.ResolveVisible();
            if (!survivors.ContainsKey(destination)) survivors[destination] = pair.Value;
            else Retire(pair.Value);                       // two canvases folded onto one cell
        }

        _canvasFor.Clear();
        _cellFor.Clear();
        _cells.Clear();
        foreach (var pair in survivors) _canvasFor[pair.Key] = pair.Value;

        Child = BuildTree(Viewport.Root);

        var divided = IsDivided;
        if (!_cells.Contains(_activeCell)) _activeCell = _cells[0];
        foreach (var cell in _cells) cell.SetActive(ReferenceEquals(cell, _activeCell), divided);
        PointHudAtActiveCell();
    }

    /// <summary>Builds the element for one viewport: a cell for a leaf, a nested grid for a branch.</summary>
    private FrameworkElement BuildTree(Viewport viewport)
    {
        if (viewport.IsLeaf) return BuildCell(viewport);

        var grid = new Grid();
        for (var r = 0; r < viewport.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = Length(viewport.RowHeightAt(r)) });
        for (var c = 0; c < viewport.Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Length(viewport.ColumnWidthAt(c)) });

        for (var r = 0; r < viewport.Rows; r++)
        {
            for (var c = 0; c < viewport.Columns; c++)
            {
                var child = BuildTree(viewport[r][c]);
                Grid.SetRow(child, r);
                Grid.SetColumn(child, c);
                grid.Children.Add(child);
            }
        }

        return grid;
    }

    /// <summary>Translates a viewport length into WPF's own, which is the one thing this needs WPF for.</summary>
    private static GridLength Length(C2VGeometry.ViewportLength length) =>
        length.IsStar ? new GridLength(length.Value, GridUnitType.Star)
                      : new GridLength(length.Value, GridUnitType.Pixel);

    private ViewportCell BuildCell(Viewport leaf)
    {
        var reused = _canvasFor.TryGetValue(leaf, out var existing);
        var canvas = existing ?? new RenderCanvas();

        // A reused canvas is still a child of the cell built for the previous layout, and a WPF
        // element may have only one parent — re-adding it without this throws "Specified element is
        // already the logical child of another element" the first time any layout changes.
        Detach(canvas);

        var cell = new ViewportCell(canvas);
        cell.ZoomExtentsClicked += c =>
            c.Canvas.ZoomExtents(CanvasRenderer.Instance.GetShapes(c.Canvas.OwningViewport ?? Viewport.Root));
        cell.Activated += Activate;

        _canvasFor[leaf] = canvas;
        _cellFor[leaf] = cell;
        _cells.Add(cell);

        canvas.OwningViewport = leaf;
        ApplyHostSettings(canvas, leaf);
        if (!reused) Announce(cell);

        return cell;
    }

    private static void Detach(RenderCanvas canvas)
    {
        if (canvas.Parent is Panel panel) panel.Children.Remove(canvas);
        else if (canvas.Parent is Decorator decorator) decorator.Child = null;
    }

    private void Announce(ViewportCell cell) => CanvasCreated?.Invoke(this, cell.Canvas);

    /// <summary>
    /// Everything a canvas needs to look like the rest of the drawing. Applied to reused canvases as
    /// well as new ones, so a cell created by a mid-run resize never comes up with the wrong grid or
    /// no snapping — the failure that is invisible until someone tries to draw in the new cell.
    /// </summary>
    private void ApplyHostSettings(RenderCanvas canvas, Viewport leaf)
    {
        canvas.OwningViewport = leaf;
        canvas.CanvasBackground = _canvasBackground;
        canvas.ShowGrid = _showGrid;
        canvas.GridSpacing = _gridSpacing;
        canvas.SnapToGrid = _snapToGrid;
        canvas.IsSelectionMode = _isSelectionMode;
        canvas.RefreshToolSnapSettings();
    }

    private void Retire(RenderCanvas canvas)
    {
        Detach(canvas);
        canvas.ReleaseGpuBackend();
        canvas.SelectionTool.ClearSelection();
        canvas.ClearShapes();
        canvas.OwningViewport = null;
    }

    private void Activate(ViewportCell cell)
    {
        if (ReferenceEquals(cell, _activeCell)) return;

        var divided = IsDivided;
        _activeCell.SetActive(false, divided);
        _activeCell = cell;
        cell.SetActive(true, divided);
        PointHudAtActiveCell();
        ActiveCanvasChanged?.Invoke(this, cell.Canvas);
    }

    /// <summary>
    /// The frame-timing readout follows the active cell. Its numbers are process-wide — one frame,
    /// one cost — so drawing them once, on the cell being looked at, is the honest presentation.
    /// </summary>
    private void PointHudAtActiveCell()
    {
        foreach (var cell in _cells) cell.Canvas.DrawsPerformanceHud = ReferenceEquals(cell, _activeCell);
    }

    /// <summary>Turns the frame-timing readout on or off, for the whole drawing.</summary>
    public bool ShowPerformanceHud
    {
        get => ActiveCanvas.ShowPerformanceHud;
        set { ForEach(c => c.ShowPerformanceHud = value); PointHudAtActiveCell(); }
    }

    #endregion

    private sealed class CompositeScope : IDisposable
    {
        private readonly Action _undo;
        internal CompositeScope(Action undo) => _undo = undo;
        public void Dispose() => _undo();
    }
}
