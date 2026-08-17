using System;
using C2VGeometry;

namespace DoodleSharp.Animation;

/// <summary>Which mouse button an event is about.</summary>
public enum MouseButtonKind
{
    /// <summary>No button — a plain move, a wheel turn, or an enter/leave.</summary>
    None,
    /// <summary>The left button.</summary>
    Left,
    /// <summary>The right button.</summary>
    Right,
    /// <summary>The middle button (the wheel pressed as a button).</summary>
    Middle,
    /// <summary>The first extra button, if the mouse has one.</summary>
    XButton1,
    /// <summary>The second extra button, if the mouse has one.</summary>
    XButton2
}

/// <summary>What kind of mouse event a <see cref="MouseInfo"/> describes.</summary>
public enum MouseEventKind
{
    /// <summary>The pointer moved with no button held.</summary>
    Move,
    /// <summary>A button went down.</summary>
    Down,
    /// <summary>A button was released.</summary>
    Up,
    /// <summary>A button went down and came back up in the same place — synthesised, see <see cref="Mouse.OnClick"/>.</summary>
    Click,
    /// <summary>A second click arrived inside the system double-click time.</summary>
    DoubleClick,
    /// <summary>The pointer moved with a button held.</summary>
    Drag,
    /// <summary>The wheel turned.</summary>
    Wheel,
    /// <summary>The pointer entered the canvas.</summary>
    Enter,
    /// <summary>The pointer left the canvas.</summary>
    Leave
}

/// <summary>
/// Everything known about one mouse event, handed to the callbacks registered on <see cref="Mouse"/>.
/// The equivalent of the <c>event</c> object a JavaScript <c>onmousemove(e)</c> handler receives.
///
/// <code>
/// Mouse.OnDown(e =&gt;
/// {
///     new VCircle(e.Position, 10) { FillColor = e.Shift ? "Red" : "Cyan" };
///     VizConsole.Log($"{e.Button} at {e.X:F1}, {e.Y:F1} over {e.Target?.Name ?? "empty space"}");
/// });
/// </code>
///
/// <para>
/// A fresh instance is created for every dispatched event, so it is safe to keep one — stash it in a
/// field, put it in a list, compare it with the next one. It is deliberately <b>not</b> a pooled or
/// reused object: a single recycled instance would appear to mutate underneath any handler that held
/// on to it, which is a class of bug that is very hard to see.
/// </para>
/// </summary>
/// <remarks>
/// This type intentionally exposes no WPF types — coordinates are <see cref="VXYZ"/> and plain
/// <see cref="double"/>s, buttons and modifiers are its own enums and <see cref="bool"/>s. That keeps
/// it usable (and testable) away from a window, and keeps the geometry-facing API free of
/// <c>System.Windows</c>.
/// </remarks>
public sealed class MouseInfo
{
    private readonly Func<VXYZ, Shape?>? _hitTest;
    private Shape? _target;
    private bool _hitTested;

    /// <summary>Creates a mouse event payload. Called by the host; user code only ever reads one.</summary>
    /// <param name="kind">Which kind of event this is.</param>
    /// <param name="position">Cursor position in world coordinates, grid-snapped if snapping is on.</param>
    /// <param name="rawPosition">Cursor position in world coordinates, never snapped.</param>
    /// <param name="screenX">Cursor X in device-independent pixels from the canvas's left edge.</param>
    /// <param name="screenY">Cursor Y in device-independent pixels from the canvas's top edge.</param>
    /// <param name="button">The button this event is about, or <see cref="MouseButtonKind.None"/>.</param>
    /// <param name="leftDown">Whether the left button is held.</param>
    /// <param name="rightDown">Whether the right button is held.</param>
    /// <param name="middleDown">Whether the middle button is held.</param>
    /// <param name="shift">Whether Shift is held.</param>
    /// <param name="ctrl">Whether Ctrl is held.</param>
    /// <param name="alt">Whether Alt is held.</param>
    /// <param name="clickCount">1 for a single click, 2 for a double click, 0 when not a button event.</param>
    /// <param name="wheelDelta">Wheel movement, 120 units per notch; 0 when the wheel did not turn.</param>
    /// <param name="scale">The canvas zoom factor when the event happened.</param>
    /// <param name="hitTest">
    /// Supplies <see cref="Target"/> on demand. Called at most once, and only if a handler actually
    /// reads <see cref="Target"/>.
    /// </param>
    public MouseInfo(
        MouseEventKind kind,
        VXYZ position,
        VXYZ rawPosition,
        double screenX,
        double screenY,
        MouseButtonKind button = MouseButtonKind.None,
        bool leftDown = false,
        bool rightDown = false,
        bool middleDown = false,
        bool shift = false,
        bool ctrl = false,
        bool alt = false,
        int clickCount = 0,
        int wheelDelta = 0,
        double scale = 1.0,
        Func<VXYZ, Shape?>? hitTest = null)
    {
        Kind = kind;
        Position = position;
        RawPosition = rawPosition;
        ScreenX = screenX;
        ScreenY = screenY;
        Button = button;
        LeftDown = leftDown;
        RightDown = rightDown;
        MiddleDown = middleDown;
        Shift = shift;
        Ctrl = ctrl;
        Alt = alt;
        ClickCount = clickCount;
        WheelDelta = wheelDelta;
        Scale = scale;
        _hitTest = hitTest;
    }

