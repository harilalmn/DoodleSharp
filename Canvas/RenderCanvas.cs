using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using C2VGeometry;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using DashStyle = System.Windows.Media.DashStyle;
using DashStyles = System.Windows.Media.DashStyles;
using PenLineCap = System.Windows.Media.PenLineCap;
// Direct usage of VPoint, VLine etc. No alias needed.
// UserMouse is the user-code callback registry (DoodleSharp.Animation.Mouse). It needs an alias only
// here, because this file also has `using System.Windows.Input;` and WPF has its own static Mouse.
// User code never imports System.Windows.Input, so there it is simply `Mouse`.
using UserMouse = DoodleSharp.Animation.Mouse;
using MouseGate = DoodleSharp.Animation.MouseGate;
using MouseInfo = DoodleSharp.Animation.MouseInfo;
using MouseEventKind = DoodleSharp.Animation.MouseEventKind;
using MouseButtonKind = DoodleSharp.Animation.MouseButtonKind;

namespace DoodleSharp.Canvas;

// Snap indicator marker brushes
internal static class SnapMarkerBrushes
{
    public static readonly Brush EndpointBrush;
    public static readonly Brush MidpointBrush;
    public static readonly Brush CenterBrush;
    public static readonly Brush IntersectionBrush;
    public static readonly Brush NearestBrush;
    public static readonly Brush PerpendicularBrush;
    public static readonly Brush ExtensionBrush;
    public static readonly Brush TangentBrush;
    public static readonly Pen ExtensionLinePen;
    public static readonly Pen PerpendicularLinePen;
    public static readonly Pen TangentLinePen;
    public static readonly Pen MeasuringLinePen;

    static SnapMarkerBrushes()
    {
        EndpointBrush = new SolidColorBrush(Colors.Yellow);
        EndpointBrush.Freeze();

        MidpointBrush = new SolidColorBrush(Colors.Cyan);
        MidpointBrush.Freeze();

        CenterBrush = new SolidColorBrush(Colors.Magenta);
        CenterBrush.Freeze();

        IntersectionBrush = new SolidColorBrush(Colors.Red);
        IntersectionBrush.Freeze();

        NearestBrush = new SolidColorBrush(Colors.LimeGreen);
        NearestBrush.Freeze();

        PerpendicularBrush = new SolidColorBrush(Colors.Orange);
        PerpendicularBrush.Freeze();

        ExtensionBrush = new SolidColorBrush(Colors.DeepSkyBlue);
        ExtensionBrush.Freeze();

        TangentBrush = new SolidColorBrush(Colors.Violet);
        TangentBrush.Freeze();

        ExtensionLinePen = new Pen(ExtensionBrush, 1) { DashStyle = DashStyles.Dot };
        ExtensionLinePen.Freeze();

        PerpendicularLinePen = new Pen(PerpendicularBrush, 1) { DashStyle = DashStyles.Dot };
        PerpendicularLinePen.Freeze();

        TangentLinePen = new Pen(TangentBrush, 1) { DashStyle = DashStyles.Dot };
        TangentLinePen.Freeze();

        var measuringBrush = new SolidColorBrush(Colors.LimeGreen);
        measuringBrush.Freeze();
        MeasuringLinePen = new Pen(measuringBrush, 2) { DashStyle = DashStyles.Dash };
        MeasuringLinePen.Freeze();
    }
}

/// <summary>
/// High-performance canvas using DrawingVisual for rendering tens of thousands of shapes.
/// </summary>
public class RenderCanvas : FrameworkElement
{
    private const double PointRadius = 5;

    // Viewport transformation (encapsulates scale/pan/coordinate conversion)
    private readonly ViewportTransform _viewport = new();

    private Point _lastMousePosition;
    private bool _isPanning = false;
    private bool _showGrid = true;
    private double _gridSpacing = 50;

    /// <summary>
    /// Current zoom scale factor. Read-only; use zoom methods to modify.
    /// </summary>
    public double Scale => _viewport.Scale;

    private List<IDrawable> _currentShapes = new();
    private readonly DrawingVisual _visual;
    private readonly DrawingVisual _gridVisual;
    private readonly DrawingVisual _rasterVisual;
    private readonly DrawingVisual _overlayVisual;
    private readonly Rendering.Raster.ManagedRasterBackend _rasterBackend = new();
    private Rendering.Raster.D3D11RasterBackend? _gpuBackend;
    private readonly C2VGeometry.Rendering.ShapeTessellator _gpuTessellator = new();

    /// <summary>
    /// Bumped whenever the shape set changes. The GPU backend uploads geometry once and then only
    /// rewrites its view matrix, so it needs to know when that assumption breaks — this is what
    /// tells it, and it is a counter rather than a hash because the answer only has to be "is this
    /// the same scene I uploaded".
    /// </summary>
    private int _sceneVersion;
    private int _gpuUploadedVersion = -1;
    private readonly List<Shape> _rasterVisibleBuffer = new();
    private readonly DoodleSharp.Rendering.SceneIndex _sceneIndex = new();
    private readonly DoodleSharp.Rendering.FrameMetrics _frameMetrics =
        DoodleSharp.Rendering.FrameMetrics.Instance;

    /// <summary>
    /// The scene index, for callers that need to ask what is where — the tools' snapping and
    /// hit-testing paths. Exposed rather than passed around so the tool classes keep their useful
    /// property of having no reference to the canvas at all.
    /// </summary>
    internal DoodleSharp.Rendering.SceneIndex SceneIndex => _sceneIndex;

    /// <summary>
    /// The view transform, for the benchmark harness to drive scripted camera paths. The canvas
    /// normally owns this outright — pan and zoom arrive as mouse input.
    /// </summary>
    internal ViewportTransform Viewport => _viewport;

    // Measuring Tool
    private MeasuringTool? _measuringTool;
    public MeasuringTool MeasuringTool => _measuringTool ??= new MeasuringTool();

    // Drawing Tool
    private DrawingTool? _drawingTool;
    public DrawingTool DrawingTool => _drawingTool ??= new DrawingTool();

    // Selection Tool
    private SelectionTool? _selectionTool;
    public SelectionTool SelectionTool => _selectionTool ??= new SelectionTool();

    /// <summary>
    /// Whether selection mode is active (vs drawing mode).
    /// </summary>
    public bool IsSelectionMode { get; set; } = true;

    /// <summary>
    /// When enabled, the effective cursor position snaps to the nearest grid intersection.
    /// </summary>
    public bool SnapToGrid { get; set; } = false;

    // Shape highlighting (for Outliner hover)
    private long? _highlightedShapeId;
    public long? HighlightedShapeId
    {
        get => _highlightedShapeId;
        set
        {
            if (_highlightedShapeId != value)
            {
                _highlightedShapeId = value;
                // Overlay only. This fires as the pointer travels down the outliner list, and the
                // scene is identical every time.
                RedrawOverlay();
            }
        }
    }

    // Brush cache for performance
    private static readonly Dictionary<string, Brush> _brushCache = new();
    private static readonly Dictionary<(string color, double thickness, LineType style), Pen> _penCache = new();
    private static readonly Dictionary<(string color, double thickness, LineType style, double scale), Pen> _scaledPenCache = new();

    // Bounds for zoom-relative line weight / line type scale (see GetShapePen). A zoom-relative
    // stroke would otherwise vanish when zoomed far out and swallow the canvas when zoomed far in.
    private const double MinRelativeLineWeight = 0.1;
    private const double MaxRelativeLineWeight = 500.0;
    private const double MinDashScale = 0.01;
    private const double MaxDashScale = 1000.0;
    private const int MaxPenCacheEntries = 4096;

    // Pre-frozen brushes for common colors
    // Removed static BackgroundBrush to allow dynamic changes
    private static readonly Brush GridBrush;
    private static readonly Brush XAxisBrush;  // Red for X-axis
    private static readonly Brush YAxisBrush;  // Green for Y-axis

    private Brush _backgroundBrush;

