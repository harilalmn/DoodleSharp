using System;

namespace DoodleSharp.Animation;

/// <summary>
/// Mouse callbacks on the canvas, in the shape JavaScript uses: assign a function per event.
///
/// <code>
/// Mouse.OnMove(e =&gt; cursor.Center = e.Position);
///
/// Mouse.OnDown(e =&gt;
/// {
///     new VCircle(e.Position, 10) { FillColor = "Cyan" };
///     VizConsole.Log($"clicked {e.Target?.Name ?? "empty space"}");
/// });
/// </code>
///
/// <para>
/// <b>Registering a handler puts the canvas into interactive mode.</b> While any handler is
/// registered the canvas stops competing for the mouse: click-to-select, wheel-zoom and
/// double-click-zoom-to-fit are all suppressed, so your handlers see every gesture. Zoom controls
/// appear over the top-right of the canvas in their place, and middle-button drag still pans. A
/// project that registers no handlers behaves exactly as it always has. The drawing tools (P/L/C/R)
/// and the measuring tape keep priority while they are armed — your handlers do not fire until you
/// leave the tool with Esc.
/// </para>
///
/// <para>
/// <b>Assigning replaces; it does not add.</b> Calling <see cref="OnMove"/> twice leaves one handler,
/// the second. That is deliberate and it is not the same as <see cref="Frame.Request"/>, which queues
/// each request separately: <c>Main()</c> is re-invoked on every tick of a Global Parameters slider
/// drag, so an additive API would silently stack hundreds of live handlers during one drag. Pass
/// <c>null</c> to detach one.
/// </para>
///
/// <para>
/// <b>Handlers are dropped at the start of every run.</b> Register them from <c>Main()</c> (or from
/// a sketch's <c>Setup()</c>) and let them be re-registered each time you press Run; do not expect a
/// handler to outlive the run that created it. It has to work this way — a handler is a delegate
/// pointing into the collectible assembly your code was compiled into, and one left behind would keep
/// that assembly alive and keep firing against shapes the next run has already replaced.
/// </para>
///
/// <para>
/// Handlers are always invoked on the UI thread, one at a time, so they can freely create and modify
/// shapes. The canvas repaints once per frame afterwards, not once per event.
/// </para>
/// </summary>
public static class Mouse
{
    /// <summary>How far the pointer may travel between down and up and still count as a click, in pixels.</summary>
    private const double ClickSlopPixels = 3.0;

    private static readonly object _lock = new();

    private static Action<MouseInfo>? _onMove;
    private static Action<MouseInfo>? _onDown;
    private static Action<MouseInfo>? _onUp;
    private static Action<MouseInfo>? _onClick;
    private static Action<MouseInfo>? _onDoubleClick;
    private static Action<MouseInfo>? _onDrag;
    private static Action<MouseInfo>? _onWheel;
    private static Action<MouseInfo>? _onEnter;
    private static Action<MouseInfo>? _onLeave;

    // Deliberately a plain field rather than a locked property like Frame.HasPending. This is read
    // once per mouse-move -- well over a hundred times a second -- on scenes that have no handlers at
    // all, and it is the gate that keeps those scenes from allocating a payload, reading the keyboard
    // modifiers or hit-testing. Writes happen in the setters, under the lock, and are rare.
    private static volatile bool _hasHandlers;

    // volatile for the same reason as _hasHandlers: written from Dispatch and read from the frame
    // loop. Both are the UI thread today, so this is about not depending on that remaining true.
    private static volatile bool _sceneDirty;

    // Click synthesis state. WPF gives a bare FrameworkElement no Click event, so an up is promoted
    // to a click when it matches the down that preceded it.
    private static MouseButtonKind _downButton = MouseButtonKind.None;
    private static double _downScreenX;
    private static double _downScreenY;

    /// <summary>Raised when a handler throws, so the host can report it. Handlers are dropped first.</summary>
    public static event Action<Exception>? CallbackFailed;

    /// <summary>
    /// Raised when the set of registered handlers becomes empty or non-empty, so the host can show or
    /// hide the interactive-mode canvas controls. Not raised for a handler swap.
    /// </summary>
    internal static event Action? HandlersChanged;

    /// <summary>
    /// True while at least one handler is registered — which is also what puts the canvas into
    /// interactive mode. The host reads this on every mouse event, so it is a plain field read.
    /// </summary>
    public static bool HasHandlers => _hasHandlers;

    /// <summary>Last known cursor X in world coordinates. Tracked even with no handlers registered.</summary>
    public static double X { get; private set; }

    /// <summary>Last known cursor Y in world coordinates. Tracked even with no handlers registered.</summary>
    public static double Y { get; private set; }

