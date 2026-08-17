using System;
using System.Collections.Generic;
using Xunit;
using DoodleSharp.Animation;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// The mouse callback registry.
///
/// <para>
/// The property everything else rests on is that registration <i>replaces</i> rather than accumulates.
/// <c>Main()</c> is re-invoked on every tick of a Global Parameters slider drag, so an additive API
/// would stack hundreds of live handlers during a single drag — each running on every mouse move. This
/// is the one behaviour that cannot be recovered by host discipline, so it is asserted first.
/// </para>
///
/// <para>
/// None of this touches WPF: <c>Mouse</c> and <c>MouseInfo</c> are deliberately free of
/// <c>System.Windows</c> types, which is what lets the whole registry be driven from a plain test
/// worker with no window, no STA thread and no synthetic input.
/// </para>
/// </summary>
public class MouseCallbackTests : IDisposable
{
    public MouseCallbackTests() => Mouse.Clear();
    public void Dispose() => Mouse.Clear();

    private static MouseInfo Move(double x = 0, double y = 0, double screenX = 0, double screenY = 0)
        => new(MouseEventKind.Move, new VXYZ(x, y), new VXYZ(x, y), screenX, screenY);

    private static MouseInfo Down(
        MouseButtonKind button = MouseButtonKind.Left,
        double screenX = 0, double screenY = 0, int clickCount = 1)
        => new(MouseEventKind.Down, new VXYZ(0, 0), new VXYZ(0, 0), screenX, screenY,
               button, leftDown: button == MouseButtonKind.Left, clickCount: clickCount);

    private static MouseInfo Up(
        MouseButtonKind button = MouseButtonKind.Left, double screenX = 0, double screenY = 0)
        => new(MouseEventKind.Up, new VXYZ(0, 0), new VXYZ(0, 0), screenX, screenY, button);

    // ── Registration semantics ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AssigningAHandlerRepeatedlyLeavesExactlyOne()
    {
        var runs = 0;

        // Stands in for a Global Parameters slider drag re-invoking Main() on every tick.
        for (var i = 0; i < 100; i++) Mouse.OnMove(_ => runs++);

        Mouse.RaiseMove(Move());

        Assert.Equal(1, runs);
    }

    [Fact]
    public void AssigningReplacesTheHandlerRatherThanChaining()
    {
        var log = new List<string>();
        Mouse.OnDown(_ => log.Add("first"));
        Mouse.OnDown(_ => log.Add("second"));

        Mouse.RaiseDown(Down());

        Assert.Equal(new[] { "second" }, log);
    }

    [Fact]
    public void PassingNullDetachesAHandler()
    {
        var runs = 0;
        Mouse.OnMove(_ => runs++);
        Mouse.OnMove(null);

        Mouse.RaiseMove(Move());

        Assert.Equal(0, runs);
        Assert.False(Mouse.HasHandlers);
    }

    [Fact]
    public void HasHandlersTracksTheSlots()
    {
        Assert.False(Mouse.HasHandlers);

        Mouse.OnMove(_ => { });
        Assert.True(Mouse.HasHandlers);

        Mouse.OnDown(_ => { });
        Assert.True(Mouse.HasHandlers);

        // Still one slot filled, so still interactive.
        Mouse.OnMove(null);
        Assert.True(Mouse.HasHandlers);

        Mouse.OnDown(null);
        Assert.False(Mouse.HasHandlers);
    }

    [Fact]
    public void ClearDetachesEveryHandler()
    {
        var runs = 0;
        Mouse.OnMove(_ => runs++);
        Mouse.OnDown(_ => runs++);
        Mouse.OnUp(_ => runs++);
        Mouse.OnWheel(_ => runs++);
        Mouse.OnEnter(_ => runs++);
        Mouse.OnLeave(_ => runs++);

        // The host calls this before every run: a handler left registered points into the previous
        // run's collectible assembly and pins it so the load context never unloads.
        Mouse.Clear();

        Mouse.RaiseMove(Move());
        Mouse.RaiseDown(Down());
        Mouse.RaiseUp(Up());
        Mouse.RaiseWheel(Move());
        Mouse.RaiseEnter(Move());
        Mouse.RaiseLeave(Move());

        Assert.Equal(0, runs);
        Assert.False(Mouse.HasHandlers);
    }

