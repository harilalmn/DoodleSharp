using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DoodleSharp.Diagnostics;

/// <summary>
/// Wires a WPF application into the <see cref="Journal"/>: global exception handlers, a UI-thread
/// hang watchdog, first-chance exception tracing and the clean-exit marker.
///
/// <para>
/// Call <see cref="Install"/> as the first statement of <c>App.OnStartup</c>.
/// </para>
///
/// <para>
/// What this can and cannot catch. Managed exceptions on the UI thread, on background threads and in
/// unobserved tasks are all captured with full stacks. <b>StackOverflowException, AccessViolation and
/// Environment.FailFast cannot be handled by any .NET Core process</b> — the CLR terminates
/// immediately. For those, the evidence is the journal itself: because every record is flushed as it
/// is written, the last lines on disk are the last things that ran. That is the whole reason for the
/// synchronous writer, the heartbeat and the ENTER/EXIT scopes.
/// </para>
/// </summary>
public static class AppDiagnostics
{
    private static bool _installed;
    private static Application? _application;
    private static UiWatchdog? _watchdog;

    // First-chance tracing is deliberately noisy-by-design but bounded: WPF and Roslyn throw and
    // swallow internally all the time, so unbounded logging would bury the signal and cost real time.
    private static readonly Dictionary<string, int> FirstChanceSeen = new(StringComparer.Ordinal);
    private static readonly object FirstChanceGate = new();
    private static int _firstChanceLogged;
    private const int FirstChanceGlobalCap = 400;
    private const int FirstChancePerSiteCap = 5;

    [ThreadStatic] private static bool _inFirstChanceHandler;

    /// <summary>
    /// Starts the journal and attaches every crash hook. Idempotent and exception-proof.
    /// </summary>
    /// <param name="application">The WPF application, or null in a non-WPF host.</param>
    /// <param name="appName">Name written into the journal header, e.g. "DoodleSharp".</param>
    /// <param name="args">Command line arguments, recorded in the header.</param>
    public static void Install(Application? application, string appName, string[]? args = null)
    {
        if (_installed) return;
        _installed = true;

        try
        {
            Journal.Start(appName, args);
            if (!Journal.IsEnabled) return;

            _application = application;

            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            if (application != null)
            {
                application.DispatcherUnhandledException += OnDispatcherUnhandledException;
                application.SessionEnding += OnSessionEnding;
                application.Exit += OnApplicationExit;

                DescribeWpfEnvironment();

                _watchdog = new UiWatchdog(application.Dispatcher);
                _watchdog.Start();
            }

            Journal.Info("DIAG.INSTALL.OK", $"Diagnostics installed for {appName}",
                $"journal={Journal.FilePath}");
        }
        catch (Exception ex)
        {
            Journal.Error("DIAG.INSTALL.FAIL", "Diagnostics installation failed", ex);
        }
    }

    /// <summary>
    /// Records a normal shutdown. Called from the <c>Exit</c>/<c>ProcessExit</c> handlers; safe to
    /// call more than once.
    /// </summary>
    public static void Shutdown(string reason)
    {
        try
        {
            _watchdog?.Stop();
            Journal.CaptureState("shutdown");
            Journal.MarkCleanExit(reason);
        }
        catch { }
    }

