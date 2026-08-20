using System;
using System.Linq;
using DoodleSharp.Console;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="ConsoleOutput.BeginRewrite"/> / <see cref="ConsoleOutput.EndRewrite"/> — re-running an
/// unchanged program must not disturb the console.
///
/// <para>
/// Auto-Run re-executes every 500 ms. The resident re-run used to clear the console and write the
/// same lines back, announcing a change for each step, so the panel emptied and refilled twice a
/// second. Worse, the emptying and the first line landed in the same beat while anything written
/// after <c>Main()</c> returned — the unnamed-shape warning — waited out the 50 ms update throttle,
/// so that one line visibly blinked on its own while the rest looked steady.
/// </para>
///
/// <para>
/// The property that kills the flicker is the first test here: identical output announces
/// <b>nothing</b>. The others pin the surrounding contract, because a staging buffer that leaks —
/// half-built output made visible, or a run that throws never giving the console back — would be a
/// worse defect than the flicker it replaced.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ConsoleRewriteTests : IDisposable
{
    private readonly ConsoleOutput _console = ConsoleOutput.Instance;
    private int _announcements;

    public ConsoleRewriteTests()
    {
        // EndRewrite is harmless without a Begin, so this also unwinds a rewrite a previous failing
        // test left open.
        _console.EndRewrite();
        _console.Clear();
        _console.OutputChanged += Count;
    }

    public void Dispose()
    {
        _console.OutputChanged -= Count;
        _console.EndRewrite();
        _console.Clear();
    }

    private void Count(object? sender, EventArgs e) => _announcements++;

    /// <summary>
    /// Drains the 50 ms update throttle and zeroes the counter, so what a test then counts is what
    /// the rewrite itself announced rather than a timer left running by the setup writes.
    /// </summary>
    private void SettleAndReset()
    {
        _console.Flush();
        _announcements = 0;
    }

    private static void WriteTheUsualRun()
    {
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.WriteLine("StartViz", 22, "road1");
        ConsoleOutput.Instance.WriteLine("DoodleSharp", 0, "Warning: 1 unnamed shape(s) hidden (1 VLine).");
    }

    [Fact]
    public void ARerunProducingTheSameOutputAnnouncesNothing()
    {
        WriteTheUsualRun();
        var displayed = _console.GetEntries();
        SettleAndReset();

        _console.BeginRewrite();
        WriteTheUsualRun();
        _console.EndRewrite();

        // Nothing to redraw, so nothing is said — this is the whole fix. Twice a second, forever.
        Assert.Equal(0, _announcements);

        // And the displayed entries are the same objects, so the panel's rows are not even rebuilt:
        // MainWindow.RefreshConsole finds the shared prefix by reference.
        Assert.Equal(displayed.Select(e => e.Message), _console.GetEntries().Select(e => e.Message));
        Assert.True(displayed.Zip(_console.GetEntries()).All(p => ReferenceEquals(p.First, p.Second)));
    }

    [Fact]
    public void ARerunProducingDifferentOutputAnnouncesExactlyOnce()
    {
        WriteTheUsualRun();
        SettleAndReset();

        _console.BeginRewrite();
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.WriteLine("StartViz", 22, "road2");
        _console.EndRewrite();

        // One announcement for the whole run, not one per line and one for the clear.
        Assert.Equal(1, _announcements);
        Assert.Equal(new[] { "road2" }, _console.GetEntries().Select(e => e.Message));
    }

    [Fact]
    public void OutputStagedByARewriteIsInvisibleUntilItCloses()
    {
        WriteTheUsualRun();

        _console.BeginRewrite();
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.WriteLine("StartViz", 1, "halfway through");

        // The PANEL still shows the previous run in full: no blank frame, no partial run on screen.
        // (What the running program sees is a different question, and a different test.)
        Assert.Equal(new[] { "road1", "Warning: 1 unnamed shape(s) hidden (1 VLine)." },
            _console.GetDisplayedEntries().Select(e => e.Message).ToArray());

        _console.EndRewrite();
        Assert.Equal(new[] { "halfway through" }, _console.GetDisplayedEntries().Select(e => e.Message));
    }

    /// <summary>
    /// User code runs <em>inside</em> the rewrite — an Auto-Run tick on unedited source, and a Global
    /// Parameters change, both re-invoke Main() on that path. So a program that logs and then reads
    /// its own output back must get what it just wrote, not what the panel happens to be showing.
    /// Answering with the visible list would have handed it the PREVIOUS run's lines: a silent wrong
    /// answer, and a far worse defect than the flicker this all came from.
    /// </summary>
    [Fact]
    public void TheRunningProgramReadsBackItsOwnOutputNotThePanels()
    {
        WriteTheUsualRun();

        _console.BeginRewrite();
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.WriteLine("StartViz", 3, "this run");

        Assert.Equal(new[] { "this run" }, _console.GetEntries().Select(e => e.Message));
        Assert.Contains("this run", _console.GetFormattedOutput());
        Assert.DoesNotContain("road1", _console.GetFormattedOutput());

        // The panel meanwhile still shows the finished previous run, which is what keeps half-built
        // output off the screen.
        Assert.Equal(new[] { "road1", "Warning: 1 unnamed shape(s) hidden (1 VLine)." },
            _console.GetDisplayedEntries().Select(e => e.Message).ToArray());
        Assert.Contains("road1", _console.GetDisplayedOutput());

        _console.EndRewrite();

        // Once the run is over the two agree again.
        Assert.Equal(_console.GetEntries().Select(e => e.Message),
                     _console.GetDisplayedEntries().Select(e => e.Message));
    }

    [Fact]
    public void EndRewriteWithoutABeginIsHarmless()
    {
        WriteTheUsualRun();
        SettleAndReset();

        // It lives in a finally, so it is reached on paths that never opened one.
        _console.EndRewrite();

        Assert.Equal(0, _announcements);
        Assert.Equal(2, _console.GetEntries().Count);
    }

    [Fact]
    public void ARewriteThatIsAbandonedMidRunStillGivesTheConsoleBack()
    {
        WriteTheUsualRun();

        _console.BeginRewrite();
        ConsoleOutput.Instance.WriteLine("StartViz", 1, "before the throw");
        _console.EndRewrite();   // the finally in ReExecuteResidentAsync

        SettleAndReset();
        ConsoleOutput.Instance.WriteError("Auto-Run", 0, "Runtime Error: ...");

        // Writing works normally again; a stuck rewrite would have swallowed this and frozen the
        // panel on the last good output for the rest of the session.
        Assert.Equal(1, _announcements);
        Assert.Contains(_console.GetEntries(), e => e.Message.StartsWith("Runtime Error"));
    }
}