    [Fact]
    public void HandlersChangedFiresOnlyWhenInteractiveModeFlips()
    {
        var flips = 0;
        void Handler() => flips++;
        Mouse.HandlersChanged += Handler;

        try
        {
            Mouse.OnMove(_ => { });      // none -> some: flip
            Assert.Equal(1, flips);

            Mouse.OnMove(_ => { });      // swap: no flip
            Mouse.OnDown(_ => { });      // still some: no flip
            Assert.Equal(1, flips);

            Mouse.OnMove(null);          // still some (down remains): no flip
            Assert.Equal(1, flips);

            Mouse.OnDown(null);          // some -> none: flip
            Assert.Equal(2, flips);

            Mouse.Clear();               // already none: no flip
            Assert.Equal(2, flips);
        }
        finally
        {
            Mouse.HandlersChanged -= Handler;
        }
    }

    // ── Failure policy ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AThrowingHandlerDetachesEverythingAndIsReported()
    {
        Exception? reported = null;
        void Report(Exception ex) => reported = ex;
        Mouse.CallbackFailed += Report;

        try
        {
            Mouse.OnMove(_ => throw new InvalidOperationException("boom"));
            Mouse.OnDown(_ => { });

            Mouse.RaiseMove(Move());

            Assert.NotNull(reported);
            Assert.IsType<InvalidOperationException>(reported);

            // Critically: everything detaches. A move handler runs well over a hundred times a
            // second, so leaving it attached would report continuously and reach WPF's dispatcher.
            Assert.False(Mouse.HasHandlers);
        }
        finally
        {
            Mouse.CallbackFailed -= Report;
        }
    }

    [Fact]
    public void AThrowingHandlerStillLeavesTheFrameMarkedDirty()
    {
        Mouse.OnMove(_ => throw new InvalidOperationException("boom"));

        Mouse.RaiseMove(Move());

        // The handler may well have changed the scene before it threw.
        Assert.True(Mouse.ConsumeSceneDirty());
    }

    // ── Lazy hit-testing: the performance contract ────────────────────────────────────────────────

    [Fact]
    public void TargetIsNotHitTestedUnlessRead()
    {
        var hitTests = 0;
        var e = new MouseInfo(MouseEventKind.Move, new VXYZ(1, 2), new VXYZ(1, 2), 0, 0,
            hitTest: _ => { hitTests++; return null; });

        Mouse.OnMove(_ => { /* never touches Target, like most handlers */ });
        Mouse.RaiseMove(e);

        Assert.Equal(0, hitTests);
    }

    [Fact]
    public void TargetIsHitTestedAtMostOncePerEvent()
    {
        var hitTests = 0;
        var shape = new VCircle(new VXYZ(0, 0), 5);
        var e = new MouseInfo(MouseEventKind.Move, new VXYZ(1, 2), new VXYZ(1, 2), 0, 0,
            hitTest: _ => { hitTests++; return shape; });

        Assert.Same(shape, e.Target);
        Assert.Same(shape, e.Target);
        Assert.Same(shape, e.Target);

        Assert.Equal(1, hitTests);
    }

    [Fact]
    public void TargetCachesANullResultToo()
    {
        var hitTests = 0;
        var e = new MouseInfo(MouseEventKind.Move, new VXYZ(1, 2), new VXYZ(1, 2), 0, 0,
            hitTest: _ => { hitTests++; return null; });

        Assert.Null(e.Target);
        Assert.Null(e.Target);

        Assert.Equal(1, hitTests);
    }

    [Fact]
    public void TargetIsHitTestedAtTheUnsnappedPosition()
    {
        VXYZ? asked = null;
        var e = new MouseInfo(MouseEventKind.Move,
            position: new VXYZ(10, 10),      // snapped to the grid
            rawPosition: new VXYZ(12, 13),   // where the cursor really is
            screenX: 0, screenY: 0,
            hitTest: p => { asked = p; return null; });

        _ = e.Target;

        // Hit-testing the snapped point would report whatever sits at the grid intersection rather
        // than what is under the cursor.
        Assert.NotNull(asked);
        Assert.Equal(12, asked!.X);
        Assert.Equal(13, asked.Y);
    }