    /// <summary>
    /// True while any mouse button is held over the canvas. Tracked even with no handlers registered,
    /// so it is usable from a <see cref="Frame"/> callback or a sketch's <c>Draw()</c> without
    /// registering anything.
    /// </summary>
    public static bool IsDown { get; private set; }

    /// <summary>
    /// Called when the pointer moves with no button held.
    /// <para>Pass null to detach. Replaces any previously registered move handler.</para>
    /// </summary>
    public static void OnMove(Action<MouseInfo>? handler) => Assign(ref _onMove, handler);

    /// <summary>
    /// Called when a mouse button goes down. <c>e.Button</c> says which.
    /// <para>Pass null to detach. Replaces any previously registered down handler.</para>
    /// </summary>
    public static void OnDown(Action<MouseInfo>? handler) => Assign(ref _onDown, handler);

    /// <summary>
    /// Called when a mouse button is released.
    /// <para>Pass null to detach. Replaces any previously registered up handler.</para>
    /// </summary>
    public static void OnUp(Action<MouseInfo>? handler) => Assign(ref _onUp, handler);

    /// <summary>
    /// Called after <see cref="OnUp"/> when the button went down and came back up within a few pixels
    /// — the usual "the user clicked this" event. A drag does not produce a click.
    /// <para>Pass null to detach. Replaces any previously registered click handler.</para>
    /// </summary>
    public static void OnClick(Action<MouseInfo>? handler) => Assign(ref _onClick, handler);

    /// <summary>
    /// Called on the second click of a double click, in place of <see cref="OnDown"/>.
    /// <para>Pass null to detach. Replaces any previously registered double-click handler.</para>
    /// </summary>
    public static void OnDoubleClick(Action<MouseInfo>? handler) => Assign(ref _onDoubleClick, handler);

    /// <summary>
    /// Called when the pointer moves with a button held, in place of <see cref="OnMove"/>. The canvas
    /// captures the mouse for the duration, so a drag keeps reporting even if the pointer leaves the
    /// canvas, and always finishes with an <see cref="OnUp"/>.
    /// <para>Pass null to detach. Replaces any previously registered drag handler.</para>
    /// </summary>
    public static void OnDrag(Action<MouseInfo>? handler) => Assign(ref _onDrag, handler);

    /// <summary>
    /// Called when the wheel turns. Read <c>e.WheelNotches</c> for the amount. In interactive mode the
    /// canvas does not zoom on the wheel, so it is yours to use.
    /// <para>Pass null to detach. Replaces any previously registered wheel handler.</para>
    /// </summary>
    public static void OnWheel(Action<MouseInfo>? handler) => Assign(ref _onWheel, handler);

    /// <summary>
    /// Called when the pointer enters the canvas.
    /// <para>Pass null to detach. Replaces any previously registered enter handler.</para>
    /// </summary>
    public static void OnEnter(Action<MouseInfo>? handler) => Assign(ref _onEnter, handler);

    /// <summary>
    /// Called when the pointer leaves the canvas. A drag in progress gets its <see cref="OnUp"/>
    /// first, so a handler tracking "am I dragging?" is never left stuck on.
    /// <para>Pass null to detach. Replaces any previously registered leave handler.</para>
    /// </summary>
    public static void OnLeave(Action<MouseInfo>? handler) => Assign(ref _onLeave, handler);

    /// <summary>
    /// Detaches every handler, which also takes the canvas out of interactive mode.
    ///
    /// <para>
    /// <b>The host must call this before each run, and this is not optional.</b> User code is compiled
    /// into a collectible <c>AssemblyLoadContext</c>; a handler left registered points into that
    /// assembly and pins it, so the context never unloads and the previous run's handlers keep firing
    /// against shapes the new run has replaced.
    /// </para>
    /// </summary>
    public static void Clear()
    {
        bool had;

        lock (_lock)
        {
            had = _hasHandlers;

            _onMove = null;
            _onDown = null;
            _onUp = null;
            _onClick = null;
            _onDoubleClick = null;
            _onDrag = null;
            _onWheel = null;
            _onEnter = null;
            _onLeave = null;
            _hasHandlers = false;

            // Gesture state belongs to the handlers that are going away, so it is reset -- including
            // IsDown, since a stuck "a button is held" would read to the next run's code as a drag
            // already in progress. X and Y are the exception and deliberately survive: they describe
            // where the pointer physically is, which is the host's fact and stays true across runs.
            _sceneDirty = false;
            _downButton = MouseButtonKind.None;
            IsDown = false;
        }

        if (had) HandlersChanged?.Invoke();
    }