    /// <summary>Which kind of event this is. Useful when one method is registered for several events.</summary>
    public MouseEventKind Kind { get; }

    /// <summary>
    /// Cursor position in world coordinates — the same value the rest of the app uses, so it is
    /// grid-snapped while Snap to Grid (F9) is on and matches the coordinate readout in the status
    /// bar. Use <see cref="RawPosition"/> if you need the true cursor position regardless.
    /// </summary>
    public VXYZ Position { get; }

    /// <summary>
    /// Cursor position in world coordinates, never grid-snapped. Equals <see cref="Position"/> unless
    /// Snap to Grid is on.
    /// </summary>
    public VXYZ RawPosition { get; }

    /// <summary>Shorthand for <c>Position.X</c>.</summary>
    public double X => Position.X;

    /// <summary>Shorthand for <c>Position.Y</c>.</summary>
    public double Y => Position.Y;

    /// <summary>
    /// Cursor X in device-independent pixels from the canvas's left edge. Rarely needed — world
    /// coordinates are what geometry is built in — but useful for size-in-pixels decisions.
    /// </summary>
    public double ScreenX { get; }

    /// <summary>Cursor Y in device-independent pixels from the canvas's top edge, increasing downwards.</summary>
    public double ScreenY { get; }

    /// <summary>
    /// The button this event is about: the one pressed or released for a down/up/click,
    /// <see cref="MouseButtonKind.None"/> for a move, wheel turn, enter or leave. To ask what is
    /// currently held during a move, use <see cref="LeftDown"/> and friends.
    /// </summary>
    public MouseButtonKind Button { get; }

    /// <summary>Whether the left button is held down.</summary>
    public bool LeftDown { get; }

    /// <summary>Whether the right button is held down.</summary>
    public bool RightDown { get; }

    /// <summary>Whether the middle button is held down.</summary>
    public bool MiddleDown { get; }

    /// <summary>Whether Shift is held.</summary>
    public bool Shift { get; }

    /// <summary>Whether Ctrl is held.</summary>
    public bool Ctrl { get; }

    /// <summary>Whether Alt is held.</summary>
    public bool Alt { get; }

    /// <summary>
    /// 1 for a single click, 2 for a double click, 0 when the event is not about a button.
    /// </summary>
    public int ClickCount { get; }

    /// <summary>
    /// How far the wheel turned, in WPF's units of 120 per notch — positive away from the user.
    /// 0 unless <see cref="Kind"/> is <see cref="MouseEventKind.Wheel"/>. See
    /// <see cref="WheelNotches"/> for the friendlier form.
    /// </summary>
    public int WheelDelta { get; }

    /// <summary>
    /// <see cref="WheelDelta"/> expressed in notches: 1.0 per detent, positive away from the user.
    /// </summary>
    public double WheelNotches => WheelDelta / 120.0;

    /// <summary>
    /// The canvas zoom factor when the event happened — screen pixels per world unit. Use it to size
    /// hit tolerances in world units, e.g. <c>8 / e.Scale</c> for "within 8 pixels".
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// The topmost shape under the cursor, or null over empty space.
    ///
    /// <para>
    /// Computed on demand and cached, so reading it costs nothing until you do and never costs twice.
    /// That matters because a move handler can run over a hundred times a second and most handlers
    /// never ask.
    /// </para>
    ///
    /// <para>
    /// Two things to know. It uses the same few-pixels tolerance the selection tool uses, so it
    /// answers "what would clicking here have picked?" rather than "is the cursor strictly inside
    /// this shape?" — use <see cref="Shape.Contains"/> for the strict question. And while a timeline
    /// or a <see cref="Frame"/> loop is animating, the spatial index it consults holds the positions
    /// from the start of the frame, so it can lag a fast-moving shape.
    /// </para>
    /// </summary>
    public Shape? Target
    {
        get
        {
            if (!_hitTested)
            {
                // RawPosition, not Position: hit-testing a grid-snapped point would report whatever
                // sits at the snap intersection rather than what is under the cursor.
                _target = _hitTest?.Invoke(RawPosition);
                _hitTested = true;
            }

            return _target;
        }
    }
}
