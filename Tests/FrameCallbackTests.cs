using System;
using System.Collections.Generic;
using Xunit;
using DoodleSharp.Animation;

namespace DoodleSharp.Tests;

/// <summary>
/// The requestAnimationFrame-style callback queue.
///
/// <para>
/// The property everything else rests on is that a callback which requests another one does not
/// run again in the same pump. That is what makes the self-rescheduling idiom — the whole point of
/// the API — terminate instead of spinning the UI thread forever.
/// </para>
/// </summary>
public class FrameCallbackTests : IDisposable
{
    public FrameCallbackTests() => Frame.Clear();
    public void Dispose() => Frame.Clear();

    [Fact]
    public void SelfReschedulingRunsOncePerPump()
    {
        var runs = 0;

        void Tick(double t)
        {
            runs++;
            Frame.Request(Tick);   // the JavaScript idiom
        }

        Frame.Request(Tick);

        // Without the queue swap this pump would re-enter Tick forever and hang.
        Frame.Pump(0.0);
        Assert.Equal(1, runs);

        Frame.Pump(0.016);
        Assert.Equal(2, runs);

        Frame.Pump(0.032);
        Assert.Equal(3, runs);
    }

    [Fact]
    public void ACallbackThatDoesNotRescheduleRunsExactlyOnce()
    {
        var runs = 0;
        Frame.Request(() => runs++);

        Frame.Pump(0);
        Frame.Pump(0.016);
        Frame.Pump(0.032);

        Assert.Equal(1, runs);
        Assert.False(Frame.HasPending);
    }

    [Fact]
    public void CallbacksReceiveTheElapsedTimestamp()
    {
        var seen = new List<double>();
        Frame.Request(t => { seen.Add(t); Frame.Request(t2 => seen.Add(t2)); });

        Frame.Pump(1.5);
        Frame.Pump(2.25);

        Assert.Equal(new[] { 1.5, 2.25 }, seen);
    }

    [Fact]
    public void CancelStopsAQueuedCallback()
    {
        var runs = 0;
        var id = Frame.Request(() => runs++);
        Frame.Cancel(id);

        Frame.Pump(0);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void CancellingAnUnknownHandleIsHarmless()
    {
        Frame.Cancel(9999);
        Frame.Cancel(-1);
        Assert.False(Frame.HasPending);
    }

    [Fact]
    public void RequestingTheSameMethodTwiceQueuesItTwice()
    {
        var runs = 0;
        void Tick() => runs++;

        Frame.Request(Tick);
        Frame.Request(Tick);
        Frame.Pump(0);

        // Matches JavaScript: the queue holds requests, not a set of callbacks.
        Assert.Equal(2, runs);
    }

    [Fact]
    public void AThrowingCallbackStopsTheLoopAndIsReported()
    {
        Exception? reported = null;
        void Handler(Exception ex) => reported = ex;
        Frame.CallbackFailed += Handler;

        try
        {
            void Bad(double t)
            {
                Frame.Request(Bad);
                throw new InvalidOperationException("boom");
            }

            Frame.Request(Bad);
            Frame.Pump(0);

            Assert.NotNull(reported);
            Assert.IsType<InvalidOperationException>(reported);

            // Critically: the loop stops. User code runs in-process, and a callback throwing sixty
            // times a second would otherwise reach WPF's dispatcher and take the app down.
            Assert.False(Frame.HasPending);
        }
        finally
        {
            Frame.CallbackFailed -= Handler;
        }
    }

    [Fact]
    public void ClearDropsEverythingQueued()
    {
        var runs = 0;
        Frame.Request(() => runs++);
        Frame.Request(() => runs++);

        // The host calls this before every run: a delegate left queued points into the previous
        // run's collectible assembly and pins it so the load context never unloads.
        Frame.Clear();
        Frame.Pump(0);

        Assert.Equal(0, runs);
        Assert.False(Frame.HasPending);
    }

    [Fact]
    public void PumpingAnEmptyQueueIsFreeAndReportsNothingRan()
    {
        Assert.False(Frame.Pump(0));
        Assert.False(Frame.HasPending);
    }

    [Fact]
    public void HasPendingTracksTheQueue()
    {
        Assert.False(Frame.HasPending);

        Frame.Request(() => { });
        Assert.True(Frame.HasPending);

        Frame.Pump(0);
        Assert.False(Frame.HasPending);
    }

    [Fact]
    public void RequestRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Frame.Request((Action)null!));
        Assert.Throws<ArgumentNullException>(() => Frame.Request((Action<double>)null!));
    }
}
