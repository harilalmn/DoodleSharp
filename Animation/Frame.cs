using System;
using System.Collections.Generic;

namespace DoodleSharp.Animation;

/// <summary>
/// Per-frame callbacks, in the shape JavaScript uses: a function that reschedules itself.
///
/// <code>
/// void Tick(double t)
/// {
///     circle.Center = new VXYZ(200 * Math.Cos(t), 200 * Math.Sin(t));
///     Frame.Request(Tick);          // ask for the next one
/// }
///
/// Frame.Request(Tick);              // start it
/// </code>
///
/// <para>
/// This exists because composing an <see cref="Animator"/> and adding <c>Animation</c> objects to
/// it is a lot of ceremony for "move this a bit each frame". The timeline system is not going
/// anywhere — it is what makes animation <i>seekable</i>, which is what the scrub bar and
/// deterministic GIF/MP4 export are built on, and a self-rescheduling callback can never offer
/// that. The two answer different questions: <c>Frame</c> is for open-ended, interactive or
/// procedural motion; the timeline is for a finite, scrubbable, exportable sequence.
/// </para>
///
/// <para>
/// Callbacks receive elapsed seconds since the loop started, matching the timestamp JavaScript
/// passes to <c>requestAnimationFrame</c>. Writing motion as a function of that value rather than
/// accumulating state keeps it frame-rate independent — and is what would let a sketch be made
/// seekable later.
/// </para>
/// </summary>
public static class Frame
{
    private static readonly object _lock = new();

    // Two queues, swapped each pump. A callback that calls Request during the pump must run on the
    // NEXT frame, not this one -- which is precisely what makes the self-rescheduling idiom work.
    // Draining a single list in place would re-enter the callback forever and hang the UI thread.
    private static readonly Dictionary<long, Action<double>> _pending = new();
    private static readonly Dictionary<long, Action<double>> _running = new();

    private static long _nextId = 1;

    /// <summary>Raised when a callback throws, so the host can report it and stop the loop.</summary>
    public static event Action<Exception>? CallbackFailed;

    /// <summary>True while at least one callback is queued — the host's frame loop checks this.</summary>
    public static bool HasPending
    {
        get { lock (_lock) return _pending.Count > 0; }
    }

    /// <summary>
    /// Queues <paramref name="callback"/> for the next frame and returns a handle for
    /// <see cref="Cancel"/>. Requesting the same method twice queues it twice, as in JavaScript.
    /// </summary>
    public static long Request(Action<double> callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        lock (_lock)
        {
            var id = _nextId++;
            _pending[id] = callback;
            return id;
        }
    }

    /// <summary>Convenience for a callback that does not need the timestamp.</summary>
    public static long Request(Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return Request(_ => callback());
    }

    /// <summary>Removes a queued callback. Unknown or already-run handles are ignored.</summary>
    public static void Cancel(long id)
    {
        lock (_lock) _pending.Remove(id);
    }

    /// <summary>
    /// Drops every queued callback.
    ///
    /// <para>
    /// <b>The host must call this before each run, and this is not optional.</b> User code is
    /// compiled into a collectible <c>AssemblyLoadContext</c>; a delegate left in this queue points
    /// into that assembly and pins it, so the context never unloads and the previous run's
    /// callbacks keep firing against shapes the new run has already replaced.
    /// </para>
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
            _running.Clear();
        }
    }

    /// <summary>
    /// Runs everything queued. Called once per frame by the host; returns true if any callback ran,
    /// so the host knows whether the scene needs repainting.
    /// </summary>
    /// <param name="elapsedSeconds">Seconds since the loop started, passed to each callback.</param>
    public static bool Pump(double elapsedSeconds)
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return false;

            // Swap, so anything requested from inside a callback lands in the now-empty _pending
            // and waits for the next frame.
            _running.Clear();
            foreach (var kvp in _pending) _running[kvp.Key] = kvp.Value;
            _pending.Clear();
        }

        foreach (var kvp in _running)
        {
            try
            {
                kvp.Value(elapsedSeconds);
            }
            catch (Exception ex)
            {
                // One bad callback stops the whole loop rather than throwing sixty times a second.
                // User code runs in-process; an unhandled exception here reaches WPF's dispatcher
                // and takes the application down.
                Clear();
                CallbackFailed?.Invoke(ex);
                return true;
            }
        }

        _running.Clear();
        return true;
    }
}