    private static void Assign(ref Action<MouseInfo>? slot, Action<MouseInfo>? handler)
    {
        bool changed;

        lock (_lock)
        {
            var had = _hasHandlers;
            slot = handler;
            _hasHandlers = _onMove != null || _onDown != null || _onUp != null || _onClick != null
                || _onDoubleClick != null || _onDrag != null || _onWheel != null
                || _onEnter != null || _onLeave != null;
            changed = had != _hasHandlers;
        }

        // Raised outside the lock: the host handler marshals to the UI thread and touches the canvas.
        if (changed) HandlersChanged?.Invoke();
    }

    // ── Host-facing dispatch ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the pointer position and button state. Called on every mouse event regardless of
    /// whether any handler is registered, so <see cref="X"/>/<see cref="Y"/>/<see cref="IsDown"/> —
    /// and the polled properties on <c>Sketch</c> that read them — are always current.
    /// </summary>
    internal static void Track(double worldX, double worldY, bool isDown)
    {
        X = worldX;
        Y = worldY;
        IsDown = isDown;
    }

    /// <summary>Dispatches a move, or a drag when a button is held. Returns true if a handler ran.</summary>
    internal static bool RaiseMove(MouseInfo e)
    {
        var dragging = e.LeftDown || e.RightDown || e.MiddleDown;
        return Dispatch(dragging ? _onDrag : _onMove, e);
    }

    /// <summary>
    /// Dispatches a button press — as a double click when <c>e.ClickCount</c> is 2. Remembers the
    /// press so <see cref="RaiseUp"/> can decide whether it became a click.
    /// </summary>
    internal static bool RaiseDown(MouseInfo e)
    {
        lock (_lock)
        {
            _downButton = e.Button;
            _downScreenX = e.ScreenX;
            _downScreenY = e.ScreenY;
        }

        return Dispatch(e.ClickCount >= 2 ? _onDoubleClick : _onDown, e);
    }

    /// <summary>
    /// Dispatches a button release, then a synthesised click if the release matches the press that
    /// preceded it — same button, and the pointer never travelled far enough to make it a drag.
    /// </summary>
    internal static bool RaiseUp(MouseInfo e)
    {
        var ran = Dispatch(_onUp, e);

        bool isClick;
        lock (_lock)
        {
            isClick = _downButton != MouseButtonKind.None
                && _downButton == e.Button
                && Math.Abs(e.ScreenX - _downScreenX) <= ClickSlopPixels
                && Math.Abs(e.ScreenY - _downScreenY) <= ClickSlopPixels;
            _downButton = MouseButtonKind.None;
        }

        if (isClick && _onClick != null)
        {
            var click = new MouseInfo(
                MouseEventKind.Click, e.Position, e.RawPosition, e.ScreenX, e.ScreenY,
                e.Button, e.LeftDown, e.RightDown, e.MiddleDown,
                e.Shift, e.Ctrl, e.Alt,
                clickCount: Math.Max(1, e.ClickCount), wheelDelta: 0, scale: e.Scale,
                hitTest: p => e.Target);

            ran |= Dispatch(_onClick, click);
        }

        return ran;
    }

    /// <summary>Dispatches a wheel turn. Returns true if a handler ran.</summary>
    internal static bool RaiseWheel(MouseInfo e) => Dispatch(_onWheel, e);

    /// <summary>Dispatches a pointer-entered-the-canvas event. Returns true if a handler ran.</summary>
    internal static bool RaiseEnter(MouseInfo e) => Dispatch(_onEnter, e);

    /// <summary>Dispatches a pointer-left-the-canvas event. Returns true if a handler ran.</summary>
    internal static bool RaiseLeave(MouseInfo e) => Dispatch(_onLeave, e);

    /// <summary>
    /// Returns true once if a handler has run since the last call, so the host repaints a frame in
    /// which user code may have changed the scene, and skips the work when it did not.
    /// </summary>
    internal static bool ConsumeSceneDirty()
    {
        if (!_sceneDirty) return false;
        _sceneDirty = false;
        return true;
    }

    private static bool Dispatch(Action<MouseInfo>? handler, MouseInfo e)
    {
        if (handler == null) return false;

        try
        {
            handler(e);
        }
        catch (Exception ex)
        {
            // One bad handler detaches all of them rather than throwing on every mouse move. User
            // code runs in-process, so an unhandled exception here reaches WPF's dispatcher and takes
            // the application down -- and a move handler can throw a hundred times a second.
            Clear();

            // Set AFTER Clear(), which resets it: the handler may well have moved something before it
            // threw, and that half-finished work should still reach the screen.
            _sceneDirty = true;

            CallbackFailed?.Invoke(ex);
            return true;
        }

        // We cannot tell whether the handler touched the scene, and the two mistakes are not equal:
        // one redundant repaint per frame is invisible, whereas missing a real change looks like the
        // API is broken. So any handler that ran marks the frame dirty.
        _sceneDirty = true;
        return true;
    }
}