    // ── Global exception handlers ────────────────────────────────────────────

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // This is the single most common crash path in a WPF app: an exception escaping an event
        // handler on the UI thread. Handled == false means the app is about to die.
        Journal.Fatal("CRASH.WPF.DISPATCHER", "Unhandled exception on the WPF dispatcher thread",
            e.Exception, $"handled={e.Handled}");
        Journal.CaptureState("dispatcher-unhandled");
        Journal.Flush();
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Journal.Fatal("CRASH.APPDOMAIN", "Unhandled exception reached the AppDomain",
            exception, $"terminating={e.IsTerminating} raw={e.ExceptionObject?.GetType().FullName ?? "<null>"}");
        Journal.CaptureState("appdomain-unhandled");
        Journal.Flush();
    }

    private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        // Not fatal by default in .NET Core, but an unobserved fault very often is the first symptom
        // of the state corruption that kills the process a few seconds later.
        Journal.Error("CRASH.TASK.UNOBSERVED", "Faulted Task was never observed", e.Exception,
            $"observed={e.Observed}");
        try { e.SetObserved(); } catch { }
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Runs for EVERY throw, before any catch — including on the thread that is about to crash,
        // and including exceptions that some outer frame silently swallows. That makes it the best
        // available breadcrumb for "the app died and nothing was logged". It must therefore be
        // reentrancy-safe, allocation-light and bounded.
        if (_inFirstChanceHandler) return;

        try
        {
            _inFirstChanceHandler = true;

            if (_firstChanceLogged >= FirstChanceGlobalCap) return;

            var exception = e.Exception;
            var frame = TopUserFrame(exception);
            var siteId = exception.GetType().FullName + "@" + frame;

            int seen;
            lock (FirstChanceGate)
            {
                FirstChanceSeen.TryGetValue(siteId, out seen);
                FirstChanceSeen[siteId] = seen + 1;
            }

            // Log the first few occurrences of each distinct site, then only at powers of ten, so a
            // tight throwing loop shows up as a rising count instead of a million lines.
            var occurrence = seen + 1;
            if (occurrence > FirstChancePerSiteCap && occurrence % 100 != 0) return;

            Interlocked.Increment(ref _firstChanceLogged);
            Journal.Debug("DIAG.FIRSTCHANCE",
                $"{exception.GetType().FullName}: {exception.Message}",
                $"occurrence={occurrence} at={frame}");
        }
        catch { }
        finally
        {
            _inFirstChanceHandler = false;
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs e)
    {
        // Late loads matter: a crash right after a plugin/NuGet dependency lands is a strong hint,
        // and the user-code assemblies produced by every Run show up here with their unique names.
        try
        {
            var name = e.LoadedAssembly.GetName();
            Journal.Debug("DIAG.ASSEMBLY.LOAD", name.Name ?? "<unnamed>",
                $"version={name.Version} dynamic={e.LoadedAssembly.IsDynamic} location={(e.LoadedAssembly.IsDynamic ? "<dynamic>" : e.LoadedAssembly.Location)}");
        }
        catch { }
    }

    private static void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Journal.Info("DIAG.SESSION.ENDING", "Windows is ending the session", $"reason={e.ReasonSessionEnding}");
    }

    private static void OnApplicationExit(object sender, ExitEventArgs e)
    {
        Shutdown($"Application.Exit code={e.ApplicationExitCode}");
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // Fires for shutdown paths that bypass Application.Exit (Environment.Exit, host teardown).
        Shutdown("ProcessExit");
    }

    // ── WPF-specific environment ─────────────────────────────────────────────

    private static void DescribeWpfEnvironment()
    {
        try
        {
            var tier = RenderCapability.Tier >> 16;
            var builder = new StringBuilder();
            builder.Append("renderTier=").Append(tier);
            builder.Append(" processRenderMode=").Append(RenderOptions.ProcessRenderMode);
            builder.Append(" pixelShader30=").Append(RenderCapability.IsPixelShaderVersionSupported(3, 0));

            // Tier 0 means WPF is rendering entirely in software — on a machine that should have a
            // GPU that usually means the display driver fell over or was blacklisted, which is
            // exactly the situation that also produces sporadic hard crashes.
            Journal.Info("DIAG.WPF.RENDER", tier == 0
                ? "WPF is using SOFTWARE rendering (tier 0)"
                : $"WPF hardware rendering tier {tier}", builder.ToString());

            Journal.Info("DIAG.WPF.SCREEN", DescribeScreens());

            RenderCapability.TierChanged += (_, _) =>
                Journal.Warn("DIAG.WPF.TIER_CHANGED", "Render tier changed at runtime — display driver reset?",
                    $"newTier={RenderCapability.Tier >> 16}");
        }
        catch (Exception ex)
        {
            Journal.Warn("DIAG.WPF.PROBE_FAIL", "Could not probe WPF render capabilities", null, ex);
        }
    }

    /// <summary>
    /// Screen geometry and DPI via <see cref="SystemParameters"/> only — deliberately not WinForms'
    /// <c>Screen.AllScreens</c>, so this file stays usable in a host that does not enable
    /// <c>UseWindowsForms</c>. Multi-monitor and fractional-DPI setups are a recurring source of WPF
    /// layout and rendering faults, so the numbers are worth having.
    /// </summary>
    private static string DescribeScreens()
    {
        try
        {
            var culture = CultureInfo.InvariantCulture;
            var builder = new StringBuilder();
            builder.Append("primary=").Append(SystemParameters.PrimaryScreenWidth.ToString("F0", culture))
                   .Append('x').Append(SystemParameters.PrimaryScreenHeight.ToString("F0", culture));
            builder.Append(" virtual=").Append(SystemParameters.VirtualScreenWidth.ToString("F0", culture))
                   .Append('x').Append(SystemParameters.VirtualScreenHeight.ToString("F0", culture));
            builder.Append(" workarea=").Append(SystemParameters.WorkArea.ToString(culture));

            var source = _application?.MainWindow != null
                ? PresentationSource.FromVisual(_application.MainWindow)
                : null;
            if (source?.CompositionTarget != null)
            {
                builder.Append(" dpiScaleX=").Append(source.CompositionTarget.TransformToDevice.M11.ToString("F2", culture));
                builder.Append(" dpiScaleY=").Append(source.CompositionTarget.TransformToDevice.M22.ToString("F2", culture));
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.GetType().Name}>";
        }
    }

    /// <summary>Best-effort "where was this thrown" for a first-chance record: the first stack frame.</summary>
    private static string TopUserFrame(Exception exception)
    {
        try
        {
            var stack = exception.StackTrace;
            if (string.IsNullOrEmpty(stack)) return "<no stack>";
            var newline = stack.IndexOf('\n');
            var first = (newline > 0 ? stack[..newline] : stack).Trim();
            return first.Length > 160 ? first[..160] : first;
        }
        catch { return "<unknown>"; }
    }

    /// <summary>
    /// Detects a wedged UI thread. A background thread posts a low-priority ping to the dispatcher; if
    /// the ping is not processed within the threshold, the UI is blocked and the journal says so —
    /// with a timestamp, so a subsequent hard kill (by the user, or by Windows' hung-window handling)
    /// is explained rather than mysterious.
    /// </summary>
    private sealed class UiWatchdog
    {
        private const int PollMs = 2000;
        private const int HangThresholdMs = 5000;

        private readonly Dispatcher _dispatcher;
        private Thread? _thread;
        private volatile bool _running;
        private volatile bool _pingOutstanding;
        private long _pingSentAt;
        private bool _hangReported;

        internal UiWatchdog(Dispatcher dispatcher) => _dispatcher = dispatcher;

        internal void Start()
        {
            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "DoodleSharp.UiWatchdog",
                Priority = ThreadPriority.BelowNormal
            };
            _thread.Start();
        }

        internal void Stop() => _running = false;

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    Thread.Sleep(PollMs);
                    if (!_running || _dispatcher.HasShutdownStarted) return;

                    if (!_pingOutstanding)
                    {
                        _pingOutstanding = true;
                        Interlocked.Exchange(ref _pingSentAt, Environment.TickCount64);

                        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            var waited = Environment.TickCount64 - Interlocked.Read(ref _pingSentAt);
                            _pingOutstanding = false;

                            if (_hangReported)
                            {
                                _hangReported = false;
                                Journal.Warn("DIAG.UI.HANG_END", "UI thread responsive again",
                                    $"blockedFor={waited}ms");
                            }
                            Journal.Activity("ui.ping");
                        }));
                    }
                    else
                    {
                        var waited = Environment.TickCount64 - Interlocked.Read(ref _pingSentAt);
                        if (waited >= HangThresholdMs)
                        {
                            _hangReported = true;
                            Journal.Warn("DIAG.UI.HANG", "UI thread has not processed a dispatcher ping",
                                $"blockedFor={waited}ms threshold={HangThresholdMs}ms");
                        }
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException) { return; }   // dispatcher shut down under us
                catch (Exception ex)
                {
                    Journal.Warn("DIAG.UI.WATCHDOG_FAIL", "UI watchdog loop error", null, ex);
                    return;
                }
            }
        }
    }
}