    static RenderCanvas()
    {
        GridBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        GridBrush.Freeze();

        XAxisBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60));  // Red
        XAxisBrush.Freeze();

        YAxisBrush = new SolidColorBrush(Color.FromRgb(60, 180, 60));  // Green
        YAxisBrush.Freeze();
    }

    public event EventHandler<Point>? MouseWorldPositionChanged;

    public Brush CanvasBackground
    {
        get => _backgroundBrush;
        set
        {
            _backgroundBrush = value;
            if (_backgroundBrush.CanFreeze) _backgroundBrush.Freeze();
            PublishBackgroundColour();
            RedrawAll();
        }
    }

    /// <summary>
    /// Copies the background colour onto <see cref="VText.CanvasBackgroundColor"/>, which is how
    /// the surfaces with no canvas of their own — the SVG and PDF exporters — resolve a text whose
    /// <c>MaskColor</c> is null ("match the canvas"). The renderer itself never reads it back: it
    /// resolves against the live brush, which cannot go stale.
    ///
    /// <para>
    /// Called from the constructor as well as the setter, because the field is initialised directly
    /// there and the setter never runs for the default.
    /// </para>
    /// </summary>
    private void PublishBackgroundColour()
    {
        if (_backgroundBrush is SolidColorBrush solid)
        {
            var c = solid.Color;
            // #RRGGBB, dropping alpha: WPF, SVG and PDFsharp all read that spelling, whereas WPF's
            // own #AARRGGBB is not valid SVG.
            VText.CanvasBackgroundColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; RedrawAll(); }
    }

    public double GridSpacing
    {
        get => _gridSpacing;
        set { _gridSpacing = value; RedrawAll(); }
    }

    public RenderCanvas()
    {
        _backgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _backgroundBrush.Freeze();
        PublishBackgroundColour();

        // Four layers, bottom first: grid, raster, vector, overlay. The raster layer holds the
        // bitmap the managed backend writes hairline geometry into; the vector layer above it holds
        // text, dimensions, chrome; the overlay holds selection and tool feedback. When the raster
        // backend is off, the raster layer simply stays empty and the vector layer is the whole
        // renderer.
        //
        // The grid has its own layer BELOW the raster bitmap because it used to be drawn into the
        // vector layer, which sits above it — so with any raster backend active the grid painted
        // straight over the drawing. Grid under geometry under annotation under overlay is the order
        // a drafting viewport needs.
        _gridVisual = new DrawingVisual();
        AddVisualChild(_gridVisual);
        AddLogicalChild(_gridVisual);

        _rasterVisual = new DrawingVisual();
        AddVisualChild(_rasterVisual);
        AddLogicalChild(_rasterVisual);

        _visual = new DrawingVisual();
        AddVisualChild(_visual);
        AddLogicalChild(_visual);

        _overlayVisual = new DrawingVisual();
        AddVisualChild(_overlayVisual);
        AddLogicalChild(_overlayVisual);

        ClipToBounds = true;
        Focusable = true; // Allow canvas to receive keyboard focus

        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Handles keyboard input for drawing tool when canvas has focus.
    /// </summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Only handle keys when drawing and waiting for next point
        if (DrawingTool.Mode == DrawingMode.None || DrawingTool.Points.Count == 0)
            return;

        var key = e.Key;

        // Escape cancels input mode
        if (key == System.Windows.Input.Key.Escape)
        {
            if (DrawingTool.InputMode != DrawingInputMode.None)
            {
                DrawingTool.HandleEscapeInput();
                Refresh();
                e.Handled = true;
            }
            return;
        }

        // Tab cycles through input modes (None -> Distance -> Angle -> None)
        if (key == System.Windows.Input.Key.Tab)
        {
            e.Handled = true;
            if (DrawingTool.CycleInputMode())
            {
                Refresh();
            }
            return;
        }

        // Enter confirms input
        if (key == System.Windows.Input.Key.Enter)
        {
            if (DrawingTool.InputMode != DrawingInputMode.None)
            {
                // Let MainWindow handle the Enter to place the point
                return;
            }
        }

        // Backspace removes last character
        if (key == System.Windows.Input.Key.Back)
        {
            if (DrawingTool.HandleBackspace())
            {
                Refresh();
                e.Handled = true;
            }
            return;
        }

        // Number keys (0-9) - start distance input if not already in input mode
        char? inputChar = null;
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
        {
            inputChar = (char)('0' + (key - System.Windows.Input.Key.D0));
        }
        else if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
        {
            inputChar = (char)('0' + (key - System.Windows.Input.Key.NumPad0));
        }
        else if (key == System.Windows.Input.Key.OemPeriod || key == System.Windows.Input.Key.Decimal)
        {
            inputChar = '.';
        }
        else if (key == System.Windows.Input.Key.OemMinus || key == System.Windows.Input.Key.Subtract)
        {
            inputChar = '-';
        }

        if (inputChar.HasValue)
        {
            // Start Distance mode if not already in input mode
            if (DrawingTool.InputMode == DrawingInputMode.None)
            {
                DrawingTool.StartDistanceInput();
            }

            if (DrawingTool.HandleCharInput(inputChar.Value))
            {
                Refresh();
                e.Handled = true;
            }
        }
    }

    // Required overrides for hosting DrawingVisual. Index order IS z-order, bottom first: the grid
    // sits under the geometry, the raster bitmap under the vector layer, and the overlay over both.
    protected override int VisualChildrenCount => 4;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _gridVisual,
        1 => _rasterVisual,
        2 => _visual,
        3 => _overlayVisual,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewport.SetViewportSize(ActualWidth, ActualHeight);
        RedrawAll();
    }

    public void CenterOrigin()
    {
        _viewport.Reset();
        RedrawAll();
    }

    /// <summary>
    /// Forces an immediate redraw of the canvas.
    ///
    /// <para>
    /// This bumps <c>_sceneVersion</c>, because by contract it means "something about the scene
    /// changed" and the usual reason to call it is a shape mutated in place — a property edit, a
    /// live slider preview, user code moving something. The GPU backend keys its vertex upload off
    /// that counter, so without the bump a mutation repainted the CPU layers and left the GPU
    /// geometry showing the old state.
    /// </para>
    ///
    /// <para>
    /// <b>The cost is real and deliberate.</b> <c>UploadScene</c> is a full rebuild — re-tessellate
    /// every shape, three copies of the scene — so this makes an in-place edit re-upload the whole
    /// drawing on the GPU path. It bites hardest in <c>MainWindow.RepaintAfterUserCode</c>, which
    /// calls this once per animation frame, so a running animation now re-uploads at frame rate on
    /// that backend. That is still the correct trade: without the bump those animations rendered
    /// <i>frozen</i> geometry, because the vertex buffer was uploaded once and never invalidated.
    /// It also makes the mutate-in-place path behave like the create-new-shapes path, which went
    /// through <c>SetFrameShapes</c> and was already re-uploading every frame.
    /// </para>
    ///
    /// <para>
    /// The actual fix is incremental upload — per-shape vertex ranges and a partial
    /// <c>UpdateSubresource</c> — which is a different piece of work. Note that panning and zooming
    /// do <b>not</b> come through here: they call <c>RedrawAll</c> directly, so navigation still
    /// costs one constant-buffer write and note 88's premise is intact. Prefer
    /// <c>RedrawOverlay</c> for anything that only moves chrome.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        _sceneVersion++;
        RedrawAll();
    }

    // Convert world coordinates to screen coordinates
    private Point WorldToScreen(double worldX, double worldY)
        => _viewport.WorldToScreen(worldX, worldY);

    // Convert screen coordinates to world coordinates
    private Point ScreenToWorld(double screenX, double screenY)
        => _viewport.ScreenToWorld(screenX, screenY);

    /// <summary>
    /// Zooms one step in or out about the centre of the viewport. This is what the floating canvas
    /// controls call — in interactive mode the wheel belongs to user code, so there has to be a way to
    /// zoom that does not consume a mouse gesture.
    /// </summary>
    public void ZoomStep(bool zoomIn)
    {
        _viewport.Zoom(zoomIn);
        RedrawAll();
    }

    // ── User mouse callbacks ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True while user code has a mouse handler registered. The canvas then stops competing for the
    /// mouse: selection, wheel zoom and double-click zoom-to-fit are all suppressed.
    /// </summary>
    private static bool IsInteractive => UserMouse.HasHandlers;

    /// <summary>Whether a drawing tool is armed and therefore owns the click.</summary>
    private bool IsDrawingToolActive => _drawingTool != null && _drawingTool.Mode != DrawingMode.None;

    /// <summary>Whether the measuring tape is armed and therefore owns the click.</summary>
    private bool IsMeasuringActive => _measuringTool?.Mode == ToolMode.Measuring;

    /// <summary>Whether this event should be dispatched to user code, and the canvas's own gesture skipped.</summary>
    private bool AllowUserMouse
        => MouseGate.Allow(IsInteractive, _isPanning, IsDrawingToolActive, IsMeasuringActive);

    /// <summary>
    /// Builds the payload handed to a user mouse callback. This is the only place WPF input types are
    /// read — <see cref="MouseInfo"/> itself is deliberately free of them, so the whole registry stays
    /// testable away from a window.
    /// </summary>
    private MouseInfo BuildMouseInfo(
        MouseEventKind kind, MouseEventArgs e, Point screenPos, VXYZ position, int wheelDelta)
    {
        var raw = ScreenToWorld(screenPos.X, screenPos.Y);
        var mods = Keyboard.Modifiers;

        return new MouseInfo(
            kind,
            position: position,
            rawPosition: new VXYZ(raw.X, raw.Y),
            screenX: screenPos.X,
            screenY: screenPos.Y,
            button: MapButton(e),
            leftDown: e.LeftButton == MouseButtonState.Pressed,
            rightDown: e.RightButton == MouseButtonState.Pressed,
            middleDown: e.MiddleButton == MouseButtonState.Pressed,
            shift: (mods & ModifierKeys.Shift) != 0,
            ctrl: (mods & ModifierKeys.Control) != 0,
            alt: (mods & ModifierKeys.Alt) != 0,
            clickCount: (e as MouseButtonEventArgs)?.ClickCount ?? 0,
            wheelDelta: wheelDelta,
            scale: _viewport.Scale,
            // Deferred, so a handler that never asks "what is under the cursor?" never pays for the
            // spatial query. SelectionTool.HitTest reuses an internal buffer, so this allocates nothing.
            hitTest: p => SelectionTool.HitTest(p, _sceneIndex, _viewport.Scale));
    }

    /// <summary>Maps the button this event is about, or None for a move/wheel/enter/leave.</summary>
    private static MouseButtonKind MapButton(MouseEventArgs e) => e switch
    {
        MouseButtonEventArgs b => b.ChangedButton switch
        {
            MouseButton.Left => MouseButtonKind.Left,
            MouseButton.Right => MouseButtonKind.Right,
            MouseButton.Middle => MouseButtonKind.Middle,
            MouseButton.XButton1 => MouseButtonKind.XButton1,
            MouseButton.XButton2 => MouseButtonKind.XButton2,
            _ => MouseButtonKind.None
        },
        _ => MouseButtonKind.None
    };

    /// <summary>
    /// Records the pointer position into <c>Mouse</c> for the polled API. Called on every mouse event
    /// regardless of whether any handler is registered, so <c>Mouse.X</c>/<c>Y</c>/<c>IsDown</c> — and
    /// the <c>Sketch.MouseX</c>/<c>MouseY</c>/<c>MousePressed</c> properties that read them — are
    /// always current. Three field writes; cheap enough for a path this hot.
    /// </summary>
    private static void TrackPointer(MouseEventArgs e, Point worldPos)
        => UserMouse.Track(worldPos.X, worldPos.Y,
            e.LeftButton == MouseButtonState.Pressed
            || e.RightButton == MouseButtonState.Pressed
            || e.MiddleButton == MouseButtonState.Pressed);

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouseScreenPos = e.GetPosition(this);

        if (AllowUserMouse)
        {
            var world = ScreenToWorld(mouseScreenPos.X, mouseScreenPos.Y);
            UserMouse.RaiseWheel(BuildMouseInfo(
                MouseEventKind.Wheel, e, mouseScreenPos, new VXYZ(world.X, world.Y), e.Delta));

            // The wheel belongs to user code in interactive mode; the floating canvas controls are how
            // the user zooms instead.
            e.Handled = true;
            return;
        }

        _viewport.ZoomAtPoint(mouseScreenPos.X, mouseScreenPos.Y, e.Delta > 0);
        RedrawAll();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        var screenPos = e.GetPosition(this);
        var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);
        TrackPointer(e, worldPos);

        if (!AllowUserMouse) return;

        UserMouse.RaiseEnter(BuildMouseInfo(
            MouseEventKind.Enter, e, screenPos, new VXYZ(worldPos.X, worldPos.Y), 0));
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        var screenPos = e.GetPosition(this);
        var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);

        if (AllowUserMouse)
        {
            // A drag that ends off-canvas without capture would otherwise never see its up, leaving a
            // handler's "am I dragging?" flag stuck on for the rest of the session.
            if (UserMouse.IsDown && !IsMouseCaptured)
            {
                UserMouse.RaiseUp(BuildMouseInfo(
                    MouseEventKind.Up, e, screenPos, new VXYZ(worldPos.X, worldPos.Y), 0));
            }

            UserMouse.RaiseLeave(BuildMouseInfo(
                MouseEventKind.Leave, e, screenPos, new VXYZ(worldPos.X, worldPos.Y), 0));
        }

        UserMouse.Track(worldPos.X, worldPos.Y, isDown: false);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Grab keyboard focus on any click so canvas shortcuts (P/L/C/R drawing tools,
        // Delete, A=select-all, Esc) work. Without this, focus stays in the code editor
        // and the keystroke is typed there instead of triggering the tool.
        if (!IsKeyboardFocusWithin) Focus();

        var downScreenPos = e.GetPosition(this);
        TrackPointer(e, ScreenToWorld(downScreenPos.X, downScreenPos.Y));

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            // Middle-drag pan stays the canvas's own gesture even in interactive mode: it is the only
            // way to pan, and handing it to a script would leave a drawing larger than the viewport
            // unreachable.
            _isPanning = true;
            _lastMousePosition = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.Hand;
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            var screenPos = e.GetPosition(this);
            var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);
            var vPoint = new VXYZ(worldPos.X, worldPos.Y);

            // Apply snap to grid on clicks (same as OnMouseMove preview)
            if (SnapToGrid && !_isPanning)
            {
                var snapped = SnapPointToGrid(vPoint.X, vPoint.Y);
                vPoint = snapped;
            }

            // Handle drawing tool clicks first (if active)
            if (_drawingTool != null && _drawingTool.Mode != DrawingMode.None)
            {
                if (e.ClickCount == 2)
                {
                    _drawingTool.OnDoubleClick(vPoint);
                }
                else
                {
                    _drawingTool.OnLeftClick(vPoint);
                }
                RedrawAll();
                e.Handled = true;
                return;
            }

            // Handle measuring tool clicks
            if (_measuringTool?.Mode == ToolMode.Measuring)
            {
                _measuringTool.OnLeftClick(vPoint);
                RedrawAll();
                e.Handled = true;
                return;
            }

            // Hand the press to user code, ahead of selection.
            //
            // The order here is the crux of the whole feature. Selection mode is ON BY DEFAULT and its
            // branch below consumes every left click with e.Handled = true, so dispatching after it
            // would mean a user click handler never fired in the default configuration. Dispatching
            // before it — and returning — is what makes the click available, and is why interactive
            // mode suppresses selection rather than trying to share the gesture with it.
            //
            // It sits *after* the drawing and measuring branches on purpose: those are modal states the
            // user armed with a shortcut, they keep priority while armed, and user code cannot override
            // them. Both already returned above, so reaching here means neither is active.
            if (AllowUserMouse)
            {
                UserMouse.RaiseDown(BuildMouseInfo(
                    MouseEventKind.Down, e, screenPos, vPoint, 0));

                // Own the drag for as long as the button is held, so a handler keeps receiving moves
                // even if the pointer leaves the canvas and always gets its matching up.
                CaptureMouse();

                e.Handled = true;
                return;
            }

            // Handle selection mode
            if (IsSelectionMode && _selectionTool != null)
            {
                var shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                // Check for double-click on empty space to zoom extents
                if (e.ClickCount == 2)
                {
                    var hitShape = _selectionTool.HitTest(vPoint, _sceneIndex, _viewport.Scale);
                    if (hitShape == null)
                    {
                        ZoomExtents(_currentShapes);
                        e.Handled = true;
                        return;
                    }
                }

                _selectionTool.OnMouseDown(vPoint, shift, ctrl, _currentShapes, _viewport.Scale, _sceneIndex);

                if (_selectionTool.IsBoxSelecting || _selectionTool.IsDraggingHandle)
                {
                    CaptureMouse();
                }

                RedrawAll();
                e.Handled = true;
                return;
            }

            // Double-click on empty space: Zoom to Fit
            if (e.ClickCount == 2)
            {
                ZoomExtents(_currentShapes);
                e.Handled = true;
            }
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            // Handle drawing tool right-click (cancel)
            if (_drawingTool != null && _drawingTool.Mode != DrawingMode.None)
            {
                _drawingTool.OnRightClick();
                RedrawAll();
                e.Handled = true;
                return;
            }

            // Right-click reaches user code only once the drawing tool has had its chance to cancel.
            if (AllowUserMouse)
            {
                var screenPos = e.GetPosition(this);
                var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);
                var vPoint = new VXYZ(worldPos.X, worldPos.Y);

                if (SnapToGrid && !_isPanning)
                    vPoint = SnapPointToGrid(vPoint.X, vPoint.Y);

                UserMouse.RaiseDown(BuildMouseInfo(
                    MouseEventKind.Down, e, screenPos, vPoint, 0));
                e.Handled = true;
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var upScreenPos = e.GetPosition(this);
        var upWorldPos = ScreenToWorld(upScreenPos.X, upScreenPos.Y);

        // Dispatch before the branches below, so a user-owned drag gets its terminating up even though
        // none of the canvas's own gestures are in progress. Mouse.RaiseUp also synthesises the click.
        if (AllowUserMouse)
        {
            var vPoint = new VXYZ(upWorldPos.X, upWorldPos.Y);
            if (SnapToGrid) vPoint = SnapPointToGrid(vPoint.X, vPoint.Y);

            UserMouse.RaiseUp(BuildMouseInfo(MouseEventKind.Up, e, upScreenPos, vPoint, 0));

            // Release the capture taken on down. Nothing below can apply: selection never started.
            if (IsMouseCaptured && !_isPanning) ReleaseMouseCapture();

            UserMouse.Track(upWorldPos.X, upWorldPos.Y, isDown: false);
            return;
        }

        TrackPointer(e, upWorldPos);

        if (e.MiddleButton == MouseButtonState.Released && _isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
        else if (e.LeftButton == MouseButtonState.Released)
        {
            // Handle selection box completion or handle dragging end
            if (_selectionTool?.IsBoxSelecting == true || _selectionTool?.IsDraggingHandle == true)
            {
                var screenPos = e.GetPosition(this);
                var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);
                var vPoint = new VXYZ(worldPos.X, worldPos.Y);

                var shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                _selectionTool.OnMouseUp(vPoint, _currentShapes, shift, ctrl);
                ReleaseMouseCapture();
                RedrawAll();
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var screenPos = e.GetPosition(this);
        var worldPos = ScreenToWorld(screenPos.X, screenPos.Y);

        if (SnapToGrid && !_isPanning)
        {
            var snapped = SnapPointToGrid(worldPos.X, worldPos.Y);
            worldPos = new Point(snapped.X, snapped.Y);
        }

        // Kept ahead of everything below: this drives the coordinate readout in the status bar, which
        // must keep working in every mode.
        MouseWorldPositionChanged?.Invoke(this, worldPos);

        TrackPointer(e, worldPos);

        // Hand the move to user code. This is a leading branch with an early return, which is safe
        // because the gate already excludes every state the chain below reacts to — panning, an armed
        // drawing tool, the measuring tape. Mouse.RaiseMove picks OnDrag over OnMove when a button is
        // held. Note e.Handled is deliberately NOT set: MouseMove is a bubbling routed event and
        // MainWindow reads IsMouseOver upstream, and there is no built-in move behaviour left to
        // suppress once the gate has passed.
        if (AllowUserMouse)
        {
            UserMouse.RaiseMove(BuildMouseInfo(
                MouseEventKind.Move, e, screenPos, new VXYZ(worldPos.X, worldPos.Y), 0));
            return;
        }

        if (_isPanning)
        {
            _viewport.Pan(screenPos.X - _lastMousePosition.X, screenPos.Y - _lastMousePosition.Y);
            _lastMousePosition = screenPos;
            RedrawAll();
        }
        else if (_drawingTool != null && _drawingTool.Mode != DrawingMode.None)
        {
            // Update drawing tool with cursor position (use spatial index for O(log n) snap detection)
            // Check for Shift key to enable orthogonal constraint
            _drawingTool.IsOrthoMode = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            _drawingTool.OnMouseMove(new VXYZ(worldPos.X, worldPos.Y), _currentShapes, _viewport.Scale, _sceneIndex);
            RedrawOverlay();   // scene is unchanged; only the tool's overlay moved

            // Focus canvas when drawing to enable keyboard input for distance/angle
            if (_drawingTool.Points.Count > 0 && !IsFocused)
            {
                Focus();
            }
        }
        else if (_measuringTool?.Mode == ToolMode.Measuring)
        {
            // Update measuring tool with cursor position (use spatial index for O(log n) snap detection)
            _measuringTool.OnMouseMove(new VXYZ(worldPos.X, worldPos.Y), _currentShapes, _viewport.Scale, _sceneIndex);
            RedrawOverlay();   // scene is unchanged; only the tool's overlay moved
        }
        else if (_selectionTool?.IsBoxSelecting == true || _selectionTool?.IsDraggingHandle == true)
        {
            // Update selection box or handle drag (with snapping support, use spatial index for O(log n) performance)
            _selectionTool.OnMouseMove(new VXYZ(worldPos.X, worldPos.Y), _currentShapes, _viewport.Scale, _sceneIndex);
            RedrawOverlay();   // scene is unchanged; only the tool's overlay moved
        }
    }

    public void ClearShapes()
    {
        _currentShapes.Clear();
        _sceneIndex.Clear();
        _viewport.Reset();
        RedrawAll();
    }

    public void Render(IEnumerable<IDrawable> shapes)
    {
        _currentShapes = shapes.ToList();
        RebuildSpatialIndex();
        _sceneVersion++;
        RedrawAll();
    }

    /// <summary>
    /// Replaces the shape set for one animation frame, without rebuilding the spatial index or
    /// repainting. Used by the per-frame paths that regenerate the whole scene — sketch mode calls
    /// <c>CanvasRenderer.Clear()</c> and re-runs <c>Draw()</c> every tick, so the shape *objects*
    /// are different every frame, not merely mutated.
    ///
    /// <para>
    /// Without this, <see cref="Refresh"/> kept redrawing <c>_currentShapes</c> — a
    /// <c>ToList()</c> snapshot assigned only by <see cref="Render"/>, which the sketch path never
    /// calls — so a sketch that *created* its shapes in <c>Draw()</c> rendered frame 0 forever and
    /// only one that mutated <c>Setup()</c>-created objects in place appeared to animate.
    /// </para>
    ///
    /// <para>
    /// The index is deliberately dropped rather than rebuilt: <see cref="RedrawAll"/> skips
    /// culling while a timeline or sketch is running, so rebuilding it per frame would be pure
    /// waste. Any later non-animation path re-creates it via <see cref="EnsureSpatialIndexForShape"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Takes a snapshot copy, matching <see cref="Render"/>. That is a per-frame allocation
    /// proportional to the scene, and it is knowingly left in place: Phase 0 is about making the
    /// sketch path *correct* so the benchmark measures real work. The allocation is removed with
    /// the rest of the per-frame garbage when <c>SceneStore</c> lands.
    /// </remarks>
    internal void SetFrameShapes(IEnumerable<IDrawable> shapes)
    {
        _currentShapes = shapes.ToList();
        _sceneIndex.Rebuild(_currentShapes);
        _sceneVersion++;
    }

    /// <summary>
    /// Re-reads the bounds of every shape for the next frame. Call after a timeline step, which
    /// mutates <c>OffsetX</c>/<c>OffsetY</c>/<c>DrawFactor</c> on existing shapes rather than
    /// replacing them — so the shape objects are the same but the boxes the index holds are stale.
    ///
    /// <para>
    /// Culling used to be switched off entirely while a timeline played, precisely to dodge this.
    /// That traded a correct-but-stale index for drawing the whole document at 60 Hz, which is the
    /// wrong side of the trade for any scene big enough to care. Re-indexing is O(n) with one
    /// <c>GetBounds()</c> each; that is cheap for the presentation-sized scenes timelines are
    /// actually used on, and gets cheaper still once bounds are cached on the shape.
    /// </para>
    /// </summary>
    internal void ReindexForAnimationFrame()
    {
        _sceneIndex.Rebuild(_currentShapes);
    }

    /// <summary>
    /// Rebuilds the spatial index from the current shapes.
    /// Called for bulk operations; individual add/remove use incremental updates.
    /// </summary>
    private void RebuildSpatialIndex()
    {
        _sceneIndex.Rebuild(_currentShapes);
    }

    /// <summary>
    /// Adds a shape to the current canvas display without requiring code execution.
    /// Uses incremental spatial index update instead of full rebuild.
    /// </summary>
    public void AddShape(IDrawable shape)
    {
        _currentShapes.Add(shape);
        _sceneVersion++;

        // O(1) append. The index has no root bounds to outgrow, so unlike the QuadTree this can
        // never trigger a surprise full rebuild from a shape landing far outside the scene.
        _sceneIndex.Add(shape);
        if (_sceneIndex.NeedsRebuild) RebuildSpatialIndex();

        RedrawAll();
    }

    /// <summary>
    /// Removes a shape from the canvas.
    /// Uses incremental spatial index update.
    /// </summary>
    public void RemoveShape(IDrawable shape)
    {
        _currentShapes.Remove(shape);
        _sceneIndex.Remove(shape);
        _sceneVersion++;
        RedrawAll();
    }

    /// <summary>
    /// Updates a shape's position in the spatial index.
    /// Call this after moving or resizing a shape.
    /// </summary>
    public void UpdateShapePosition(IDrawable shape)
    {
        // The index stores bounds by value, so a moved shape must be re-indexed. A rebuild is the
        // honest answer: the alternative is tracking each shape's slot, and moves come from
        // dragging, which repaints anyway.
        //
        // The version bump is what makes the GPU backend re-upload. Without it, dragging a shape
        // moved its hit-testing and its selection handles while the geometry itself stayed put on
        // screen — the shape appeared stuck to the canvas.
        _sceneVersion++;
        RebuildSpatialIndex();
        RedrawAll();
    }

    /// <summary>
    /// Gets a read-only list of current shapes.
    /// </summary>
    public IReadOnlyList<IDrawable> GetCurrentShapes()
    {
        return _currentShapes.AsReadOnly();
    }

    private static Brush GetCachedBrush(string colorName)
    {
        if (_brushCache.TryGetValue(colorName, out var cached))
            return cached;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorName);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[colorName] = brush;
            return brush;
        }
        catch
        {
            return Brushes.White;
        }
    }

    /// <summary>
    /// Builds the pen for a shape.
    ///
    /// <para>
    /// <b>Display Line Weight</b> (Settings &gt; Application Settings, off by default) is the single
    /// switch: off, a shape's <c>LineWeight</c> is device pixels and a stroke looks the same at any
    /// zoom; on, it is world units, so strokes thicken as you zoom in the way a CAD package shows
    /// true widths. It replaced a pair of Absolute/Relative dropdowns — one for line weight and one
    /// for line type scale — which offered four combinations where only two were ever wanted.
    /// </para>
    ///
    /// <para>
    /// <b>Line type scale is now always absolute</b>: dash lengths are fixed on screen. That is the
    /// only behaviour anyone asked for, and it removes the interaction that made the old pair hard
    /// to reason about. The compensation below still matters, though — WPF dash lengths are
    /// multiples of the pen thickness, so scaling the *thickness* with zoom stretches the dashes as
    /// a side effect, and it has to be divided back out to keep them absolute.
    /// </para>
    /// </summary>
    private Pen GetShapePen(string colorName, double lineWeight, LineType style, double lineTypeScale)
    {
        var settings = DoodleSharp.ApplicationSettings.Instance;

        var zoom = _viewport.Scale;
        if (!settings.DisplayLineWeight || zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom))
            return GetCachedPen(colorName, lineWeight, style, lineTypeScale);

        // Clamped, or a world-unit stroke vanishes zoomed out and swallows the canvas zoomed in.
        var thickness = Math.Clamp(lineWeight * zoom, MinRelativeLineWeight, MaxRelativeLineWeight);

        // No dash compensation here any more. It used to divide the thickness scaling back out of
        // the pattern, because WPF dash lengths are multiples of the pen thickness and the pattern
        // was expressed in those multiples. The pattern is now canonical device pixels
        // (LineTypePatterns) and GetDashStyle divides by the thickness it is actually given, so the
        // on-screen dash length is thickness-independent by construction rather than by correction.
        return GetCachedPen(colorName, thickness, style, lineTypeScale);
    }

    private static Pen GetCachedPen(string colorName, double thickness, LineType style = LineType.Continuous, double scale = 1.0)
    {
        // Round to avoid too many cache entries - zoom-relative thickness and scale are
        // continuous values, so an unrounded key would add an entry per zoom step.
        var roundedThickness = Math.Round(thickness, 2);
        var roundedScale = Math.Round(scale, 3);
        var key = (colorName, roundedThickness, style, roundedScale);
        if (_scaledPenCache.TryGetValue(key, out var cached))
            return cached;

        // Continuous zooming can still walk the key space; drop the cache rather than grow it forever.
        if (_scaledPenCache.Count >= MaxPenCacheEntries)
            _scaledPenCache.Clear();

        var brush = GetCachedBrush(colorName);
        var pen = new Pen(brush, roundedThickness);

        // Apply dash pattern based on stroke style
        if (style != LineType.Continuous)
        {
            pen.DashStyle = GetDashStyle(style, roundedScale, roundedThickness);
            pen.DashCap = PenLineCap.Round;
        }

        pen.Freeze();
        _scaledPenCache[key] = pen;
        return pen;
    }

    /// <summary>
    /// Builds the WPF dash style for a line type.
    ///
    /// <para>
    /// The pattern comes from <see cref="C2VGeometry.Rendering.LineTypePatterns"/>, the single
    /// definition shared with the software rasterizer — there used to be a second table here that
    /// disagreed with it and rendered Center, Phantom and Hidden as solid lines on the other backend.
    /// </para>
    ///
    /// <para>
    /// <b>WPF dash lengths are multiples of the pen thickness</b>, so the canonical device-pixel runs
    /// are divided by <paramref name="thickness"/> here; WPF multiplies it straight back and the
    /// stroke lands at the intended pixel length whatever the pen weight or the zoom. That division
    /// is why <c>GetShapePen</c> no longer carries a compensation term of its own.
    /// </para>
    /// </summary>
    private static DashStyle GetDashStyle(LineType style, double scale, double thickness)
    {
        var pattern = C2VGeometry.Rendering.LineTypePatterns.DevicePixels(style);

        if (pattern.IsEmpty ||
            C2VGeometry.Rendering.LineTypePatterns.IsSolid(style, scale) ||
            thickness <= 0 || !double.IsFinite(thickness))
            return DashStyles.Solid;

        var runs = new double[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
            runs[i] = pattern[i] * scale / thickness;

        return new DashStyle(runs, 0);
    }

    private void RedrawAll()
    {
        _frameMetrics.BeginFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var wasRaster = _rasterActive;
        try
        {
            RedrawAllCore();
        }
        finally
        {
            watch.Stop();
            // Only a vector frame tells us what the vector path costs. Timing a raster frame and
            // feeding it back would measure the wrong renderer and latch the choice permanently.
            if (!wasRaster) _lastVectorFrameMs = watch.Elapsed.TotalMilliseconds;
            _frameMetrics.EndFrame();
        }
    }

    private void RedrawAllCore()
    {
        using var dc = _visual.RenderOpen();

        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        // Decide the backend before anything is painted, because it changes who owns the
        // background. The raster layer sits *beneath* the vector one, so if the vector layer also
        // filled an opaque background it would hide the bitmap completely — which is exactly what
        // happened the first time: the rasterised geometry vanished and only the grid and the
        // WPF-drawn text remained visible.
        var useRaster = ShouldUseRasterBackend();

        // Background and grid both go into the bottom layer, underneath the raster bitmap.
        //
        // The grid used to be drawn into the vector layer, which composites ABOVE the raster bitmap,
        // so with a raster backend active it painted over the geometry. The background has to move
        // with it: leaving it in the vector layer would draw an opaque rectangle on top of the grid
        // and hide it completely — which is exactly what happened the first time this was split, and
        // is why the offscreen comparison showed no grid on either backend.
        //
        // Painting it beneath the raster bitmap is harmless: that backend clears its own surface to
        // the same colour and writes opaque pixels over it. Note 82's rule — that the VECTOR layer
        // must not paint a background while the raster layer is active — is unchanged and is why
        // nothing is drawn into `dc` here.
        using (var gridDc = _gridVisual.RenderOpen())
        {
            gridDc.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (_showGrid)
            {
                DrawGrid(gridDc);
                DrawAxes(gridDc);
            }
        }

        // Calculate Viewport in World Coordinates for Culling
        var visibleBounds = _viewport.GetVisibleWorldBounds();

        // Add padding to account for stroke thickness (approx 20px in world units)
        var padding = 20.0 / Math.Max(_viewport.Scale, ViewportTransform.MinZoom);
        var minX = visibleBounds.Left - padding;
        var maxX = visibleBounds.Right + padding;
        var minY = visibleBounds.Top - padding;
        var maxY = visibleBounds.Bottom + padding;

        // Cull. Two things changed here from the original, and both mattered more than the index:
        //
        //  1. Culling used to be switched OFF whenever a timeline or sketch was playing — the only
        //     two paths that run at 60 Hz, i.e. exactly when it was needed. The reasoning was that
        //     moving shapes invalidate their bounds; the answer is to re-index, which SetFrameShapes
        //     and the animation path now do, not to draw the entire document every frame.
        //
        //  2. The old loop walked ALL n shapes and probed a per-frame HashSet for each, so it was
        //     O(n) regardless of how few were visible — the index only ever saved the *draw*, never
        //     the *iteration*. Walking the visibility bitset instead makes the frame O(visible), and
        //     the bits come out in slot order, which is draw order, so nothing has to be sorted.
        _frameMetrics.BeginStage(Rendering.FrameStage.Cull);
        _sceneIndex.Query(minX, minY, maxX, maxY);
        _frameMetrics.EndStage();
        _frameMetrics.RecordVisibility(_sceneIndex.VisibleCount, _sceneIndex.ConsideredCount);

        DoodleSharp.Diagnostics.Journal.Activity("canvas.redraw");

        _frameMetrics.BeginStage(Rendering.FrameStage.Raster);
        var lodScale = _viewport.Scale;

        // Raster backend: hairline geometry goes into the bitmap layer beneath, and only what it
        // declines — text, dimensions, arrows, infinite lines — continues through the vector path
        // below. Overlays and chrome are always vector.
        IReadOnlyList<Shape>? rasterDeferred = null;

        if (useRaster)
        {
            rasterDeferred = RenderThroughRasterBackend(lodScale);
        }
        else
        {
            ClearRasterLayer();
        }

        if (rasterDeferred != null)
        {
            for (int i = 0; i < rasterDeferred.Count; i++)
            {
                try { DispatchShapeDraw(dc, rasterDeferred[i]); }
                catch (Exception ex)
                {
                    // A distinct key from the main draw loop's, so a journal shows which of the two
                    // paths failed. Site keys are unique repo-wide precisely so a key in a
                    // user-submitted log maps back to one line (CLAUDE.md note 40).
                    DoodleSharp.Diagnostics.Journal.Fatal("CANVAS.DRAW.DEFERRED_THREW",
                        "Raster-deferred shape rendering threw — this will terminate the render pass",
                        ex, DescribeShapeForJournal(rasterDeferred[i]));
                    throw;
                }
            }
        }

        foreach (var slot in _sceneIndex.Visible)
        {
            // With the raster backend on, the geometry has already been drawn into the bitmap and
            // anything it declined was handled above; the vector pass here would draw everything a
            // second time.
            if (useRaster) break;

            var shape = _sceneIndex.ShapeAt(slot);
            if (shape == null) continue;

            // Skip hidden shapes
            if (shape is Shape s && !s.IsVisible)
                continue;

            // Level of detail. Culling answers *which* shapes are on screen; zoomed out over a large
            // drawing the answer is "most of them", and drawing a quarter-pixel building outline
            // costs a full tessellation to produce one indistinguishable mark. This is what stops
            // frame cost from tracking document size once culling has stopped helping.
            var lod = Rendering.LodPolicy.Classify(_sceneIndex.MaxExtentAt(slot), lodScale);
            if (lod == Rendering.LodLevel.Skip)
                continue;

            if (lod == Rendering.LodLevel.Dot && shape is Shape dot)
            {
                // The index's bounds already have OffsetX/OffsetY folded in, so the centre is
                // animation-correct as it stands — do not add them again.
                //
                // Dots are accumulated per colour and emitted as one geometry each at the end of the
                // pass rather than drawn individually. At the widest zoom of a large drawing almost
                // every shape lands here, so "one draw call per shape" simply moves the bottleneck
                // rather than removing it: 100k DrawRectangle calls cost as much as the geometry
                // they replaced.
                _sceneIndex.CentreAt(slot, out var cx, out var cy);
                var p = WorldToScreen(cx, cy);
                AddDot(dot.Color, p);
                continue;
            }

            // Rendering runs on the UI thread inside WPF's render pass, so a throw here takes the
            // process down through DispatcherUnhandledException — where the stack names the Draw*
            // method but not *which shape* had the bad data. Record the culprit's identity, then
            // rethrow unchanged: swallowing it would leave the DrawingContext with unbalanced
            // Push/Pop from a half-drawn group and corrupt every later frame.
            try
            {
                // Stroke-only shapes accumulate into one geometry per pen; anything else flushes
                // the batch first, so draw order stays exact against filled shapes, text, hatches
                // and regions. Only consecutive runs of unfilled strokes are reordered among
                // themselves, and hairlines do not occlude each other visibly.
                if (shape is Shape batchable && Rendering.StrokeBatcher.CanBatch(batchable))
                {
                    if (TryBatchStrokes(batchable)) continue;
                }

                _strokeBatcher.Flush(dc);
                DispatchShapeDraw(dc, shape);
            }
            catch (Exception ex)
            {
                DoodleSharp.Diagnostics.Journal.Fatal("CANVAS.DRAW.THREW",
                    "Shape rendering threw — this will terminate the render pass", ex,
                    DescribeShapeForJournal(shape));
                throw;
            }
        }

        _strokeBatcher.Flush(dc);
        FlushDots(dc);
        _frameMetrics.EndStage();

        RedrawOverlay();
    }

    /// <summary>
    /// Redraws only the interactive layer — selection handles, the rubber band, snap markers, the
    /// tool preview, the outliner highlight.
    ///
    /// <para>
    /// These change on every mouse move; the scene beneath them does not. Rebuilding the whole
    /// scene to move a snap marker four pixels is what made drawing and selecting feel heavy on a
    /// large drawing, and it is pure waste: the geometry, its culling and its tessellation are all
    /// identical between the two frames. Keeping them in a separate visual means WPF re-composites
    /// the retained scene rather than the renderer rebuilding it.
    /// </para>
    /// </summary>
    /// <summary>
    /// Shows the frame-timing readout. Off by default: <see cref="Rendering.FrameMetrics"/> costs
    /// nothing while disabled, and the HUD is a diagnostic, not decoration.
    /// </summary>
    public bool ShowPerformanceHud
    {
        get => _frameMetrics.IsEnabled;
        set
        {
            if (_frameMetrics.IsEnabled == value) return;
            _frameMetrics.IsEnabled = value;
            _frameMetrics.Reset();
            RedrawOverlay();
        }
    }

    private bool _overlaySuppressed;

    /// <summary>
    /// Blanks the overlay layer for the lifetime of the returned scope, so a capture of this canvas
    /// contains only the drawing.
    ///
    /// <para>
    /// The overlay is a visual child of the canvas (see <see cref="GetVisualChild"/>), and every image
    /// and video export renders the canvas itself — so without this the F10 frame-timing readout,
    /// selection handles, the rubber band, snap markers and the measuring overlay all end up baked
    /// into the exported PNG, GIF or MP4. That is never what someone exporting a drawing wants.
    /// </para>
    ///
    /// <para>
    /// <see cref="RedrawOverlay"/> checks the flag itself rather than relying on nothing else calling
    /// it, so an overlay repaint triggered mid-capture cannot put the chrome back.
    /// </para>
    /// </summary>
    public IDisposable SuppressOverlayForCapture() => new OverlaySuppression(this);

    private sealed class OverlaySuppression : IDisposable
    {
        private readonly RenderCanvas _canvas;

        public OverlaySuppression(RenderCanvas canvas)
        {
            _canvas = canvas;
            _canvas._overlaySuppressed = true;
            _canvas.RedrawOverlay();
        }

        public void Dispose()
        {
            _canvas._overlaySuppressed = false;
            _canvas.RedrawOverlay();
        }
    }

    private void RedrawOverlay()
    {
        // Opening and closing the context with nothing drawn is what actually clears the layer.
        using var dc = _overlayVisual.RenderOpen();

        if (_overlaySuppressed) return;

        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (_frameMetrics.IsEnabled) DrawPerformanceHud(dc);

        if (_highlightedShapeId.HasValue)
        {
            DrawShapeHighlight(dc, _highlightedShapeId.Value);
        }

        if (_measuringTool?.Mode == ToolMode.Measuring)
        {
            DrawMeasuringOverlay(dc);
        }

        if (_drawingTool?.Mode != DrawingMode.None)
        {
            DrawDrawingToolOverlay(dc);
        }

        // Interactive mode suppresses selection, so its chrome must go too — otherwise handles from a
        // selection made before the run stay painted over a canvas that no longer responds to them.
        if (IsSelectionMode && _selectionTool != null && !IsInteractive)
        {
            DrawSelectionOverlay(dc);
        }
    }

    /// <summary>
    /// Routes one shape to its Draw* method. Split out of <see cref="RedrawAll"/> so the render loop
    /// can wrap a single, well-defined call in the diagnostic try/catch.
    /// </summary>
    private void DispatchShapeDraw(DrawingContext dc, IDrawable shape)
    {
        // Animated rotation is applied here, once, for every shape type — NOT in the individual
        // Draw* methods. RotateAnimation writes Shape.RotationAngle/RotationPivot on any Shape, but
        // only DrawLine, DrawCircle and DrawArrow ever read them back, so rotating an ellipse, arc,
        // polygon, polyline, bezier, spline, text, group, hatch or region silently did nothing.
        // (Note 55 fixed the same bug for VRectangle alone.) Per-shape opt-in was the defect: a new
        // shape had to remember to implement it, and fifteen of them did not.
        //
        // VRectangle is the one exception: its RotationAngle setter rebuilds the corner geometry,
        // so it arrives here already rotated and a transform on top would rotate it twice.
        var rotation = shape is Shape s && shape is not VRectangle
                       && s.RotationPivot != null && Math.Abs(s.RotationAngle) > 1e-9
            ? s
            : null;

        if (rotation != null)
        {
            var pivot = WorldToScreen(rotation.RotationPivot!.X + rotation.OffsetX,
                                      rotation.RotationPivot!.Y + rotation.OffsetY);
            // Negated because screen Y is inverted relative to world Y.
            dc.PushTransform(new RotateTransform(-rotation.RotationAngle, pivot.X, pivot.Y));
        }

        try
        {
            switch (shape)
            {
                case VPoint point:
                    DrawPoint(dc, point);
                    break;

                case VLine line:
                    DrawLine(dc, line);
                    break;

                case VXLine xline:
                    DrawXLine(dc, xline);
                    break;

                case VRay ray:
                    DrawRay(dc, ray);
                    break;

                case VArc arc:
                    DrawArc(dc, arc);
                    break;

                case VCircle circle:
                    DrawCircle(dc, circle);
                    break;

                case VRectangle rect:
                    DrawRectangle(dc, rect);
                    break;

                case VEllipse ellipse:
                    DrawEllipse(dc, ellipse);
                    break;

                case VPolygon polygon:
                    DrawPolygon(dc, polygon);
                    break;

                case VPolyline polyline:
                    DrawPolyline(dc, polyline);
                    break;

                case VText text:
                    DrawText(dc, text);
                    break;

                case VBezier bezier:
                    DrawBezier(dc, bezier);
                    break;

                case VSpline spline:
                    DrawSpline(dc, spline);
                    break;

                case VArrow arrow:
                    DrawArrow(dc, arrow);
                    break;

                case VRadialDimension radDim:
                    DrawRadialDimension(dc, radDim);
                    break;

                case VDimension dim:
                    DrawDimension(dc, dim);
                    break;

                case VGroup group:
                    DrawGroup(dc, group);
                    break;

                case Region region:
                    DrawRegion(dc, region);
                    break;

                case VHatch hatch:
                    DrawHatch(dc, hatch);
                    break;
            }
        }
        finally
        {
            // Must balance even if a Draw* method throws: RedrawAll logs and rethrows (note 40),
            // and an unbalanced Push would corrupt every later frame in the same pass.
            if (rotation != null) dc.Pop();
        }
    }

    /// <summary>
    /// Identity of a shape for a crash record: enough to find it in the user's code (id, name, type)
    /// and to spot the classic renderer-killers (NaN/Infinity coordinates, absurd extents).
    /// </summary>
    private static string DescribeShapeForJournal(IDrawable shape)
    {
        try
        {
            if (shape is not Shape s)
                return $"type={shape?.GetType().Name ?? "<null>"}";

            var text = $"id={s.Id} name={s.Name ?? "<unnamed>"} type={s.GetType().Name} visible={s.IsVisible}";
            try
            {
                var bounds = AABB.FromShape(s);
                var finite = double.IsFinite(bounds.MinX) && double.IsFinite(bounds.MinY)
                          && double.IsFinite(bounds.MaxX) && double.IsFinite(bounds.MaxY);
                text += $" bounds=[{bounds.MinX:G6},{bounds.MinY:G6} .. {bounds.MaxX:G6},{bounds.MaxY:G6}] finiteBounds={finite}";
            }
            catch (Exception boundsEx)
            {
                text += $" bounds=<threw {boundsEx.GetType().Name}>";
            }
            return text;
        }
        catch
        {
            return "<shape description unavailable>";
        }
    }

    private void DrawDrawingToolOverlay(DrawingContext dc)
    {
        if (_drawingTool == null) return;

        // Draw snap indicator
        if (_drawingTool.CurrentSnap != null)
        {
            DrawSnapIndicator(dc, _drawingTool.CurrentSnap);
        }

        // Draw collected points as markers
        foreach (var point in _drawingTool.Points)
        {
            var screenPos = WorldToScreen(point.X, point.Y);
            dc.DrawEllipse(SnapMarkerBrushes.EndpointBrush, null, screenPos, 5, 5);
        }

        // Draw preview shape
        var previewShape = _drawingTool.GetPreviewShape();
        if (previewShape != null)
        {
            DrawPreviewShape(dc, previewShape);
        }
    }

    private void DrawPreviewShape(DrawingContext dc, C2VGeometry.Shape shape)
    {
        // Use dashed gray pen for preview
        var previewBrush = new SolidColorBrush(Colors.Gray);
        previewBrush.Freeze();
        var previewPen = new Pen(previewBrush, 1.5) { DashStyle = DashStyles.Dash };
        previewPen.Freeze();

        switch (shape)
        {
            case VPoint point:
                var screenPoint = WorldToScreen(point.X, point.Y);
                dc.DrawEllipse(previewBrush, previewPen, screenPoint, PointRadius, PointRadius);
                break;

            case VLine line:
                var lineStart = WorldToScreen(line.Start.X, line.Start.Y);
                var lineEnd = WorldToScreen(line.End.X, line.End.Y);
                dc.DrawLine(previewPen, lineStart, lineEnd);
                break;

            case VCircle circle:
                var circleCenter = WorldToScreen(circle.Center.X, circle.Center.Y);
                var circleRadius = circle.Radius * _viewport.Scale;
                dc.DrawEllipse(null, previewPen, circleCenter, circleRadius, circleRadius);
                break;

            case VRectangle rect:
                var rectTopLeft = WorldToScreen(rect.Corner.X, rect.Corner.Y + rect.Height);
                var rectWidth = rect.Width * _viewport.Scale;
                var rectHeight = rect.Height * _viewport.Scale;
                dc.DrawRectangle(null, previewPen, new Rect(rectTopLeft.X, rectTopLeft.Y, rectWidth, rectHeight));
                break;

            case VEllipse ellipse:
                var ellipseCenter = WorldToScreen(ellipse.Center.X, ellipse.Center.Y);
                var radiusX = ellipse.RadiusX * _viewport.Scale;
                var radiusY = ellipse.RadiusY * _viewport.Scale;
                dc.DrawEllipse(null, previewPen, ellipseCenter, radiusX, radiusY);
                break;

            case VArc arc:
                DrawArcPreview(dc, arc, previewPen);
                break;

            case VPolygon polygon:
                if (polygon.Points.Count > 1)
                {
                    var polyGeom = new StreamGeometry();
                    using (var ctx = polyGeom.Open())
                    {
                        var firstPt = WorldToScreen(polygon.Points[0].X, polygon.Points[0].Y);
                        ctx.BeginFigure(firstPt, false, true);
                        for (int i = 1; i < polygon.Points.Count; i++)
                        {
                            var pt = WorldToScreen(polygon.Points[i].X, polygon.Points[i].Y);
                            ctx.LineTo(pt, true, false);
                        }
                    }
                    polyGeom.Freeze();
                    dc.DrawGeometry(null, previewPen, polyGeom);
                }
                break;

            case VPolyline polyline:
                if (polyline.Points.Count > 1)
                {
                    var plGeom = new StreamGeometry();
                    using (var ctx = plGeom.Open())
                    {
                        var firstPt = WorldToScreen(polyline.Points[0].X, polyline.Points[0].Y);
                        ctx.BeginFigure(firstPt, false, false);
                        for (int i = 1; i < polyline.Points.Count; i++)
                        {
                            var pt = WorldToScreen(polyline.Points[i].X, polyline.Points[i].Y);
                            ctx.LineTo(pt, true, false);
                        }
                    }
                    plGeom.Freeze();
                    dc.DrawGeometry(null, previewPen, plGeom);
                }
                break;

            case VBezier bezier:
                var bezierPts = bezier.GetRenderPoints();
                if (bezierPts.Count > 1)
                {
                    var bezGeom = new StreamGeometry();
                    using (var ctx = bezGeom.Open())
                    {
                        var firstPt = WorldToScreen(bezierPts[0].X, bezierPts[0].Y);
                        ctx.BeginFigure(firstPt, false, false);
                        for (int i = 1; i < bezierPts.Count; i++)
                        {
                            var pt = WorldToScreen(bezierPts[i].X, bezierPts[i].Y);
                            ctx.LineTo(pt, true, false);
                        }
                    }
                    bezGeom.Freeze();
                    dc.DrawGeometry(null, previewPen, bezGeom);
                }
                // Also draw control point indicators
                var cp1 = WorldToScreen(bezier.P1.X, bezier.P1.Y);
                var cp2 = WorldToScreen(bezier.P2.X, bezier.P2.Y);
                dc.DrawEllipse(previewBrush, null, cp1, 4, 4);
                dc.DrawEllipse(previewBrush, null, cp2, 4, 4);
                break;

            case VSpline spline:
                var splinePts = spline.GetRenderPoints();
                if (splinePts.Count > 1)
                {
                    var spGeom = new StreamGeometry();
                    using (var ctx = spGeom.Open())
                    {
                        var firstPt = WorldToScreen(splinePts[0].X, splinePts[0].Y);
                        ctx.BeginFigure(firstPt, false, false);
                        for (int i = 1; i < splinePts.Count; i++)
                        {
                            var pt = WorldToScreen(splinePts[i].X, splinePts[i].Y);
                            ctx.LineTo(pt, true, false);
                        }
                    }
                    spGeom.Freeze();
                    dc.DrawGeometry(null, previewPen, spGeom);
                }
                break;

            case VArrow arrow:
                var arrowStart = WorldToScreen(arrow.Start.X, arrow.Start.Y);
                var arrowEnd = WorldToScreen(arrow.End.X, arrow.End.Y);
                dc.DrawLine(previewPen, arrowStart, arrowEnd);
                // Draw arrowhead
                var (wing1, wing2) = arrow.GetEndArrowhead();
                var screenWing1 = WorldToScreen(wing1.X, wing1.Y);
                var screenWing2 = WorldToScreen(wing2.X, wing2.Y);
                dc.DrawLine(previewPen, arrowEnd, screenWing1);
                dc.DrawLine(previewPen, arrowEnd, screenWing2);
                break;

            case VText text:
                var textPos = WorldToScreen(text.Location.X, text.Location.Y);
                var formattedText = new FormattedText(
                    text.Content,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"),
                    text.Height * _viewport.Scale,
                    previewBrush,
                    1.0);
                dc.DrawText(formattedText, new Point(textPos.X, textPos.Y - text.Height * _viewport.Scale));
                break;
        }
    }

    private void DrawArcPreview(DrawingContext dc, VArc arc, Pen pen)
    {
        var center = WorldToScreen(arc.Center.X, arc.Center.Y);
        var radius = arc.Radius * _viewport.Scale;

        var startAngle = arc.StartAngle * Math.PI / 180;
        var endAngle = arc.EndAngle * Math.PI / 180;

        var startPoint = new Point(
            center.X + radius * Math.Cos(-startAngle),
            center.Y + radius * Math.Sin(-startAngle));
        var endPoint = new Point(
            center.X + radius * Math.Cos(-endAngle),
            center.Y + radius * Math.Sin(-endAngle));

        // Match DrawArc: span is |sweep|, direction follows its sign (see note there).
        var sweep = arc.EndAngle - arc.StartAngle;
        var isLargeArc = Math.Abs(sweep) > 180;
        var sweepDir = sweep >= 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, sweepDir, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawMeasuringOverlay(DrawingContext dc)
    {
        if (_measuringTool == null) return;

        // Draw snap indicator
        if (_measuringTool.CurrentSnap != null)
        {
            DrawSnapIndicator(dc, _measuringTool.CurrentSnap);
        }

        // Draw measuring line if first point is set
        if (_measuringTool.FirstPoint != null)
        {
            var startScreen = WorldToScreen(_measuringTool.FirstPoint.X, _measuringTool.FirstPoint.Y);

            // Draw first point marker
            dc.DrawEllipse(SnapMarkerBrushes.EndpointBrush, null, startScreen, 6, 6);

            // Draw line to current position
            var endPoint = _measuringTool.GetEffectiveEndPoint();
            if (endPoint != null)
            {
                var endScreen = WorldToScreen(endPoint.X, endPoint.Y);
                dc.DrawLine(SnapMarkerBrushes.MeasuringLinePen, startScreen, endScreen);

                // Draw distance label at midpoint
                var distance = _measuringTool.GetCurrentDistance();
                if (distance.HasValue)
                {
                    var midScreen = new Point(
                        (startScreen.X + endScreen.X) / 2,
                        (startScreen.Y + endScreen.Y) / 2);

                    DrawDistanceLabel(dc, midScreen, distance.Value);
                }
            }
        }
    }

    private void DrawSelectionOverlay(DrawingContext dc)
    {
        if (_selectionTool == null) return;

        // Create handle brushes and pens (always needed for selection handles)
        var handleBrush = new SolidColorBrush(Colors.White);
        handleBrush.Freeze();
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 1.5);
        handlePen.Freeze();

        // Default selection pen for handles bounding box
        var selectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 255)), 1.5);
        selectionPen.Freeze();

        // Draw selection box if dragging
        if (_selectionTool.IsBoxSelecting && _selectionTool.BoxStart != null && _selectionTool.BoxEnd != null)
        {
            var start = WorldToScreen(_selectionTool.BoxStart.X, _selectionTool.BoxStart.Y);
            var end = WorldToScreen(_selectionTool.BoxEnd.X, _selectionTool.BoxEnd.Y);

            var rect = new Rect(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y));

            // Crossing selection (drag left): green dashed
            // Window selection (drag right): blue solid
            bool isCrossing = _selectionTool.BoxEnd.X < _selectionTool.BoxStart.X;

            Brush boxBrush;
            Pen boxPen;
            if (isCrossing)
            {
                boxBrush = new SolidColorBrush(Color.FromArgb(40, 0, 200, 80));
                boxBrush.Freeze();
                var strokeBrush = new SolidColorBrush(Color.FromRgb(0, 200, 80));
                strokeBrush.Freeze();
                boxPen = new Pen(strokeBrush, 1.5) { DashStyle = DashStyles.Dash };
                boxPen.Freeze();
            }
            else
            {
                boxBrush = new SolidColorBrush(Color.FromArgb(40, 0, 150, 255));
                boxBrush.Freeze();
                boxPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 255)), 1.5);
                boxPen.Freeze();
            }

            dc.DrawRectangle(boxBrush, boxPen, rect);
        }

        // Draw snap indicator when dragging control points
        if (_selectionTool.IsDraggingHandle && _selectionTool.CurrentSnap != null)
        {
            DrawSnapIndicator(dc, _selectionTool.CurrentSnap);
        }

        // Draw selection handles for selected shapes
        foreach (var shape in _selectionTool.SelectedShapes)
        {
            DrawSelectionHandles(dc, shape, handleBrush, handlePen, selectionPen);
        }
    }

    private void DrawSelectionHandles(DrawingContext dc, Shape shape, Brush handleBrush, Pen handlePen, Pen boundsPen)
    {
        const double handleSize = 8;
        const double smallHandleSize = 6;

        // Get bounding box
        var bounds = shape.GetBounds();
        var minScreen = WorldToScreen(bounds.Min.X, bounds.Max.Y);
        var maxScreen = WorldToScreen(bounds.Max.X, bounds.Min.Y);

        // Draw bounding box
        var boundsRect = new Rect(minScreen, maxScreen);
        dc.DrawRectangle(null, boundsPen, boundsRect);

        // Draw control points
        var controlPoints = shape.GetControlPoints();
        var moveBrush = new SolidColorBrush(Color.FromRgb(50, 205, 50)); // Green for move
        moveBrush.Freeze();
        var vertexBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange for vertex
        vertexBrush.Freeze();
        var radiusBrush = new SolidColorBrush(Color.FromRgb(138, 43, 226)); // Purple for radius
        radiusBrush.Freeze();
        var curveBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // Pink for curve control
        curveBrush.Freeze();

        foreach (var cp in controlPoints)
        {
            var screenPos = WorldToScreen(cp.X, cp.Y);
            var size = cp.Type == ControlPointType.Move ? handleSize : smallHandleSize;

            Brush fillBrush = cp.Type switch
            {
                ControlPointType.Move => moveBrush,
                ControlPointType.Vertex => vertexBrush,
                ControlPointType.Radius => radiusBrush,
                ControlPointType.CurveControl => curveBrush,
                _ => handleBrush
            };

            if (cp.Type == ControlPointType.Move)
            {
                // Draw circle for move handle
                dc.DrawEllipse(fillBrush, handlePen, screenPos, size / 2, size / 2);
            }
            else if (cp.Type == ControlPointType.CurveControl)
            {
                // Draw diamond for curve control
                var diamond = new StreamGeometry();
                using (var ctx = diamond.Open())
                {
                    ctx.BeginFigure(new Point(screenPos.X, screenPos.Y - size / 2), true, true);
                    ctx.LineTo(new Point(screenPos.X + size / 2, screenPos.Y), true, false);
                    ctx.LineTo(new Point(screenPos.X, screenPos.Y + size / 2), true, false);
                    ctx.LineTo(new Point(screenPos.X - size / 2, screenPos.Y), true, false);
                }
                diamond.Freeze();
                dc.DrawGeometry(fillBrush, handlePen, diamond);
            }
            else
            {
                // Draw square for other handles
                dc.DrawRectangle(fillBrush, handlePen, new Rect(
                    screenPos.X - size / 2,
                    screenPos.Y - size / 2,
                    size,
                    size));
            }
        }
    }

    private void DrawSnapIndicator(DrawingContext dc, SnapResult snap)
    {
        var screenPos = WorldToScreen(snap.Point.X, snap.Point.Y);
        const double markerSize = 8;

        Brush markerBrush = snap.Type switch
        {
            SnapType.Endpoint => SnapMarkerBrushes.EndpointBrush,
            SnapType.Midpoint => SnapMarkerBrushes.MidpointBrush,
            SnapType.Center => SnapMarkerBrushes.CenterBrush,
            SnapType.Intersection => SnapMarkerBrushes.IntersectionBrush,
            SnapType.Nearest => SnapMarkerBrushes.NearestBrush,
            SnapType.Perpendicular => SnapMarkerBrushes.PerpendicularBrush,
            SnapType.Extension => SnapMarkerBrushes.ExtensionBrush,
            SnapType.Tangent => SnapMarkerBrushes.TangentBrush,
            _ => Brushes.White
        };

        var markerPen = new Pen(markerBrush, 2);
        markerPen.Freeze();

        switch (snap.Type)
        {
            case SnapType.Endpoint:
                // Square marker
                dc.DrawRectangle(null, markerPen, new Rect(
                    screenPos.X - markerSize / 2, screenPos.Y - markerSize / 2,
                    markerSize, markerSize));
                break;

            case SnapType.Midpoint:
                // Triangle marker
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(screenPos.X, screenPos.Y - markerSize), false, true);
                    ctx.LineTo(new Point(screenPos.X - markerSize * 0.866, screenPos.Y + markerSize / 2), true, false);
                    ctx.LineTo(new Point(screenPos.X + markerSize * 0.866, screenPos.Y + markerSize / 2), true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(null, markerPen, triangle);
                break;

            case SnapType.Center:
                // Circle marker
                dc.DrawEllipse(null, markerPen, screenPos, markerSize / 2, markerSize / 2);
                break;

            case SnapType.Intersection:
                // X marker
                dc.DrawLine(markerPen,
                    new Point(screenPos.X - markerSize / 2, screenPos.Y - markerSize / 2),
                    new Point(screenPos.X + markerSize / 2, screenPos.Y + markerSize / 2));
                dc.DrawLine(markerPen,
                    new Point(screenPos.X + markerSize / 2, screenPos.Y - markerSize / 2),
                    new Point(screenPos.X - markerSize / 2, screenPos.Y + markerSize / 2));
                break;

            case SnapType.Nearest:
                // Diamond marker
                var diamond = new StreamGeometry();
                using (var ctx = diamond.Open())
                {
                    ctx.BeginFigure(new Point(screenPos.X, screenPos.Y - markerSize), false, true);
                    ctx.LineTo(new Point(screenPos.X + markerSize, screenPos.Y), true, false);
                    ctx.LineTo(new Point(screenPos.X, screenPos.Y + markerSize), true, false);
                    ctx.LineTo(new Point(screenPos.X - markerSize, screenPos.Y), true, false);
                }
                diamond.Freeze();
                dc.DrawGeometry(null, markerPen, diamond);
                break;

            case SnapType.Perpendicular:
                // Draw dotted line from reference point to perpendicular point
                if (snap.ReferenceSource != null)
                {
                    var refScreen = WorldToScreen(snap.ReferenceSource.X, snap.ReferenceSource.Y);
                    dc.DrawLine(SnapMarkerBrushes.PerpendicularLinePen, refScreen, screenPos);

                    // Draw small circle at reference point
                    dc.DrawEllipse(null, markerPen, refScreen, markerSize / 3, markerSize / 3);
                }

                // Right angle marker at snap point
                var rightAngle = new StreamGeometry();
                using (var ctx = rightAngle.Open())
                {
                    ctx.BeginFigure(new Point(screenPos.X - markerSize, screenPos.Y), false, false);
                    ctx.LineTo(new Point(screenPos.X, screenPos.Y), true, false);
                    ctx.LineTo(new Point(screenPos.X, screenPos.Y - markerSize), true, false);
                }
                rightAngle.Freeze();
                dc.DrawGeometry(null, markerPen, rightAngle);

                // Draw perpendicular label
                if (snap.ReferenceSource != null)
                {
                    var distance = snap.ReferenceSource.DistanceTo(snap.Point);
                    DrawSnapLabel(dc, screenPos, $"Perp: {distance:F2}", SnapMarkerBrushes.PerpendicularBrush);
                }
                break;

            case SnapType.Tangent:
                // Draw dotted line from reference point to tangent point
                if (snap.ReferenceSource != null)
                {
                    var refScreen = WorldToScreen(snap.ReferenceSource.X, snap.ReferenceSource.Y);
                    dc.DrawLine(SnapMarkerBrushes.TangentLinePen, refScreen, screenPos);

                    // Draw small circle at reference point
                    var tangentPen = new Pen(SnapMarkerBrushes.TangentBrush, 2);
                    tangentPen.Freeze();
                    dc.DrawEllipse(null, tangentPen, refScreen, markerSize / 3, markerSize / 3);
                }

                // Circle marker at tangent point
                dc.DrawEllipse(null, markerPen, screenPos, markerSize / 2, markerSize / 2);

                // Draw tangent label
                if (snap.ReferenceSource != null)
                {
                    var distance = snap.ReferenceSource.DistanceTo(snap.Point);
                    DrawSnapLabel(dc, screenPos, $"Tan: {distance:F2}", SnapMarkerBrushes.TangentBrush);
                }
                break;

            case SnapType.Extension:
                // Draw dotted extension line from source to snap point
                if (snap.ExtensionSource != null)
                {
                    var sourceScreen = WorldToScreen(snap.ExtensionSource.X, snap.ExtensionSource.Y);
                    dc.DrawLine(SnapMarkerBrushes.ExtensionLinePen, sourceScreen, screenPos);

                    // Draw small square at source endpoint
                    dc.DrawRectangle(null, markerPen, new Rect(
                        sourceScreen.X - markerSize / 3, sourceScreen.Y - markerSize / 3,
                        markerSize * 2 / 3, markerSize * 2 / 3));
                }

                // Draw X marker at snap point
                dc.DrawLine(markerPen,
                    new Point(screenPos.X - markerSize / 2, screenPos.Y - markerSize / 2),
                    new Point(screenPos.X + markerSize / 2, screenPos.Y + markerSize / 2));
                dc.DrawLine(markerPen,
                    new Point(screenPos.X + markerSize / 2, screenPos.Y - markerSize / 2),
                    new Point(screenPos.X - markerSize / 2, screenPos.Y + markerSize / 2));

                // Draw extension label with distance and angle
                if (snap.ExtensionSource != null)
                {
                    var effectivePoint = _drawingTool.GetEffectiveEndPoint() ?? snap.Point;
                    var basePoint = _drawingTool.OverrideDistance.HasValue || _drawingTool.OverrideAngle.HasValue
                        ? snap.ExtensionSource
                        : snap.ExtensionSource;

                    var distance = _drawingTool.OverrideDistance ?? snap.ExtensionSource.DistanceTo(effectivePoint);
                    var angle = _drawingTool.OverrideAngle ?? snap.ExtensionAngle;

                    // Format label with highlighting for active input mode
                    string labelText;
                    if (_drawingTool.InputMode == DrawingInputMode.Distance)
                    {
                        labelText = $"Extension: [{_drawingTool.InputBuffer}_] < {angle:F0}°";
                    }
                    else if (_drawingTool.InputMode == DrawingInputMode.Angle)
                    {
                        labelText = $"Extension: {distance:F2} < [{_drawingTool.InputBuffer}_]°";
                    }
                    else
                    {
                        labelText = $"Extension: {distance:F2} < {angle:F0}°";
                    }
                    DrawExtensionLabel(dc, screenPos, labelText);
                }
                break;
        }
    }

    private void DrawExtensionLabel(DrawingContext dc, Point screenPos, string text)
    {
        var typeface = new Typeface("Segoe UI");
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            SnapMarkerBrushes.ExtensionBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Position label below and to the right of the snap point
        var labelPos = new Point(screenPos.X + 10, screenPos.Y + 5);

        // Draw background
        var padding = 3.0;
        var bgRect = new Rect(
            labelPos.X - padding,
            labelPos.Y - padding,
            formattedText.Width + padding * 2,
            formattedText.Height + padding * 2);

        var bgBrush = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30));
        bgBrush.Freeze();
        dc.DrawRectangle(bgBrush, null, bgRect);

        // Draw text
        dc.DrawText(formattedText, labelPos);
    }

    private void DrawSnapLabel(DrawingContext dc, Point screenPos, string text, Brush brush)
    {
        var typeface = new Typeface("Segoe UI");
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Position label below and to the right of the snap point
        var labelPos = new Point(screenPos.X + 10, screenPos.Y + 5);

        // Draw background
        var padding = 3.0;
        var bgRect = new Rect(
            labelPos.X - padding,
            labelPos.Y - padding,
            formattedText.Width + padding * 2,
            formattedText.Height + padding * 2);

        var bgBrush = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30));
        bgBrush.Freeze();
        dc.DrawRectangle(bgBrush, null, bgRect);

        // Draw text
        dc.DrawText(formattedText, labelPos);
    }

    private void DrawDistanceLabel(DrawingContext dc, Point screenPos, double distance)
    {
        var text = distance.ToString("F2");
        var typeface = new Typeface("Segoe UI");
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14,
            Brushes.LimeGreen,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Draw background
        var padding = 4.0;
        var bgRect = new Rect(
            screenPos.X - formattedText.Width / 2 - padding,
            screenPos.Y - formattedText.Height / 2 - padding,
            formattedText.Width + padding * 2,
            formattedText.Height + padding * 2);

        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30));
        bgBrush.Freeze();
        dc.DrawRectangle(bgBrush, null, bgRect);

        // Draw text
        dc.DrawText(formattedText, new Point(
            screenPos.X - formattedText.Width / 2,
            screenPos.Y - formattedText.Height / 2));
    }

    private void DrawShapeHighlight(DrawingContext dc, long shapeId)
    {
        // Find the shape by ID
        var shape = _currentShapes.OfType<Shape>().FirstOrDefault(s => s.Id == shapeId);
        if (shape == null) return;

        // Get bounding box
        var bounds = shape.GetBounds();
        var minScreen = WorldToScreen(bounds.Min.X, bounds.Max.Y); // Y is inverted
        var maxScreen = WorldToScreen(bounds.Max.X, bounds.Min.Y);

        // Add padding in screen coordinates
        const double padding = 8;
        var highlightRect = new Rect(
            minScreen.X - padding,
            minScreen.Y - padding,
            (maxScreen.X - minScreen.X) + padding * 2,
            (maxScreen.Y - minScreen.Y) + padding * 2);

        // Create highlight brush from settings
        var highlightBrush = CreateHighlightBrush();

        // Draw highlight fill only (no stroke)
        dc.DrawRectangle(highlightBrush, null, highlightRect);
    }

    private Brush CreateHighlightBrush()
    {
        var settings = ApplicationSettings.Instance;
        try
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(settings.HighlightColor);
            var alpha = (byte)(settings.HighlightOpacity * 255 / 100);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            return brush;
        }
        catch
        {
            // Fallback to yellow with 40% opacity
            var brush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 0));
            brush.Freeze();
            return brush;
        }
    }

    private void DrawGrid(DrawingContext dc)
    {
        var gridPen = new Pen(GridBrush, 0.5);
        gridPen.Freeze();

        // Calculate adaptive spacing
        var spacing = CalculateAdaptiveSpacing();

        var topLeft = ScreenToWorld(0, 0);
        var bottomRight = ScreenToWorld(ActualWidth, ActualHeight);

        var startX = Math.Floor(topLeft.X / spacing) * spacing;
        var endX = Math.Ceiling(bottomRight.X / spacing) * spacing;
        var startY = Math.Floor(bottomRight.Y / spacing) * spacing;
        var endY = Math.Ceiling(topLeft.Y / spacing) * spacing;

        // Vertical lines
        for (var x = startX; x <= endX; x += spacing)
        {
            // Avoid drawing over the Y-axis (x=0) if possible, or let it draw over. 
            // The axis is drawn later so it will be on top anyway.
            if (Math.Abs(x) < 0.001) continue; 
            
            var screenX = WorldToScreen(x, 0).X;
            dc.DrawLine(gridPen, new Point(screenX, 0), new Point(screenX, ActualHeight));
        }

        // Horizontal lines
        for (var y = startY; y <= endY; y += spacing)
        {
            if (Math.Abs(y) < 0.001) continue;
            
            var screenY = WorldToScreen(0, y).Y;
            dc.DrawLine(gridPen, new Point(0, screenY), new Point(ActualWidth, screenY));
        }
    }

    private VXYZ SnapPointToGrid(double worldX, double worldY)
    {
        var spacing = CalculateAdaptiveSpacing();
        var snappedX = Math.Round(worldX / spacing) * spacing;
        var snappedY = Math.Round(worldY / spacing) * spacing;
        return new VXYZ(snappedX, snappedY);
    }

    private double CalculateAdaptiveSpacing()
    {
        // Target visual spacing in pixels (approx 50px)
        const double targetPixelSpacing = 50.0;

        // Calculate the theoretical world spacing to achieve target pixel spacing
        // world = pixels / scale
        double rawSpacing = targetPixelSpacing / _viewport.Scale;

        // Find the nearest "nice" interval: 1, 2, 5, 10, 20, 50, etc.
        double powerOf10 = Math.Pow(10, Math.Floor(Math.Log10(rawSpacing)));
        double normalized = rawSpacing / powerOf10;

        double niceSpacing;
        if (normalized >= 5.0)       niceSpacing = 5.0;
        else if (normalized >= 2.0)  niceSpacing = 2.0;
        else                         niceSpacing = 1.0;

        return niceSpacing * powerOf10;
    }

    private void DrawAxes(DrawingContext dc)
    {
        var xAxisPen = new Pen(XAxisBrush, 1.5);
        xAxisPen.Freeze();

        var yAxisPen = new Pen(YAxisBrush, 1.5);
        yAxisPen.Freeze();

        // X-axis (horizontal, red)
        var xAxisY = WorldToScreen(0, 0).Y;
        if (xAxisY >= 0 && xAxisY <= ActualHeight)
        {
            dc.DrawLine(xAxisPen, new Point(0, xAxisY), new Point(ActualWidth, xAxisY));
        }

        // Y-axis (vertical, green)
        var yAxisX = WorldToScreen(0, 0).X;
        if (yAxisX >= 0 && yAxisX <= ActualWidth)
        {
            dc.DrawLine(yAxisPen, new Point(yAxisX, 0), new Point(yAxisX, ActualHeight));
        }
    }

    private void DrawPoint(DrawingContext dc, VPoint point)
    {
        if (point.DrawFactor <= 0 || point.Opacity <= 0) return;

        var applyOpacity = point.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(point.Opacity);

        // Apply offset for move animation
        var screenPos = WorldToScreen(point.X + point.OffsetX, point.Y + point.OffsetY);
        var fill = GetCachedBrush(point.FillColor);
        var pen = GetShapePen(point.Color, point.LineWeight, point.LineType, point.LineTypeScale);

        if (DoodleSharp.ApplicationSettings.Instance.DrawPointAsPatch)
        {
            dc.DrawEllipse(fill, pen, screenPos, PointRadius, PointRadius);
        }
        else
        {
            dc.DrawEllipse(pen.Brush, pen, screenPos, 1.5, 1.5);
        }

        if (applyOpacity) dc.Pop();
    }

    private void DrawLine(DrawingContext dc, VLine line)
    {
        if (line.DrawFactor <= 0 || line.Opacity <= 0) return;

        var applyOpacity = line.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(line.Opacity);

        // Rotation is applied once for every shape type in DispatchShapeDraw — do not reintroduce
        // a per-shape transform here, or rotated lines will rotate twice.

        // Apply offset for move animation
        var offsetX = line.OffsetX;
        var offsetY = line.OffsetY;

        var start = WorldToScreen(line.Start.X + offsetX, line.Start.Y + offsetY);
        var end = WorldToScreen(line.End.X + offsetX, line.End.Y + offsetY);
        var pen = GetShapePen(line.Color, line.LineWeight, line.LineType, line.LineTypeScale);

        // Apply DrawFactor for animation (partial line drawing)
        if (line.DrawFactor < 1.0)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            end = new Point(start.X + dx * line.DrawFactor, start.Y + dy * line.DrawFactor);
        }

        dc.DrawLine(pen, start, end);

        if (applyOpacity) dc.Pop();
    }

    private void DrawXLine(DrawingContext dc, VXLine xline)
    {
        if (xline.DrawFactor <= 0 || xline.Opacity <= 0) return;

        var applyOpacity = xline.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(xline.Opacity);

        // Apply offset for move animation
        var offsetX = xline.OffsetX;
        var offsetY = xline.OffsetY;

        // Get the visible canvas bounds in world coordinates
        var (minWorld, maxWorld) = GetVisibleWorldBounds();

        // Calculate intersection of the infinite line with a large bounding box
        // Use the larger of the render extent or canvas bounds
        double extent = Math.Max(xline.RenderExtent, Math.Max(maxWorld.X - minWorld.X, maxWorld.Y - minWorld.Y) * 2);

        var p1 = xline.GetPointAtParameter(-extent);
        var p2 = xline.GetPointAtParameter(extent);

        var start = WorldToScreen(p1.X + offsetX, p1.Y + offsetY);
        var end = WorldToScreen(p2.X + offsetX, p2.Y + offsetY);
        var pen = GetShapePen(xline.Color, xline.LineWeight, xline.LineType, xline.LineTypeScale);

        // Apply DrawFactor for animation
        if (xline.DrawFactor < 1.0)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var midX = (start.X + end.X) / 2;
            var midY = (start.Y + end.Y) / 2;
            start = new Point(midX - dx * xline.DrawFactor / 2, midY - dy * xline.DrawFactor / 2);
            end = new Point(midX + dx * xline.DrawFactor / 2, midY + dy * xline.DrawFactor / 2);
        }

        dc.DrawLine(pen, start, end);

        if (applyOpacity) dc.Pop();
    }

    private void DrawRay(DrawingContext dc, VRay ray)
    {
        if (ray.DrawFactor <= 0 || ray.Opacity <= 0) return;

        var applyOpacity = ray.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(ray.Opacity);

        // Apply offset for move animation
        var offsetX = ray.OffsetX;
        var offsetY = ray.OffsetY;

        // Get the visible canvas bounds in world coordinates
        var (minWorld, maxWorld) = GetVisibleWorldBounds();

        // Calculate extent based on render extent or canvas size
        double extent = Math.Max(ray.RenderExtent, Math.Max(maxWorld.X - minWorld.X, maxWorld.Y - minWorld.Y) * 2);

        var p1 = ray.Origin;
        var p2 = ray.GetPointAtDistance(extent);

        var start = WorldToScreen(p1.X + offsetX, p1.Y + offsetY);
        var end = WorldToScreen(p2.X + offsetX, p2.Y + offsetY);
        var pen = GetShapePen(ray.Color, ray.LineWeight, ray.LineType, ray.LineTypeScale);

        // Apply DrawFactor for animation (draws from origin outward)
        if (ray.DrawFactor < 1.0)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            end = new Point(start.X + dx * ray.DrawFactor, start.Y + dy * ray.DrawFactor);
        }

        dc.DrawLine(pen, start, end);

        if (applyOpacity) dc.Pop();
    }

    private (VPoint min, VPoint max) GetVisibleWorldBounds()
    {
        var minScreen = new Point(0, 0);
        var maxScreen = new Point(ActualWidth, ActualHeight);
        var minWorld = ScreenToWorld(minScreen.X, minScreen.Y);
        var maxWorld = ScreenToWorld(maxScreen.X, maxScreen.Y);
        // Swap Y values since screen Y is inverted
        return (new VPoint(Math.Min(minWorld.X, maxWorld.X), Math.Min(minWorld.Y, maxWorld.Y)),
                new VPoint(Math.Max(minWorld.X, maxWorld.X), Math.Max(minWorld.Y, maxWorld.Y)));
    }

    private void DrawArc(DrawingContext dc, VArc arc)
    {
        if (arc.DrawFactor <= 0 || arc.Opacity <= 0) return;

        var applyOpacity = arc.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(arc.Opacity);

        // Apply offset for move animation
        var offsetX = arc.OffsetX;
        var offsetY = arc.OffsetY;

        var startAngleRad = arc.StartAngle * Math.PI / 180;

        // Apply DrawFactor - draw partial arc
        var effectiveEndAngle = arc.StartAngle + (arc.EndAngle - arc.StartAngle) * arc.DrawFactor;
        var endAngleRad = effectiveEndAngle * Math.PI / 180;

        var startWorldX = arc.Center.X + offsetX + arc.Radius * Math.Cos(startAngleRad);
        var startWorldY = arc.Center.Y + offsetY + arc.Radius * Math.Sin(startAngleRad);
        var endWorldX = arc.Center.X + offsetX + arc.Radius * Math.Cos(endAngleRad);
        var endWorldY = arc.Center.Y + offsetY + arc.Radius * Math.Sin(endAngleRad);

        var startScreen = WorldToScreen(startWorldX, startWorldY);
        var endScreen = WorldToScreen(endWorldX, endWorldY);

        // The arc spans |EndAngle - StartAngle|; a positive sweep is CCW in world
        // (Y-up), which stays CCW here because we pass world-derived endpoints and
        // WPF resolves the arc center from radius + endpoints + these flags.
        var sweep = effectiveEndAngle - arc.StartAngle;
        var isLargeArc = Math.Abs(sweep) > 180;
        var sweepDir = sweep >= 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

        var screenRadius = arc.Radius * _viewport.Scale;
        var pen = GetShapePen(arc.Color, arc.LineWeight, arc.LineType, arc.LineTypeScale);

        // Use StreamGeometry for better performance
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startScreen, false, false);
            ctx.ArcTo(endScreen, new Size(screenRadius, screenRadius), 0, isLargeArc, sweepDir, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(null, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    private void DrawCircle(DrawingContext dc, VCircle circle)
    {
        if (circle.DrawFactor <= 0 || circle.Opacity <= 0) return;

        var applyOpacity = circle.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(circle.Opacity);

        // Rotation is applied once in DispatchShapeDraw — see the note there.

        // Apply offset for move animation
        var offsetX = circle.OffsetX;
        var offsetY = circle.OffsetY;

        var centerScreen = WorldToScreen(circle.Center.X + offsetX, circle.Center.Y + offsetY);
        var screenRadius = circle.Radius * _viewport.Scale;
        var fill = GetCachedBrush(circle.FillColor);
        var pen = GetShapePen(circle.Color, circle.LineWeight, circle.LineType, circle.LineTypeScale);

        // Apply DrawFactor - draw as arc from 0 to DrawFactor*360 degrees
        if (circle.DrawFactor < 1.0)
        {
            var endAngle = 360.0 * circle.DrawFactor;
            var endAngleRad = endAngle * Math.PI / 180;

            var startWorldX = circle.Center.X + offsetX + circle.Radius;
            var startWorldY = circle.Center.Y + offsetY;
            var endWorldX = circle.Center.X + offsetX + circle.Radius * Math.Cos(endAngleRad);
            var endWorldY = circle.Center.Y + offsetY + circle.Radius * Math.Sin(endAngleRad);

            var startScreen = WorldToScreen(startWorldX, startWorldY);
            var endScreen = WorldToScreen(endWorldX, endWorldY);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startScreen, false, false);
                ctx.ArcTo(endScreen, new Size(screenRadius, screenRadius), 0, endAngle > 180, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        else
        {
            dc.DrawEllipse(fill, pen, centerScreen, screenRadius, screenRadius);
        }

        if (applyOpacity) dc.Pop();
    }

    private void DrawRectangle(DrawingContext dc, VRectangle rect)
    {
        if (rect.DrawFactor <= 0 || rect.Opacity <= 0) return;

        var applyOpacity = rect.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(rect.Opacity);

        // Apply offset for move animation
        var offsetX = rect.OffsetX;
        var offsetY = rect.OffsetY;

        var fill = GetCachedBrush(rect.FillColor);
        var pen = GetShapePen(rect.Color, rect.LineWeight, rect.LineType, rect.LineTypeScale);

        // If rectangle has internal rotation, draw as polygon
        if (Math.Abs(rect.RotationAngle) > 1e-9)
        {
            var vertices = rect.Vertices;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var first = WorldToScreen(vertices[0].X + offsetX, vertices[0].Y + offsetY);
                ctx.BeginFigure(first, fill != null, true);
                for (int i = 1; i < vertices.Count; i++)
                {
                    var pt = WorldToScreen(vertices[i].X + offsetX, vertices[i].Y + offsetY);
                    ctx.LineTo(pt, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);

            if (applyOpacity) dc.Pop();
            return;
        }

        // No rotation branch here: a non-zero RotationAngle returned above, drawn from the corner
        // geometry the setter rebuilt. Reaching this point means the angle is zero, so the transform
        // that used to sit here was an identity rotation — dead code that looked like coverage.

        var actualWidth = rect.Width;
        var actualHeight = rect.Height;
        var cornerX = rect.Corner.X + offsetX + (actualWidth < 0 ? actualWidth : 0);
        var cornerY = rect.Corner.Y + offsetY + (actualHeight > 0 ? actualHeight : 0);
        var corner = WorldToScreen(cornerX, cornerY);
        var screenWidth = Math.Abs(actualWidth) * _viewport.Scale;
        var screenHeight = Math.Abs(actualHeight) * _viewport.Scale;

        // Apply DrawFactor - draw partial rectangle outline
        if (rect.DrawFactor < 1.0)
        {
            var absWidth = Math.Abs(rect.Width);
            var absHeight = Math.Abs(rect.Height);
            var perimeter = 2 * (absWidth + absHeight);
            var drawLength = perimeter * rect.DrawFactor;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(corner, false, false);
                var remaining = drawLength;

                // Right edge
                if (remaining > 0)
                {
                    var len = Math.Min(remaining, screenWidth);
                    ctx.LineTo(new Point(corner.X + len, corner.Y), true, false);
                    remaining -= absWidth;
                }
                // Bottom edge
                if (remaining > 0)
                {
                    var len = Math.Min(remaining, screenHeight);
                    ctx.LineTo(new Point(corner.X + screenWidth, corner.Y + len), true, false);
                    remaining -= absHeight;
                }
                // Left edge
                if (remaining > 0)
                {
                    var len = Math.Min(remaining, screenWidth);
                    ctx.LineTo(new Point(corner.X + screenWidth - len, corner.Y + screenHeight), true, false);
                    remaining -= absWidth;
                }
                // Top edge
                if (remaining > 0)
                {
                    var len = Math.Min(remaining, screenHeight);
                    ctx.LineTo(new Point(corner.X, corner.Y + screenHeight - len), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        else
        {
            dc.DrawRectangle(fill, pen, new Rect(corner.X, corner.Y, screenWidth, screenHeight));
        }

        if (applyOpacity) dc.Pop();
    }

    private void DrawEllipse(DrawingContext dc, VEllipse ellipse)
    {
        if (ellipse.DrawFactor <= 0 || ellipse.Opacity <= 0) return;

        var applyOpacity = ellipse.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(ellipse.Opacity);

        var centerScreen = WorldToScreen(ellipse.Center.X, ellipse.Center.Y);
        var screenRadiusX = ellipse.RadiusX * _viewport.Scale;
        var screenRadiusY = ellipse.RadiusY * _viewport.Scale;
        var fill = GetCachedBrush(ellipse.FillColor);
        var pen = GetShapePen(ellipse.Color, ellipse.LineWeight, ellipse.LineType, ellipse.LineTypeScale);

        dc.DrawEllipse(fill, pen, centerScreen, screenRadiusX, screenRadiusY);

        if (applyOpacity) dc.Pop();
    }

    private void DrawPolygon(DrawingContext dc, VPolygon polygon)
    {
        if (polygon.Points.Count < 3 || polygon.DrawFactor <= 0 || polygon.Opacity <= 0) return;

        var applyOpacity = polygon.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(polygon.Opacity);

        // Apply offset for move animation
        var offsetX = polygon.OffsetX;
        var offsetY = polygon.OffsetY;

        var fill = GetCachedBrush(polygon.FillColor);
        var pen = GetShapePen(polygon.Color, polygon.LineWeight, polygon.LineType, polygon.LineTypeScale);

        // Apply DrawFactor - draw partial polygon outline
        var totalSegments = polygon.Points.Count; // includes closing segment
        var segmentsToDraw = polygon.DrawFactor * totalSegments;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var firstPoint = WorldToScreen(polygon.Points[0].X + offsetX, polygon.Points[0].Y + offsetY);
            ctx.BeginFigure(firstPoint, polygon.DrawFactor >= 1.0, polygon.DrawFactor >= 1.0);

            int fullSegments = (int)segmentsToDraw;
            double partialFraction = segmentsToDraw - fullSegments;

            // Draw full segments
            for (int i = 1; i <= fullSegments && i <= polygon.Points.Count; i++)
            {
                var idx = i % polygon.Points.Count;
                var pt = WorldToScreen(polygon.Points[idx].X + offsetX, polygon.Points[idx].Y + offsetY);
                ctx.LineTo(pt, true, false);
            }

            // Draw partial segment if needed
            if (partialFraction > 0 && fullSegments < polygon.Points.Count)
            {
                var prevIdx = fullSegments % polygon.Points.Count;
                var nextIdx = (fullSegments + 1) % polygon.Points.Count;
                var prevPt = WorldToScreen(polygon.Points[prevIdx].X + offsetX, polygon.Points[prevIdx].Y + offsetY);
                var nextPt = WorldToScreen(polygon.Points[nextIdx].X + offsetX, polygon.Points[nextIdx].Y + offsetY);
                var partialPt = new Point(
                    prevPt.X + (nextPt.X - prevPt.X) * partialFraction,
                    prevPt.Y + (nextPt.Y - prevPt.Y) * partialFraction);
                ctx.LineTo(partialPt, true, false);
            }
        }
        geometry.Freeze();

        dc.DrawGeometry(polygon.DrawFactor >= 1.0 ? fill : null, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    private void DrawPolyline(DrawingContext dc, VPolyline polyline)
    {
        if (polyline.Points.Count < 2 || polyline.DrawFactor <= 0 || polyline.Opacity <= 0) return;

        var applyOpacity = polyline.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(polyline.Opacity);

        // Apply offset for move animation
        var offsetX = polyline.OffsetX;
        var offsetY = polyline.OffsetY;

        var pen = GetShapePen(polyline.Color, polyline.LineWeight, polyline.LineType, polyline.LineTypeScale);

        // Apply DrawFactor - draw partial polyline
        var totalSegments = polyline.Points.Count - 1;
        var segmentsToDraw = polyline.DrawFactor * totalSegments;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var firstPoint = WorldToScreen(polyline.Points[0].X + offsetX, polyline.Points[0].Y + offsetY);
            ctx.BeginFigure(firstPoint, false, false);

            for (int i = 1; i < polyline.Points.Count && i <= segmentsToDraw + 1; i++)
            {
                var pt = WorldToScreen(polyline.Points[i].X + offsetX, polyline.Points[i].Y + offsetY);

                // Partial last segment
                if (i > segmentsToDraw && i <= segmentsToDraw + 1)
                {
                    var prevPt = WorldToScreen(polyline.Points[i - 1].X + offsetX, polyline.Points[i - 1].Y + offsetY);
                    var fraction = segmentsToDraw - (i - 1);
                    var partialPt = new Point(
                        prevPt.X + (pt.X - prevPt.X) * fraction,
                        prevPt.Y + (pt.Y - prevPt.Y) * fraction);
                    ctx.LineTo(partialPt, true, false);
                }
                else
                {
                    ctx.LineTo(pt, true, false);
                }
            }
        }
        geometry.Freeze();

        dc.DrawGeometry(null, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    /// <summary>
    /// Typefaces, cached by font and weight.
    ///
    /// <para>
    /// <c>new FontFamily(name)</c> hits WPF's font-resolution machinery, and <c>Typeface</c> wraps
    /// it — both were being constructed per text shape per frame, alongside a
    /// <c>VisualTreeHelper.GetDpi</c> call that walks the visual tree. On a benchmark scene with a
    /// label per cell that was the single largest remaining allocation source.
    /// </para>
    ///
    /// <para>
    /// The <c>FormattedText</c> itself still has to be built per draw: it bakes in the font size,
    /// which changes with zoom, and the brush. Caching that too needs a key on size and colour and
    /// belongs with the text layer rather than here.
    /// </para>
    /// </summary>
    /// <summary>
    /// WPF's own hard limit on <c>FormattedText</c>'s em size, minus a margin. Exceeding it throws
    /// <c>ArgumentOutOfRangeException</c> from inside the render pass.
    /// </summary>
    private const double MaxFontSize = 35000;

    private static readonly Dictionary<(VFont font, VFontWeight weight), Typeface> _typefaceCache = new();

    /// <summary>
    /// Pixel density for <see cref="FormattedText"/>, cached because every <c>VText</c> in a frame
    /// needs it. Invalidated in <see cref="OnDpiChanged"/> — see the note there.
    /// </summary>
    private double? _cachedPixelsPerDip;

    /// <summary>
    /// Drops the cached pixel density when the canvas moves to a monitor with different scaling.
    ///
    /// <para>
    /// This only started mattering when panels became dockable. Previously the canvas could only
    /// change DPI along with the whole window, which is rare; now it can be floated onto a 150%
    /// second monitor on its own, and a stale density renders every glyph at the wrong size — subtly
    /// blurry rather than obviously broken, which is the kind of bug that goes unreported for a long
    /// time.
    /// </para>
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        _cachedPixelsPerDip = null;
        RedrawAll();
    }

    private static Typeface GetCachedTypeface(VFont font, VFontWeight weight)
    {
        var key = (font, weight);
        if (_typefaceCache.TryGetValue(key, out var cached)) return cached;

        var typeface = new Typeface(
            new FontFamily(GetFontFamilyName(font)),
            FontStyles.Normal,
            weight == VFontWeight.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        _typefaceCache[key] = typeface;
        return typeface;
    }

    private void DrawText(DrawingContext dc, VText text)
    {
        if (string.IsNullOrEmpty(text.Content) || text.DrawFactor <= 0 || text.Opacity <= 0)
            return;

        var applyOpacity = text.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(text.Opacity);

        var screenPos = WorldToScreen(text.Location.X, text.Location.Y);
        var brush = GetCachedBrush(text.Color);

        // Scale font size with zoom, but keep it readable -- and keep it inside what WPF will
        // accept. FormattedText throws above roughly 35,791 em, and zooming far enough into a
        // drawing reaches that: the exception escapes the render pass and takes the frame, and with
        // it the process. A glyph that large fills the viewport many times over, so clamping costs
        // nothing visible.
        var fontSize = text.Height * _viewport.Scale;
        fontSize = Math.Clamp(fontSize, 6, MaxFontSize);

        var typeface = GetCachedTypeface(text.Font, text.FontWeight);
        var dpi = _cachedPixelsPerDip ??= VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formattedText = new FormattedText(
            text.Content,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            dpi);

        // Anchor offset uses full text size so the layout stays put while characters reveal.
        var (anchorOffsetX, anchorOffsetY) = text.GetAnchorOffset(
            formattedText.Width / _viewport.Scale,
            formattedText.Height / _viewport.Scale);
        var drawX = screenPos.X + anchorOffsetX * _viewport.Scale;
        // Y is inverted in screen coords, so negate the world offsetY
        var drawY = screenPos.Y - formattedText.Height - anchorOffsetY * _viewport.Scale;

        // Angle is CCW in world (Y-up); WPF screen Y is down, so negate for RotateTransform.
        bool applyRotation = text.Angle != 0;
        if (applyRotation)
            dc.PushTransform(new RotateTransform(-text.Angle, screenPos.X, screenPos.Y));

        // The mask goes down inside the same rotation, immediately before the glyphs, so it always
        // sits under its own text and over whatever the text crosses. Its padding is a fraction of
        // the font size rather than a fixed number of pixels, so a label keeps the same visual
        // breathing room at any height or zoom. It is deliberately drawn at full width even while
        // DrawFactor is revealing the characters — the layout is already pinned to the full text
        // size a few lines above, and a mask that grew with the reveal would slide the background
        // out from under the letters already on screen.
        if (text.Mask)
        {
            var pad = text.MaskOffset * fontSize;
            // A null MaskColor means "the canvas background", and it is resolved HERE rather than
            // captured on the text, so a label keeps blending in after the background is changed
            // — no re-run, nothing to invalidate.
            var maskBrush = string.IsNullOrEmpty(text.MaskColor)
                ? CanvasBackground
                : GetCachedBrush(text.MaskColor);
            dc.DrawRectangle(maskBrush, null, new Rect(
                drawX - pad,
                drawY - pad,
                formattedText.Width + 2 * pad,
                formattedText.Height + 2 * pad));
        }

        if (text.DrawFactor < 1.0)
        {
            int visibleCount = (int)Math.Floor(text.DrawFactor * text.Content.Length);
            if (visibleCount > 0)
            {
                var partial = new FormattedText(
                    text.Content.Substring(0, visibleCount),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush,
                    dpi);
                dc.DrawText(partial, new Point(drawX, drawY));
            }
        }
        else
        {
            dc.DrawText(formattedText, new Point(drawX, drawY));
        }

        if (applyRotation) dc.Pop();
        if (applyOpacity) dc.Pop();
    }

    private static string GetFontFamilyName(VFont font) => font switch
    {
        VFont.Arial => "Arial",
        VFont.TimesNewRoman => "Times New Roman",
        VFont.CourierNew => "Courier New",
        VFont.Verdana => "Verdana",
        VFont.Georgia => "Georgia",
        VFont.Tahoma => "Tahoma",
        VFont.TrebuchetMS => "Trebuchet MS",
        VFont.Consolas => "Consolas",
        VFont.Calibri => "Calibri",
        VFont.Cambria => "Cambria",
        VFont.SegoeUI => "Segoe UI",
        VFont.ComicSansMS => "Comic Sans MS",
        VFont.Impact => "Impact",
        VFont.LucidaConsole => "Lucida Console",
        _ => "Arial"
    };

    private void DrawBezier(DrawingContext dc, VBezier bezier)
    {
        if (bezier.DrawFactor <= 0 || bezier.Opacity <= 0) return;

        var applyOpacity = bezier.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(bezier.Opacity);

        // Apply offset for move animation
        var offsetX = bezier.OffsetX;
        var offsetY = bezier.OffsetY;

        var pen = GetShapePen(bezier.Color, bezier.LineWeight, bezier.LineType, bezier.LineTypeScale);
        var points = bezier.GetRenderPoints();
        if (points.Count < 2)
        {
            if (applyOpacity) dc.Pop();
            return;
        }

        // Apply DrawFactor - draw partial bezier
        var pointsToDraw = (int)Math.Ceiling(points.Count * bezier.DrawFactor);
        pointsToDraw = Math.Max(2, Math.Min(pointsToDraw, points.Count));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = WorldToScreen(points[0].X + offsetX, points[0].Y + offsetY);
            ctx.BeginFigure(first, false, false);
            for (int i = 1; i < pointsToDraw; i++)
            {
                var pt = WorldToScreen(points[i].X + offsetX, points[i].Y + offsetY);
                ctx.LineTo(pt, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    private void DrawSpline(DrawingContext dc, VSpline spline)
    {
        if (spline.DrawFactor <= 0 || spline.Opacity <= 0) return;

        var applyOpacity = spline.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(spline.Opacity);

        var offsetX = spline.OffsetX;
        var offsetY = spline.OffsetY;

        var pen = GetShapePen(spline.Color, spline.LineWeight, spline.LineType, spline.LineTypeScale);
        var points = spline.GetRenderPoints();
        if (points.Count < 2)
        {
            if (applyOpacity) dc.Pop();
            return;
        }

        var totalSegments = points.Count - 1;
        var segmentsToDraw = spline.DrawFactor * totalSegments;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = WorldToScreen(points[0].X + offsetX, points[0].Y + offsetY);
            ctx.BeginFigure(first, false, false);
            for (int i = 1; i < points.Count && i <= segmentsToDraw + 1; i++)
            {
                var pt = WorldToScreen(points[i].X + offsetX, points[i].Y + offsetY);

                if (i > segmentsToDraw && i <= segmentsToDraw + 1)
                {
                    var prevPt = WorldToScreen(points[i - 1].X + offsetX, points[i - 1].Y + offsetY);
                    var fraction = segmentsToDraw - (i - 1);
                    var partialPt = new Point(
                        prevPt.X + (pt.X - prevPt.X) * fraction,
                        prevPt.Y + (pt.Y - prevPt.Y) * fraction);
                    ctx.LineTo(partialPt, true, false);
                }
                else
                {
                    ctx.LineTo(pt, true, false);
                }
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    private void DrawArrow(DrawingContext dc, VArrow arrow)
    {
        if (arrow.DrawFactor <= 0 || arrow.Opacity <= 0) return;

        var applyOpacity = arrow.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(arrow.Opacity);

        // Rotation is applied once in DispatchShapeDraw — see the note there.

        var pen = GetShapePen(arrow.Color, arrow.LineWeight, arrow.LineType, arrow.LineTypeScale);
        var brush = GetCachedBrush(arrow.Color);  // Use stroke color for filled arrowhead
        var start = WorldToScreen(arrow.Start.X + arrow.OffsetX, arrow.Start.Y + arrow.OffsetY);
        var fullEnd = WorldToScreen(arrow.End.X + arrow.OffsetX, arrow.End.Y + arrow.OffsetY);

        // Apply DrawFactor for animation (partial arrow drawing)
        Point end = fullEnd;
        if (arrow.DrawFactor < 1.0)
        {
            // Calculate partial end point
            var dx = fullEnd.X - start.X;
            var dy = fullEnd.Y - start.Y;
            end = new Point(start.X + dx * arrow.DrawFactor, start.Y + dy * arrow.DrawFactor);
        }

        // Draw main line
        dc.DrawLine(pen, start, end);

        // Draw filled end arrowhead (at the current end position)
        // Calculate arrowhead based on current draw progress
        var arrowDirX = arrow.End.X - arrow.Start.X;
        var arrowDirY = arrow.End.Y - arrow.Start.Y;
        var currentEndX = arrow.Start.X + arrow.OffsetX + arrowDirX * arrow.DrawFactor;
        var currentEndY = arrow.Start.Y + arrow.OffsetY + arrowDirY * arrow.DrawFactor;

        // Get arrowhead wings relative to current end position. The geometry comes from VArrow so
        // that HeadAngle is honoured here exactly as it is by the tessellator and the PDF exporter —
        // this used to recompute a fixed HeadLength/6 half-width and ignore HeadAngle entirely.
        var length = Math.Sqrt(arrowDirX * arrowDirX + arrowDirY * arrowDirY);
        if (length > 0)
        {
            var currentTip = new VXYZ(currentEndX, currentEndY);
            var shaftFrom = new VXYZ(currentEndX - arrowDirX, currentEndY - arrowDirY);
            var (wing1, wing2) = arrow.GetArrowheadPoints(currentTip, shaftFrom);

            var w1 = WorldToScreen(wing1.X, wing1.Y);
            var w2 = WorldToScreen(wing2.X, wing2.Y);

            var arrowHead = new StreamGeometry();
            using (var ctx = arrowHead.Open())
            {
                ctx.BeginFigure(end, true, true);  // Start at tip, filled, closed
                ctx.LineTo(w1, true, false);
                ctx.LineTo(w2, true, false);
            }
            arrowHead.Freeze();
            dc.DrawGeometry(brush, null, arrowHead);
        }

        // Draw start arrowhead if double-ended (only when fully drawn)
        if (arrow.DoubleEnded && arrow.DrawFactor >= 1.0)
        {
            var (sw1, sw2) = arrow.GetStartArrowhead();
            var sw1Screen = WorldToScreen(sw1.X + arrow.OffsetX, sw1.Y + arrow.OffsetY);
            var sw2Screen = WorldToScreen(sw2.X + arrow.OffsetX, sw2.Y + arrow.OffsetY);
            var startHead = new StreamGeometry();
            using (var ctx = startHead.Open())
            {
                ctx.BeginFigure(start, true, true);  // Start at tip, filled, closed
                ctx.LineTo(sw1Screen, true, false);
                ctx.LineTo(sw2Screen, true, false);
            }
            startHead.Freeze();
            dc.DrawGeometry(brush, null, startHead);
        }

        if (applyOpacity) dc.Pop();
    }

    private void DrawRadialDimension(DrawingContext dc, VRadialDimension dim)
    {
        if (dim.DrawFactor <= 0 || dim.Opacity <= 0) return;

        var applyOpacity = dim.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(dim.Opacity);

        var dimLineColor = dim.DimensionLineColor ?? dim.Color;
        var textColor = dim.TextColor ?? dim.Color;

        var dimLinePen = GetShapePen(dimLineColor, dim.LineWeight, dim.LineType, dim.LineTypeScale);
        var dimLineBrush = GetCachedBrush(dimLineColor);
        var textBrush = GetCachedBrush(textColor);

        var (leaderStart, leaderEnd, textPos) = dim.GetDimensionGeometry();

        // Create formatted text
        var fontSize = dim.TextHeight * _viewport.Scale;
        fontSize = Math.Max(fontSize, 8);
        var typeface = new Typeface("Segoe UI");
        var formattedText = new FormattedText(
            dim.DisplayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Draw leader line with gap for text
        var ls = WorldToScreen(leaderStart.X, leaderStart.Y);
        var le = WorldToScreen(leaderEnd.X, leaderEnd.Y);

        var dimDx = leaderEnd.X - leaderStart.X;
        var dimDy = leaderEnd.Y - leaderStart.Y;
        var dimLength = Math.Sqrt(dimDx * dimDx + dimDy * dimDy);

        if (dimLength > 1e-10)
        {
            var dirX = dimDx / dimLength;
            var dirY = dimDy / dimLength;

            var textWorldWidth = formattedText.Width / _viewport.Scale;
            var padding = textWorldWidth * 0.15;
            var halfGap = textWorldWidth / 2 + padding;

            var midX = (leaderStart.X + leaderEnd.X) / 2;
            var midY = (leaderStart.Y + leaderEnd.Y) / 2;

            var gs = WorldToScreen(midX - dirX * halfGap, midY - dirY * halfGap);
            var ge = WorldToScreen(midX + dirX * halfGap, midY + dirY * halfGap);

            dc.DrawLine(dimLinePen, ls, gs);
            dc.DrawLine(dimLinePen, ge, le);
        }
        else
        {
            dc.DrawLine(dimLinePen, ls, le);
        }

        // Arrowhead at circumference point (leaderEnd)
        DrawDimensionArrowhead(dc, dimLineBrush, dimLinePen, leaderEnd, leaderStart, dim.ArrowSize);

        // For diameter mode, also draw arrowhead at the opposite side
        if (dim.ShowDiameter)
        {
            DrawDimensionArrowhead(dc, dimLineBrush, dimLinePen, leaderStart, leaderEnd, dim.ArrowSize);
        }

        // Draw text
        var tp = WorldToScreen(textPos.X, textPos.Y);
        var textOrigin = new Point(tp.X - formattedText.Width / 2, tp.Y - formattedText.Height / 2);

        if (dim.TextBackgroundOpaque)
        {
            var bgRect = new Rect(textOrigin.X - 2, textOrigin.Y - 1,
                formattedText.Width + 4, formattedText.Height + 2);
            dc.DrawRectangle(CanvasBackground, null, bgRect);
        }

        dc.DrawText(formattedText, textOrigin);

        if (applyOpacity) dc.Pop();
    }

    private void DrawDimension(DrawingContext dc, VDimension dim)
    {
        if (dim.DrawFactor <= 0 || dim.Opacity <= 0) return;

        var applyOpacity = dim.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(dim.Opacity);

        // Per-element colors: fall back to base Color when specific color is null
        var dimLineColor = dim.DimensionLineColor ?? dim.Color;
        var extLineColor = dim.ExtensionLineColor ?? dim.Color;
        var textColor = dim.TextColor ?? dim.Color;

        var dimLinePen = GetShapePen(dimLineColor, dim.LineWeight, dim.LineType, dim.LineTypeScale);
        var dimLineBrush = GetCachedBrush(dimLineColor);
        var extLinePen = GetShapePen(extLineColor, dim.LineWeight, dim.LineType, dim.LineTypeScale);
        var textBrush = GetCachedBrush(textColor);

        var (dimStart, dimEnd, textPos, ext1Start, ext1End, ext2Start, ext2End) = dim.GetDimensionGeometry();

        // Create formatted text first to know its width for the line gap
        var fontSize = dim.TextHeight * _viewport.Scale;
        fontSize = Math.Max(fontSize, 8);
        var typeface = new Typeface("Segoe UI");
        var formattedText = new FormattedText(
            dim.DisplayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Compute dimension line direction
        var dimDx = dimEnd.X - dimStart.X;
        var dimDy = dimEnd.Y - dimStart.Y;
        var dimLength = Math.Sqrt(dimDx * dimDx + dimDy * dimDy);

        // Draw dimension line split around text gap (unless suppressed)
        if (!dim.SuppressDimensionLine)
        {
            var ds = WorldToScreen(dimStart.X, dimStart.Y);
            var de = WorldToScreen(dimEnd.X, dimEnd.Y);

            if (dimLength > 1e-10)
            {
                var dirX = dimDx / dimLength;
                var dirY = dimDy / dimLength;

                // Text width in world units, plus padding on each side
                var textWorldWidth = formattedText.Width / _viewport.Scale;
                var padding = textWorldWidth * 0.15;
                var halfGap = textWorldWidth / 2 + padding;

                // Gap center is at the midpoint of the dimension line
                var midX = (dimStart.X + dimEnd.X) / 2;
                var midY = (dimStart.Y + dimEnd.Y) / 2;

                var gapStartX = midX - dirX * halfGap;
                var gapStartY = midY - dirY * halfGap;
                var gapEndX = midX + dirX * halfGap;
                var gapEndY = midY + dirY * halfGap;

                var gs = WorldToScreen(gapStartX, gapStartY);
                var ge = WorldToScreen(gapEndX, gapEndY);

                dc.DrawLine(dimLinePen, ds, gs);
                dc.DrawLine(dimLinePen, ge, de);
            }
            else
            {
                dc.DrawLine(dimLinePen, ds, de);
            }

            // Draw arrowheads at both ends of dimension line
            DrawDimensionArrowhead(dc, dimLineBrush, dimLinePen, dimStart, dimEnd, dim.ArrowSize);
            DrawDimensionArrowhead(dc, dimLineBrush, dimLinePen, dimEnd, dimStart, dim.ArrowSize);
        }

        // Draw extension lines (respecting suppress flags)
        if (!dim.SuppressExtLine1)
            dc.DrawLine(extLinePen, WorldToScreen(ext1Start.X, ext1Start.Y), WorldToScreen(ext1End.X, ext1End.Y));
        if (!dim.SuppressExtLine2)
            dc.DrawLine(extLinePen, WorldToScreen(ext2Start.X, ext2Start.Y), WorldToScreen(ext2End.X, ext2End.Y));

        // Draw text (with optional opaque background)
        var tp = WorldToScreen(textPos.X, textPos.Y);
        var textOrigin = new Point(tp.X - formattedText.Width / 2, tp.Y - formattedText.Height / 2);

        if (dim.TextBackgroundOpaque)
        {
            var bgRect = new Rect(textOrigin.X - 2, textOrigin.Y - 1,
                formattedText.Width + 4, formattedText.Height + 2);
            dc.DrawRectangle(CanvasBackground, null, bgRect);
        }

        dc.DrawText(formattedText, textOrigin);

        if (applyOpacity) dc.Pop();
    }

    /// <summary>
    /// Draws a filled triangular arrowhead at tipPoint, pointing from tailPoint toward tipPoint.
    /// </summary>
    private void DrawDimensionArrowhead(DrawingContext dc, Brush brush, Pen pen,
        VXYZ tipPoint, VXYZ tailPoint, double arrowSize)
    {
        // Shared geometry, so a dimension's arrowheads are the same width here as on the raster and
        // GPU backends and in an export. This used to use a fixed arrowSize/6 half-width (≈9.5°)
        // while the tessellator drew them at 20°.
        var (wing1, wing2) = VArrow.ArrowheadWings(
            tipPoint, tailPoint, arrowSize, VDimension.DimensionArrowAngleDegrees);
        if (wing1.IsAlmostEqualTo(tipPoint) && wing2.IsAlmostEqualTo(tipPoint)) return;

        var tip = WorldToScreen(tipPoint.X, tipPoint.Y);
        var w1 = WorldToScreen(wing1.X, wing1.Y);
        var w2 = WorldToScreen(wing2.X, wing2.Y);

        var arrowHead = new StreamGeometry();
        using (var ctx = arrowHead.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(w1, true, false);
            ctx.LineTo(w2, true, false);
        }
        arrowHead.Freeze();
        dc.DrawGeometry(brush, null, arrowHead);
    }

    private void DrawGroup(DrawingContext dc, VGroup group)
    {
        if (group.DrawFactor <= 0 || group.Opacity <= 0) return;

        var applyOpacity = group.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(group.Opacity);

        // Apply group-level offset (Move/Path animations drive group.OffsetX/OffsetY).
        // Screen-Y is inverted, so negate the Y component.
        var applyOffset = group.OffsetX != 0 || group.OffsetY != 0;
        if (applyOffset)
            dc.PushTransform(new TranslateTransform(group.OffsetX * _viewport.Scale, -group.OffsetY * _viewport.Scale));

        foreach (var shape in group.Shapes)
        {
            DrawShape(dc, shape);
        }

        if (applyOffset) dc.Pop();
        if (applyOpacity) dc.Pop();
    }

    /// <summary>
    /// Draws a single shape using the appropriate method based on its type.
    /// Used for rendering child shapes within groups.
    /// </summary>
    private void DrawShape(DrawingContext dc, Shape shape)
    {
        switch (shape)
        {
            case VPoint point:
                DrawPoint(dc, point);
                break;
            case VLine line:
                DrawLine(dc, line);
                break;
            case VArc arc:
                DrawArc(dc, arc);
                break;
            case VCircle circle:
                DrawCircle(dc, circle);
                break;
            case VRectangle rect:
                DrawRectangle(dc, rect);
                break;
            case VEllipse ellipse:
                DrawEllipse(dc, ellipse);
                break;
            case VPolygon polygon:
                DrawPolygon(dc, polygon);
                break;
            case VPolyline polyline:
                DrawPolyline(dc, polyline);
                break;
            case VText text:
                DrawText(dc, text);
                break;
            case VBezier bezier:
                DrawBezier(dc, bezier);
                break;
            case VSpline spline:
                DrawSpline(dc, spline);
                break;
            case VArrow arrow:
                DrawArrow(dc, arrow);
                break;
            case VRadialDimension radDim:
                DrawRadialDimension(dc, radDim);
                break;
            case VDimension dim:
                DrawDimension(dc, dim);
                break;
            case VGroup nestedGroup:
                DrawGroup(dc, nestedGroup);
                break;
            case Region region:
                DrawRegion(dc, region);
                break;
            case VHatch hatch:
                DrawHatch(dc, hatch);
                break;
        }
    }

    private void DrawRegion(DrawingContext dc, Region region)
    {
        if (region.OuterLoop.Count == 0 || region.DrawFactor <= 0 || region.Opacity <= 0) return;

        var applyOpacity = region.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(region.Opacity);

        var offsetX = region.OffsetX;
        var offsetY = region.OffsetY;

        var fill = GetCachedBrush(region.FillColor);
        var pen = GetShapePen(region.Color, region.LineWeight, region.LineType, region.LineTypeScale);

        // GetCachedOutline, not SampleLoop: sampling a region walks every edge through
        // ICurve.Divide, which allocates a VXYZ per point and — for beziers and splines — walks the
        // curve a few hundred times internally to parameterise by arc length. That was happening
        // per region, per frame.
        //
        // The segment count is chosen from the region's size on screen rather than fixed at 32: a
        // region the size of a postage stamp does not need 32 segments an edge, and one filling the
        // viewport visibly needs more.
        var regionBounds = region.GetBounds();
        var radiusPx = Math.Max(regionBounds.Width, regionBounds.Height) * 0.5 * _viewport.Scale;
        var segments = Rendering.LodPolicy.SegmentsForRadius(radiusPx);
        region.GetCachedOutline(segments, out var outerPoints, out var holeLoops);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (outerPoints.Count >= 3)
            {
                var firstPt = WorldToScreen(outerPoints[0].X + offsetX, outerPoints[0].Y + offsetY);
                ctx.BeginFigure(firstPt, true, true);
                for (int i = 1; i < outerPoints.Count; i++)
                {
                    var pt = WorldToScreen(outerPoints[i].X + offsetX, outerPoints[i].Y + offsetY);
                    ctx.LineTo(pt, true, false);
                }
            }

            // Draw holes (as separate figures wound in opposite direction)
            foreach (var holePoints in holeLoops)
            {
                if (holePoints.Count >= 3)
                {
                    var firstHolePt = WorldToScreen(holePoints[0].X + offsetX, holePoints[0].Y + offsetY);
                    ctx.BeginFigure(firstHolePt, true, true);
                    for (int i = 1; i < holePoints.Count; i++)
                    {
                        var pt = WorldToScreen(holePoints[i].X + offsetX, holePoints[i].Y + offsetY);
                        ctx.LineTo(pt, true, false);
                    }
                }
            }
        }
        geometry.Freeze();

        dc.DrawGeometry(fill, pen, geometry);

        if (applyOpacity) dc.Pop();
    }

    private void DrawHatch(DrawingContext dc, VHatch hatch)
    {
        if (hatch.Boundary.Count < 3 || hatch.DrawFactor <= 0 || hatch.Opacity <= 0) return;

        var applyOpacity = hatch.Opacity < 1.0;
        if (applyOpacity) dc.PushOpacity(hatch.Opacity);

        var offsetX = hatch.OffsetX;
        var offsetY = hatch.OffsetY;

        var pen = GetShapePen(hatch.Color, hatch.LineWeight, LineType.Continuous, 1.0);

        // Generate hatch lines and draw them
        // GetCachedLines, not GenerateLines: the latter regenerates the whole pattern and hands
        // back a fresh list. Doing that once per hatch per frame cost 11.5 ms and 146 MB of
        // allocation per frame on a benchmark scene with a few hundred hatches.
        var lines = hatch.GetCachedLines();

        // Pattern-level detail. A hatch is the one shape whose cost is unbounded by its size on
        // screen: the generator caps each pattern family at 10,000 segments, so a thumbnail-sized
        // parcel can still submit tens of thousands of strokes. Once the pattern is denser than the
        // display can resolve, every one of those strokes lands on a pixel another stroke already
        // covered — the user sees a solid block, and pays thousands of draw calls for it.
        //
        // So below a threshold density, draw the block directly. This is what AutoCAD does, and it
        // is the difference between a hatched drawing being usable when zoomed out and not.
        var screenArea = EstimateScreenArea(hatch, offsetX, offsetY);
        if (lines.Count > MinLinesForHatchFill && screenArea > 0
            && lines.Count / screenArea > MaxHatchLinesPerPixel)
        {
            DrawHatchAsSolid(dc, hatch, offsetX, offsetY);
            if (applyOpacity) dc.Pop();
            return;
        }

        foreach (var (start, end) in lines)
        {
            var p1 = WorldToScreen(start.X + offsetX, start.Y + offsetY);
            var p2 = WorldToScreen(end.X + offsetX, end.Y + offsetY);

            // Skip degenerate lines (dots become small circles)
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            if (dx * dx + dy * dy < 0.5)
            {
                dc.DrawEllipse(GetCachedBrush(hatch.Color), null, p1, 0.5, 0.5);
                continue;
            }

            dc.DrawLine(pen, p1, p2);
        }

        if (applyOpacity) dc.Pop();
    }

    /// <summary>
    /// Frame time above which the vector path is judged too slow and the rasterizer takes over.
    /// Set well under a 60 Hz budget so the switch happens before the user perceives a stall, but
    /// above the rasterizer's own fixed overhead so it is never chosen for a frame it would lose.
    /// </summary>
    private const double RasterSwitchUpMs = 8.0;

    /// <summary>
    /// Visible-shape count below which the vector path is judged cheap enough to return to. A count
    /// rather than a time, because the raster path's cost is dominated by its fixed per-frame buffer
    /// work and so tells you nothing about what the vector path would have cost.
    /// </summary>
    private const int RasterSwitchDownShapes = 1_500;

    private bool _rasterActive;

    /// <summary>
    /// Picks a backend for this frame.
    ///
    /// <para>
    /// Neither is right as a fixed choice. The rasterizer pays about 2 ms a frame at 1080p to clear
    /// and copy its buffer no matter what is on screen, and in exchange draws primitives far more
    /// cheaply than WPF: on the benchmark's densest frame that is 107 ms down to 45 ms, while on a
    /// near-empty view it is 0.2 ms up to 2.2 ms. So the choice is made per frame.
    /// </para>
    ///
    /// <para>
    /// The two thresholds are deliberately different quantities and deliberately far apart. Using
    /// one number in both directions would flap between backends on frames sitting near it, and
    /// because the backends differ in layer ordering, flapping is visible — annotation would appear
    /// to jump above and below geometry from frame to frame.
    /// </para>
    /// </summary>
    private bool ShouldUseRasterBackend()
    {
        var setting = ApplicationSettings.Instance.RenderBackend;

        if (string.Equals(setting, "GPU", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(setting, "Managed", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(setting, "Legacy", StringComparison.OrdinalIgnoreCase)) return false;

        // Auto must not silently throw away Display Line Weight. Neither raster backend reads
        // PenSpec.LineWeight — both draw one-pixel hairlines — so switching away from the vector
        // path turns the setting off without telling anyone, and Auto switches on frame time and
        // shape count, which is exactly the large drawings where a user notices stroke weights. An
        // explicit Managed or GPU choice still wins above: naming a backend is a decision, and it is
        // the caller's to make.
        if (ApplicationSettings.Instance.DisplayLineWeight)
        {
            _rasterActive = false;
            return false;
        }

        if (_rasterActive)
        {
            if (_sceneIndex.VisibleCount < RasterSwitchDownShapes) _rasterActive = false;
        }
        else
        {
            // The previous frame's cost, which is the only honest evidence of what this one will
            // cost — shape count alone does not distinguish a hundred lines from a hundred hatches.
            if (_lastVectorFrameMs > RasterSwitchUpMs) _rasterActive = true;
        }

        return _rasterActive;
    }

    private double _lastVectorFrameMs;

    /// <summary>
    /// Runs the managed rasterizer over the visible set and paints its bitmap into the lower layer.
    /// Returns the shapes it declined, in draw order, for the vector path to finish.
    /// </summary>
    private IReadOnlyList<Shape> RenderThroughRasterBackend(double scale)
    {
        _rasterVisibleBuffer.Clear();

        foreach (var slot in _sceneIndex.Visible)
        {
            if (_sceneIndex.ShapeAt(slot) is not Shape shape || !shape.IsVisible) continue;
            if (Rendering.LodPolicy.Classify(_sceneIndex.MaxExtentAt(slot), scale)
                == Rendering.LodLevel.Skip) continue;

            _rasterVisibleBuffer.Add(shape);
        }

        var width = (int)Math.Round(ActualWidth);
        var height = (int)Math.Round(ActualHeight);

        if (string.Equals(ApplicationSettings.Instance.RenderBackend, "GPU",
                          StringComparison.OrdinalIgnoreCase))
        {
            var gpuDeferred = RenderThroughGpuBackend(width, height);
            if (gpuDeferred != null) return gpuDeferred;
            // Device unavailable or lost: fall through to the CPU rasterizer for this and every
            // later frame, rather than failing repeatedly.
        }

        // Transparent, not the background colour. The bottom layer paints the background and the
        // grid; an opaque clear here would cover both, because this bitmap spans the whole canvas.
        // The surface is Pbgra32, so 0 is fully transparent premultiplied and WPF composites the
        // finished bitmap over the grid in one step — the rasterizer itself still writes only opaque
        // pixels and does no per-pixel blending, so note 82's premise is untouched.
        const int background = 0;

        // The closure captures the viewport rather than a snapshot of its numbers, so every band
        // projects with the same transform the vector layer above it uses.
        var ok = _rasterBackend.Render(width, height, background, _rasterVisibleBuffer, scale,
            (wx, wy) =>
            {
                var p = _viewport.WorldToScreen(wx, wy);
                return (p.X, p.Y);
            });

        if (!ok)
        {
            ClearRasterLayer();
            return _rasterVisibleBuffer;   // nothing rasterised; let the vector path draw it all
        }

        _frameMetrics.AddSegments(_rasterBackend.SegmentsSubmitted);

        using (var rdc = _rasterVisual.RenderOpen())
        {
            if (_rasterBackend.Output != null)
                rdc.DrawImage(_rasterBackend.Output, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        return _rasterBackend.Deferred;
    }

    /// <summary>
    /// Draws through Direct3D, or returns null if no device is usable.
    ///
    /// <para>
    /// The geometry is uploaded only when <see cref="_sceneVersion"/> moves, so panning and zooming
    /// cost one constant-buffer write no matter how large the drawing is — which is the entire
    /// reason this backend exists, and the only way past the full-frame bitmap copy that caps the
    /// CPU paths at 4K.
    /// </para>
    /// </summary>
    private IReadOnlyList<Shape>? RenderThroughGpuBackend(int width, int height)
    {
        _gpuBackend ??= new Rendering.Raster.D3D11RasterBackend();

        if (!_gpuBackend.Initialise())
        {
            DoodleSharp.Diagnostics.Journal.Warn("CANVAS.GPU.UNAVAILABLE",
                "Direct3D backend unavailable; using the CPU rasterizer",
                _gpuBackend.UnavailableReason ?? "<no reason>");
            return null;
        }

        if (_gpuUploadedVersion != _sceneVersion)
        {
            _rasterVisibleBuffer.Clear();
            foreach (var drawable in _currentShapes)
                if (drawable is Shape shape && shape.IsVisible) _rasterVisibleBuffer.Add(shape);

            _gpuBackend.UploadScene(_rasterVisibleBuffer, _gpuTessellator);
            _gpuUploadedVersion = _sceneVersion;
        }

        var bg = _backgroundBrush is SolidColorBrush b
            ? new Vortice.Mathematics.Color4(b.Color.R / 255f, b.Color.G / 255f, b.Color.B / 255f, 1f)
            : new Vortice.Mathematics.Color4(0.118f, 0.118f, 0.118f, 1f);

        if (!_gpuBackend.Render(width, height, bg, _viewport.Scale, _viewport.PanX, _viewport.PanY))
            return null;

        _frameMetrics.AddSegments(_gpuBackend.SegmentCount);

        using (var rdc = _rasterVisual.RenderOpen())
        {
            if (_gpuBackend.Output != null)
                rdc.DrawImage(_gpuBackend.Output, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        // Text and anything else the GPU sink declines is drawn by the vector layer above -- but
        // only what is actually on screen. Walking the whole document here meant every label in the
        // drawing was handed to the text renderer regardless of the viewport, which is both wasteful
        // and how the font-size overflow above was reached: a label far off screen at extreme zoom
        // still had its size computed.
        _gpuDeferred.Clear();

        var view = _viewport.GetVisibleWorldBounds();
        var pad = 20.0 / Math.Max(_viewport.Scale, ViewportTransform.MinZoom);
        _sceneIndex.Query(view.Left - pad, view.Top - pad, view.Right + pad, view.Bottom + pad);

        // Text, plus anything the tessellator or the GPU sink declined at upload time. The latter
        // used to be dropped on the floor: UploadScene ignored Tessellate's return value, so a
        // dimension or an arrow simply did not exist on this backend.
        var declined = _gpuBackend.DeclinedShapes;

        foreach (var slot in _sceneIndex.Visible)
        {
            if (_sceneIndex.ShapeAt(slot) is not Shape s || !s.IsVisible) continue;

            if (s is VText || declined.Contains(s)) _gpuDeferred.Add(s);
        }

        return _gpuDeferred;
    }

    private readonly List<Shape> _gpuDeferred = new();

    /// <summary>Empties the bitmap layer, so switching back to the vector backend leaves nothing behind.</summary>
    private void ClearRasterLayer()
    {
        using var rdc = _rasterVisual.RenderOpen();
    }

    private static readonly Brush _hudBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)));
    private static readonly Brush _hudForeground = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)));
    private static readonly Typeface _hudTypeface = new(new FontFamily("Consolas"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <summary>
    /// Draws the frame-timing readout, top-left.
    ///
    /// <para>
    /// It lives on the overlay layer, so switching it on does not itself force the scene to be
    /// rebuilt every frame — which would be a measurement instrument that changes what it measures.
    /// The cull ratio is the number worth watching: shapes examined per shape drawn. At 1.0 culling
    /// is perfect; when it tracks the document size, culling is doing nothing.
    /// </para>
    /// </summary>
    private Rendering.FrameSummary? _hudSummary;
    private int _hudSummaryAge;

    private void DrawPerformanceHud(DrawingContext dc)
    {
        // Summarize() sorts a 600-entry history and allocates; doing that every frame made the HUD
        // itself the largest per-frame allocation, so the instrument was dominating the number it
        // reported. Refreshing a few times a second is more readable anyway — a figure updating at
        // 60 Hz cannot be read.
        if (_hudSummary == null || ++_hudSummaryAge >= 15)
        {
            _hudSummary = _frameMetrics.Summarize();
            _hudSummaryAge = 0;
        }

        var s = _hudSummary;
        if (s.Frames == 0) return;

        var backend = _rasterActive ? "raster" : "vector";
        var lines = new[]
        {
            $"p50 {s.P50Ms,6:F2} ms   p95 {s.P95Ms,6:F2} ms  ({s.P95Fps,5:F0} fps)",
            $"cull {s.CullMs,5:F2}  raster {s.RasterMs,5:F2}   backend {backend}",
            $"visible {s.MeanVisibleShapes,7:N0} / examined {s.MeanConsideredShapes,-7:N0}",
            $"alloc {s.MeanAllocatedBytes / 1024.0,7:F1} KB/frame   gen0 {s.Gen0Collections}",
        };

        var dpi = _cachedPixelsPerDip ??= VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var texts = lines.Select(l => new FormattedText(l, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _hudTypeface, 12, _hudForeground, dpi)).ToList();

        var w = texts.Max(t => t.Width) + 16;
        var h = texts.Sum(t => t.Height) + 12;

        dc.DrawRectangle(_hudBackground, null, new Rect(8, 8, w, h));

        var y = 14.0;
        foreach (var t in texts)
        {
            dc.DrawText(t, new Point(16, y));
            y += t.Height;
        }
    }

    private readonly Rendering.StrokeBatcher _strokeBatcher = new();

    /// <summary>
    /// Adds a stroke-only shape's segments to the pen batch. Returns false if the shape turns out
    /// not to be expressible as plain segments after all, in which case the caller draws it normally.
    /// </summary>
    private bool TryBatchStrokes(Shape shape)
    {
        var pen = GetShapePen(shape.Color, shape.LineWeight, shape.LineType, shape.LineTypeScale);
        var ox = shape.OffsetX;
        var oy = shape.OffsetY;

        switch (shape)
        {
            case VLine line:
                _strokeBatcher.Add(pen,
                    WorldToScreen(line.Start.X + ox, line.Start.Y + oy),
                    WorldToScreen(line.End.X + ox, line.End.Y + oy));
                return true;

            case VPolygon polygon:
            {
                var pts = polygon.Points;
                if (pts == null || pts.Count < 2) return false;
                for (int i = 0; i < pts.Count; i++)
                {
                    // Closed: the last vertex joins back to the first.
                    var next = pts[(i + 1) % pts.Count];
                    _strokeBatcher.Add(pen,
                        WorldToScreen(pts[i].X + ox, pts[i].Y + oy),
                        WorldToScreen(next.X + ox, next.Y + oy));
                }
                return true;
            }

            case VPolyline polyline:
            {
                var pts = polyline.Points;
                if (pts == null || pts.Count < 2) return false;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    _strokeBatcher.Add(pen,
                        WorldToScreen(pts[i].X + ox, pts[i].Y + oy),
                        WorldToScreen(pts[i + 1].X + ox, pts[i + 1].Y + oy));
                }
                return true;
            }
        }

        return false;
    }

    // ── Batched level-of-detail dots ─────────────────────────────────────────────────────────
    //
    // Reused across frames; cleared, never reallocated, because at the widest zoom this holds one
    // entry per visible shape and reallocating it per frame would reintroduce exactly the garbage
    // the rest of this work removed.
    private readonly Dictionary<string, List<Point>> _dotBatches = new();

    private void AddDot(string color, Point screenPoint)
    {
        if (!_dotBatches.TryGetValue(color, out var list))
        {
            list = new List<Point>(256);
            _dotBatches[color] = list;
        }
        list.Add(screenPoint);
    }

    /// <summary>
    /// Emits the accumulated dots, one geometry per colour.
    ///
    /// <para>
    /// Each dot is a one-pixel figure inside a shared <see cref="StreamGeometry"/>, so a hundred
    /// thousand of them cost one <c>DrawGeometry</c> per distinct colour instead of a hundred
    /// thousand <c>DrawRectangle</c> calls. Batching by colour is the same principle the eventual
    /// raster backend needs at a larger scale — one submission per pen, not per shape.
    /// </para>
    /// </summary>
    private void FlushDots(DrawingContext dc)
    {
        foreach (var (color, points) in _dotBatches)
        {
            if (points.Count == 0) continue;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var p in points)
                {
                    ctx.BeginFigure(p, true, true);
                    ctx.LineTo(new Point(p.X + 1, p.Y), false, false);
                    ctx.LineTo(new Point(p.X + 1, p.Y + 1), false, false);
                    ctx.LineTo(new Point(p.X, p.Y + 1), false, false);
                }
            }
            geometry.Freeze();

            dc.DrawGeometry(GetCachedBrush(color), null, geometry);
            points.Clear();
        }
    }

    /// <summary>
    /// Above this many pattern segments per square screen pixel, the hatch reads as solid and is
    /// drawn as a filled boundary instead. Set below 1 rather than at it because strokes have width
    /// and overlap: a pattern at half a line per pixel already covers most of the area.
    /// </summary>
    private const double MaxHatchLinesPerPixel = 0.35;

    /// <summary>
    /// Don't bother with the solid substitution for sparse hatches — the check costs more than it
    /// saves, and a handful of visible strokes is exactly what the user asked for.
    /// </summary>
    private const int MinLinesForHatchFill = 64;

    private double EstimateScreenArea(Shape shape, double offsetX, double offsetY)
    {
        var b = shape.GetBounds();
        var w = b.Width * _viewport.Scale;
        var h = b.Height * _viewport.Scale;
        if (!double.IsFinite(w) || !double.IsFinite(h)) return 0;
        return Math.Max(w, 1) * Math.Max(h, 1);
    }

    /// <summary>
    /// Draws a too-dense hatch as its filled boundary — the same thing the pattern would produce
    /// once every pixel is covered, at one draw call instead of thousands.
    /// </summary>
    private void DrawHatchAsSolid(DrawingContext dc, VHatch hatch, double offsetX, double offsetY)
    {
        var boundary = hatch.Boundary;
        if (boundary == null || boundary.Count < 3) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = WorldToScreen(boundary[0].X + offsetX, boundary[0].Y + offsetY);
            ctx.BeginFigure(first, true, true);
            for (int i = 1; i < boundary.Count; i++)
            {
                var pt = WorldToScreen(boundary[i].X + offsetX, boundary[i].Y + offsetY);
                ctx.LineTo(pt, false, false);
            }
        }
        geometry.Freeze();

        dc.DrawGeometry(GetCachedBrush(hatch.Color), null, geometry);
    }

    public void ZoomExtents(IEnumerable<IDrawable> shapes)
    {
        var shapeList = shapes.ToList();
        if (!shapeList.Any() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _viewport.Scale = 1.0;
            _viewport.PanX = 0;
            _viewport.PanY = 0;
            RedrawAll();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var shape in shapeList)
        {
            // Skip hidden shapes
            if (shape is Shape shp && !shp.IsVisible)
                continue;

            switch (shape)
            {
                case VPoint point:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, point.X, point.Y);
                    break;
                case VLine line:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, line.Start.X, line.Start.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, line.End.X, line.End.Y);
                    break;
                case VArc arc:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius);
                    break;
                case VCircle circle:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius);
                    break;
                case VRectangle rect:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, rect.Corner.X, rect.Corner.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, rect.Corner.X + rect.Width, rect.Corner.Y + rect.Height);
                    break;
                case VEllipse ellipse:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY);
                    break;
                case VPolygon polygon:
                    foreach (var p in polygon.Points)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VPolyline polyline:
                    foreach (var p in polyline.Points)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VText text:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, text.Location.X, text.Location.Y);
                    break;
                case VBezier bezier:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P0.X, bezier.P0.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P1.X, bezier.P1.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P2.X, bezier.P2.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P3.X, bezier.P3.Y);
                    break;
                case VSpline spline:
                    foreach (var p in spline.ControlPoints)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VArrow arrow:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arrow.Start.X, arrow.Start.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arrow.End.X, arrow.End.Y);
                    break;
                case VRadialDimension radDim:
                    var radBounds = radDim.GetBounds();
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, radBounds.Min.X, radBounds.Min.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, radBounds.Max.X, radBounds.Max.Y);
                    break;
                case VDimension dim:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, dim.Point1.X, dim.Point1.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, dim.Point2.X, dim.Point2.Y);
                    break;
                case VGroup group:
                    var groupBounds = group.GetBounds();
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, groupBounds.Min.X, groupBounds.Min.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, groupBounds.Max.X, groupBounds.Max.Y);
                    break;
            }
        }

        var padding = 50.0;
        var worldWidth = maxX - minX;
        var worldHeight = maxY - minY;

        if (worldWidth < 1) worldWidth = 100;
        if (worldHeight < 1) worldHeight = 100;

        var worldCenterX = (minX + maxX) / 2;
        var worldCenterY = (minY + maxY) / 2;

        var availableWidth = ActualWidth - padding * 2;
        var availableHeight = ActualHeight - padding * 2;

        var scaleX = availableWidth / worldWidth;
        var scaleY = availableHeight / worldHeight;
        _viewport.Scale = Math.Min(scaleX, scaleY);
        _viewport.Scale = Math.Clamp(_viewport.Scale, ViewportTransform.MinZoom, ViewportTransform.MaxZoom);

        _viewport.PanX = -worldCenterX * _viewport.Scale;
        _viewport.PanY = worldCenterY * _viewport.Scale;

        RedrawAll();
    }

    private static void UpdateBounds(ref double minX, ref double maxX, ref double minY, ref double maxY, double x, double y)
    {
        minX = Math.Min(minX, x);
        maxX = Math.Max(maxX, x);
        minY = Math.Min(minY, y);
        maxY = Math.Max(maxY, y);
    }

    /// <summary>
    /// Finds a shape by its unique ID and zooms the canvas to fit it.
    /// </summary>
    /// <param name="id">The unique ID of the shape to zoom to.</param>
    /// <returns>True if the shape was found and zoomed to, false otherwise.</returns>
    public bool ZoomToShape(long id)
    {
        var shape = _currentShapes.OfType<Shape>().FirstOrDefault(s => s.Id == id);
        if (shape == null)
            return false;

        ZoomExtents(new[] { shape }, minWorldSize: 10);
        return true;
    }

    /// <summary>
    /// Zooms the canvas to fit the given shapes with a specified minimum world size.
    /// </summary>
    public void ZoomExtents(IEnumerable<IDrawable> shapes, double minWorldSize)
    {
        var shapeList = shapes.ToList();
        if (!shapeList.Any() || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _viewport.Scale = 1.0;
            _viewport.PanX = 0;
            _viewport.PanY = 0;
            RedrawAll();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var shape in shapeList)
        {
            // Skip hidden shapes
            if (shape is Shape shp && !shp.IsVisible)
                continue;

            switch (shape)
            {
                case VPoint point:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, point.X, point.Y);
                    break;
                case VLine line:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, line.Start.X, line.Start.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, line.End.X, line.End.Y);
                    break;
                case VArc arc:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius);
                    break;
                case VCircle circle:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius);
                    break;
                case VRectangle rect:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, rect.Corner.X, rect.Corner.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, rect.Corner.X + rect.Width, rect.Corner.Y + rect.Height);
                    break;
                case VEllipse ellipse:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY);
                    break;
                case VPolygon polygon:
                    foreach (var p in polygon.Points)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VPolyline polyline:
                    foreach (var p in polyline.Points)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VText text:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, text.Location.X, text.Location.Y);
                    break;
                case VBezier bezier:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P0.X, bezier.P0.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P1.X, bezier.P1.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P2.X, bezier.P2.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, bezier.P3.X, bezier.P3.Y);
                    break;
                case VSpline spline:
                    foreach (var p in spline.ControlPoints)
                        UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, p.X, p.Y);
                    break;
                case VArrow arrow:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arrow.Start.X, arrow.Start.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, arrow.End.X, arrow.End.Y);
                    break;
                case VRadialDimension radDim2:
                    var radBounds2 = radDim2.GetBounds();
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, radBounds2.Min.X, radBounds2.Min.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, radBounds2.Max.X, radBounds2.Max.Y);
                    break;
                case VDimension dim:
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, dim.Point1.X, dim.Point1.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, dim.Point2.X, dim.Point2.Y);
                    break;
                case VGroup group:
                    var groupBounds = group.GetBounds();
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, groupBounds.Min.X, groupBounds.Min.Y);
                    UpdateBounds(ref minX, ref maxX, ref minY, ref maxY, groupBounds.Max.X, groupBounds.Max.Y);
                    break;
            }
        }

        var padding = 50.0;
        var worldWidth = maxX - minX;
        var worldHeight = maxY - minY;

        // Ensure minimum world size for better visibility
        if (worldWidth < minWorldSize) worldWidth = minWorldSize;
        if (worldHeight < minWorldSize) worldHeight = minWorldSize;

        var worldCenterX = (minX + maxX) / 2;
        var worldCenterY = (minY + maxY) / 2;

        var availableWidth = ActualWidth - padding * 2;
        var availableHeight = ActualHeight - padding * 2;

        var scaleX = availableWidth / worldWidth;
        var scaleY = availableHeight / worldHeight;
        _viewport.Scale = Math.Min(scaleX, scaleY);
        _viewport.Scale = Math.Clamp(_viewport.Scale, ViewportTransform.MinZoom, ViewportTransform.MaxZoom);

        _viewport.PanX = -worldCenterX * _viewport.Scale;
        _viewport.PanY = worldCenterY * _viewport.Scale;

        RedrawAll();
    }
}