    [Fact]
    public void TargetIsNullWhenNoHitTesterWasSupplied()
    {
        var e = new MouseInfo(MouseEventKind.Move, new VXYZ(0, 0), new VXYZ(0, 0), 0, 0);
        Assert.Null(e.Target);
    }

    // ── Click synthesis ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpSynthesisesAClickWhenTheButtonWentDownInTheSamePlace()
    {
        var kinds = new List<MouseEventKind>();
        Mouse.OnUp(e => kinds.Add(e.Kind));
        Mouse.OnClick(e => kinds.Add(e.Kind));

        Mouse.RaiseDown(Down(screenX: 100, screenY: 100));
        Mouse.RaiseUp(Up(screenX: 101, screenY: 100));

        // Up first, then the click derived from it — the order a JavaScript handler expects.
        Assert.Equal(new[] { MouseEventKind.Up, MouseEventKind.Click }, kinds);
    }

    [Fact]
    public void ADragDoesNotProduceAClick()
    {
        var clicks = 0;
        Mouse.OnClick(_ => clicks++);

        Mouse.RaiseDown(Down(screenX: 100, screenY: 100));
        Mouse.RaiseUp(Up(screenX: 180, screenY: 140));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void ReleasingADifferentButtonDoesNotProduceAClick()
    {
        var clicks = 0;
        Mouse.OnClick(_ => clicks++);

        Mouse.RaiseDown(Down(MouseButtonKind.Left, screenX: 10, screenY: 10));
        Mouse.RaiseUp(Up(MouseButtonKind.Right, screenX: 10, screenY: 10));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void AnUpWithNoPrecedingDownDoesNotProduceAClick()
    {
        var clicks = 0;
        Mouse.OnClick(_ => clicks++);

        // Happens for real: press outside the canvas, release inside it.
        Mouse.RaiseUp(Up(screenX: 10, screenY: 10));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void OneDownYieldsAtMostOneClick()
    {
        var clicks = 0;
        Mouse.OnClick(_ => clicks++);

        Mouse.RaiseDown(Down(screenX: 5, screenY: 5));
        Mouse.RaiseUp(Up(screenX: 5, screenY: 5));
        Mouse.RaiseUp(Up(screenX: 5, screenY: 5));

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void TheSynthesisedClickReusesTheAlreadyResolvedTarget()
    {
        var hitTests = 0;
        var shape = new VCircle(new VXYZ(0, 0), 5);

        Shape? seenByUp = null;
        Shape? seenByClick = null;
        Mouse.OnUp(e => seenByUp = e.Target);
        Mouse.OnClick(e => seenByClick = e.Target);

        Mouse.RaiseDown(Down(screenX: 7, screenY: 7));
        Mouse.RaiseUp(new MouseInfo(MouseEventKind.Up, new VXYZ(0, 0), new VXYZ(0, 0), 7, 7,
            MouseButtonKind.Left, hitTest: _ => { hitTests++; return shape; }));

        Assert.Same(shape, seenByUp);
        Assert.Same(shape, seenByClick);

        // The click must not pay for a second spatial query over the same point.
        Assert.Equal(1, hitTests);
    }

    [Fact]
    public void ADoubleClickAlsoProducesASecondClick()
    {
        var log = new List<string>();
        Mouse.OnDown(_ => log.Add("down"));
        Mouse.OnUp(_ => log.Add("up"));
        Mouse.OnClick(_ => log.Add("click"));
        Mouse.OnDoubleClick(_ => log.Add("dblclick"));

        // What WPF delivers for a double click: two down/up pairs, the second with ClickCount == 2.
        Mouse.RaiseDown(Down(screenX: 10, screenY: 10, clickCount: 1));
        Mouse.RaiseUp(Up(screenX: 10, screenY: 10));
        Mouse.RaiseDown(Down(screenX: 10, screenY: 10, clickCount: 2));
        Mouse.RaiseUp(Up(screenX: 10, screenY: 10));

        // Both clicks are reported, as the DOM does — a double click is still two clicks. Pinned
        // because it is surprising enough that someone would otherwise "fix" it.
        Assert.Equal(new[] { "down", "up", "click", "dblclick", "up", "click" }, log);
    }

    [Fact]
    public void ADoubleClickRaisesTheDoubleClickHandlerNotTheDownHandler()
    {
        var downs = 0;
        var doubles = 0;
        Mouse.OnDown(_ => downs++);
        Mouse.OnDoubleClick(_ => doubles++);

        Mouse.RaiseDown(Down(clickCount: 2));

        Assert.Equal(0, downs);
        Assert.Equal(1, doubles);
    }

    // ── Move vs drag ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MovingWithAButtonHeldRaisesDragNotMove()
    {
        var moves = 0;
        var drags = 0;
        Mouse.OnMove(_ => moves++);
        Mouse.OnDrag(_ => drags++);

        Mouse.RaiseMove(new MouseInfo(MouseEventKind.Drag, new VXYZ(0, 0), new VXYZ(0, 0), 0, 0,
            leftDown: true));

        Assert.Equal(0, moves);
        Assert.Equal(1, drags);
    }

    [Fact]
    public void MovingWithNoButtonHeldRaisesMoveNotDrag()
    {
        var moves = 0;
        var drags = 0;
        Mouse.OnMove(_ => moves++);
        Mouse.OnDrag(_ => drags++);

        Mouse.RaiseMove(Move());

        Assert.Equal(1, moves);
        Assert.Equal(0, drags);
    }

    [Fact]
    public void ADragWithNoDragHandlerDoesNotFallBackToTheMoveHandler()
    {
        var moves = 0;
        Mouse.OnMove(_ => moves++);

        Mouse.RaiseMove(new MouseInfo(MouseEventKind.Drag, new VXYZ(0, 0), new VXYZ(0, 0), 0, 0,
            leftDown: true));

        // Drag and move are distinct events; silently substituting one would make "move" fire during
        // a drag the author explicitly chose not to handle.
        Assert.Equal(0, moves);
    }

    // ── Repaint signalling ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SceneDirtyIsSetWhenAHandlerRanAndIsConsumedOnce()
    {
        Mouse.OnMove(_ => { });
        Mouse.RaiseMove(Move());

        Assert.True(Mouse.ConsumeSceneDirty());
        Assert.False(Mouse.ConsumeSceneDirty());
    }

    [Fact]
    public void SceneDirtyIsNotSetWhenNoHandlerIsRegistered()
    {
        Mouse.RaiseMove(Move());
        Mouse.RaiseDown(Down());
        Mouse.RaiseUp(Up());

        // A scene with no handlers must cost nothing per frame.
        Assert.False(Mouse.ConsumeSceneDirty());
    }

    [Fact]
    public void RaiseReturnsWhetherAHandlerRan()
    {
        Assert.False(Mouse.RaiseMove(Move()));

        Mouse.OnMove(_ => { });
        Assert.True(Mouse.RaiseMove(Move()));
    }

    // ── Polled state ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackUpdatesThePolledStateWithNoHandlersRegistered()
    {
        Mouse.Track(12.5, -3.5, isDown: true);

        Assert.Equal(12.5, Mouse.X);
        Assert.Equal(-3.5, Mouse.Y);
        Assert.True(Mouse.IsDown);
    }

    [Fact]
    public void ClearResetsIsDownButKeepsThePosition()
    {
        Mouse.Track(40, 50, isDown: true);
        Mouse.Clear();

        // The pointer is still where it is — that is the host's fact, not the handlers'.
        Assert.Equal(40, Mouse.X);
        Assert.Equal(50, Mouse.Y);

        // A stuck "button is down" would be read by the next run's code as a drag in progress.
        Assert.False(Mouse.IsDown);
    }

    [Fact]
    public void ClearForgetsAPendingDownSoNoClickIsSynthesisedAcrossRuns()
    {
        var clicks = 0;
        Mouse.OnDown(_ => { });
        Mouse.RaiseDown(Down(screenX: 20, screenY: 20));

        Mouse.Clear();
        Mouse.OnClick(_ => clicks++);
        Mouse.RaiseUp(Up(screenX: 20, screenY: 20));

        Assert.Equal(0, clicks);
    }

    // ── Payload plumbing ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ModifiersAndButtonStatesRoundTripIntoTheHandler()
    {
        MouseInfo? seen = null;
        Mouse.OnDown(e => seen = e);

        Mouse.RaiseDown(new MouseInfo(
            MouseEventKind.Down, new VXYZ(3, 4), new VXYZ(3, 4), 30, 40,
            MouseButtonKind.Middle, leftDown: true, rightDown: true, middleDown: true,
            shift: true, ctrl: true, alt: true, clickCount: 1, wheelDelta: 0, scale: 2.5));

        Assert.NotNull(seen);
        Assert.Equal(MouseButtonKind.Middle, seen!.Button);
        Assert.True(seen.LeftDown);
        Assert.True(seen.RightDown);
        Assert.True(seen.MiddleDown);
        Assert.True(seen.Shift);
        Assert.True(seen.Ctrl);
        Assert.True(seen.Alt);
        Assert.Equal(3, seen.X);
        Assert.Equal(4, seen.Y);
        Assert.Equal(30, seen.ScreenX);
        Assert.Equal(40, seen.ScreenY);
        Assert.Equal(2.5, seen.Scale);
    }

    [Theory]
    [InlineData(120, 1.0)]
    [InlineData(-120, -1.0)]
    [InlineData(240, 2.0)]
    [InlineData(0, 0.0)]
    public void WheelNotchesConvertsFromTheRawDelta(int delta, double expected)
    {
        var e = new MouseInfo(MouseEventKind.Wheel, new VXYZ(0, 0), new VXYZ(0, 0), 0, 0,
            wheelDelta: delta);

        Assert.Equal(expected, e.WheelNotches, 6);
    }

    [Fact]
    public void PositionAndRawPositionDifferOnlyWhenSnapping()
    {
        var snapped = new MouseInfo(MouseEventKind.Move, new VXYZ(10, 10), new VXYZ(12, 13), 0, 0);
        Assert.Equal(10, snapped.X);
        Assert.Equal(12, snapped.RawPosition.X);

        var unsnapped = new MouseInfo(MouseEventKind.Move, new VXYZ(12, 13), new VXYZ(12, 13), 0, 0);
        Assert.Equal(unsnapped.RawPosition.X, unsnapped.X);
    }

    [Fact]
    public void EachDispatchedEventIsADistinctObject()
    {
        var seen = new List<MouseInfo>();
        Mouse.OnMove(e => seen.Add(e));

        Mouse.RaiseMove(Move(1, 1));
        Mouse.RaiseMove(Move(2, 2));

        // Pooling one reusable payload would make a handler that stores the event see it silently
        // change underneath it.
        Assert.NotSame(seen[0], seen[1]);
        Assert.Equal(1, seen[0].X);
        Assert.Equal(2, seen[1].X);
    }

    // ── The gate ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheGateDispatchesWhileSelectionModeIsOn()
    {
        // Selection mode is ON BY DEFAULT and its branch consumes every left click. Gating user
        // handlers on it would mean a click handler never fired out of the box, which is the single
        // most likely "my handler doesn't work" report. Interactive mode suppresses selection instead.
        Assert.True(MouseGate.Allow(interactive: true, panning: false,
            drawingToolActive: false, measuring: false));
    }

    [Fact]
    public void TheGateBlocksEverythingWhenNoHandlerIsRegistered()
    {
        Assert.False(MouseGate.Allow(interactive: false, panning: false,
            drawingToolActive: false, measuring: false));
    }

    [Theory]
    // interactive, panning, drawing, measuring -> allowed
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, false)]   // middle-drag pan stays the canvas's own
    [InlineData(true, false, true, false, false)]   // an armed drawing tool owns the click
    [InlineData(true, false, false, true, false)]   // so does the measuring tape
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, true, false)]
    public void TheGateTruthTable(bool interactive, bool panning, bool drawing, bool measuring, bool allowed)
    {
        Assert.Equal(allowed, MouseGate.Allow(interactive, panning, drawing, measuring));

        // The canvas suppresses its own gesture exactly when user code is being given the event; if
        // these ever diverge, a gesture would be both consumed by the app and reported to the script.
        Assert.Equal(allowed,
            MouseGate.SuppressCanvasGesture(interactive, panning, drawing, measuring));
    }
}
