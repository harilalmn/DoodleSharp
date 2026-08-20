using System.Text;
using System.Timers;
using DoodleSharp.Services;

namespace DoodleSharp.Console;

public class ConsoleEntry
{
    public string ModuleName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsNewLine { get; set; }
    public bool IsError { get; set; }

    // For clickable error navigation
    public string? FilePath { get; set; }
    public int Column { get; set; }
    public bool IsClickable => !string.IsNullOrEmpty(FilePath) && LineNumber > 0;

    /// <summary>
    /// Value equality over everything the console displays. Used by
    /// <see cref="ConsoleOutput.EndRewrite"/> to tell a re-run that produced the same output from
    /// one that produced different output.
    /// </summary>
    internal bool SameContentAs(ConsoleEntry other) =>
        ModuleName == other.ModuleName &&
        LineNumber == other.LineNumber &&
        Column == other.Column &&
        Message == other.Message &&
        IsNewLine == other.IsNewLine &&
        IsError == other.IsError &&
        FilePath == other.FilePath;
}

public class ConsoleOutput : IConsoleOutput
{
    private static readonly Lazy<ConsoleOutput> _instance = new(() => new ConsoleOutput());
    public static ConsoleOutput Instance => _instance.Value;

    private readonly List<ConsoleEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>
    /// Non-null while a <see cref="BeginRewrite"/>/<see cref="EndRewrite"/> pair is open: new output
    /// accumulates here instead of in <see cref="_entries"/>, and no change is announced until the
    /// pair closes. See <see cref="BeginRewrite"/> for why.
    /// </summary>
    private List<ConsoleEntry>? _staging;

    // Throttling for UI updates
    private readonly System.Timers.Timer _throttleTimer;
    private bool _pendingUpdate = false;
    private const int ThrottleIntervalMs = 50;

    public event EventHandler? OutputChanged;

    private ConsoleOutput()
    {
        _throttleTimer = new System.Timers.Timer(ThrottleIntervalMs);
        _throttleTimer.Elapsed += OnThrottleTimerElapsed;
        _throttleTimer.AutoReset = false;
    }

    private void OnThrottleTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_pendingUpdate)
        {
            _pendingUpdate = false;
            OutputChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotifyOutputChanged()
    {
        if (!_throttleTimer.Enabled)
        {
            // First update - fire immediately and start throttle timer
            OutputChanged?.Invoke(this, EventArgs.Empty);
            _throttleTimer.Start();
        }
        else
        {
            // Subsequent updates within throttle window - mark as pending
            _pendingUpdate = true;
        }
    }

    /// <summary>
    /// Adds one entry to whichever list is currently taking output, and announces the change unless
    /// a rewrite is open (see <see cref="BeginRewrite"/>).
    /// </summary>
    private void Append(ConsoleEntry entry)
    {
        bool announce;
        lock (_lock)
        {
            (_staging ?? _entries).Add(entry);
            announce = _staging == null;
        }
        if (announce) NotifyOutputChanged();
    }

    public void WriteLine(string moduleName, int lineNumber, string message)
    {
        Append(new ConsoleEntry
        {
            ModuleName = moduleName,
            LineNumber = lineNumber,
            Message = message,
            IsNewLine = true,
            IsError = false
        });
    }

    public void WriteError(string moduleName, int lineNumber, string message)
    {
        Append(new ConsoleEntry
        {
            ModuleName = moduleName,
            LineNumber = lineNumber,
            Message = message,
            IsNewLine = true,
            IsError = true
        });
    }

    /// <summary>
    /// Writes a compilation error with full location info for click-to-navigate.
    /// </summary>
    public void WriteCompilationError(string filePath, int lineNumber, int column, string message)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        Append(new ConsoleEntry
        {
            ModuleName = fileName,
            LineNumber = lineNumber,
            Column = column,
            FilePath = filePath,
            Message = message,
            IsNewLine = true,
            IsError = true
        });
    }

    /// <summary>
    /// Adds a custom entry (e.g., for Find References output).
    /// </summary>
    public void AddEntry(string message, string? filePath = null, int lineNumber = 0, int column = 0, bool isError = false)
    {
        Append(new ConsoleEntry
        {
            ModuleName = filePath != null ? System.IO.Path.GetFileName(filePath) : "",
            LineNumber = lineNumber,
            Column = column,
            FilePath = filePath,
            Message = message,
            IsNewLine = true,
            IsError = isError
        });
    }

    public void Clear()
    {
        bool announce;
        lock (_lock)
        {
            // Inside a rewrite this is the run clearing its own output, not the console being
            // emptied — the visible list stays untouched until the rewrite closes.
            (_staging ?? _entries).Clear();
            announce = _staging == null;
        }
        if (!announce) return;

        _pendingUpdate = false;
        OutputChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts a rewrite: output written from here until <see cref="EndRewrite"/> accumulates out of
    /// sight, and the console keeps showing the previous contents meanwhile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for re-running an unchanged program. Auto-Run re-executes every 500 ms, and each
    /// re-execution used to <see cref="Clear"/> the console and then write the same lines back. Each
    /// of those steps announced a change, so twice a second the panel emptied and refilled — and
    /// because the emptying and the first line landed together while anything written after
    /// <c>Main()</c> returned (the unnamed-shape warning) waited out the 50 ms update throttle, that
    /// last line visibly blinked on its own.
    /// </para>
    ///
    /// <para>
    /// Staging the output and swapping it in at the end collapses that to one announcement — and to
    /// none at all when the text is identical, which for a program nobody is editing is every time.
    /// </para>
    ///
    /// <para>
    /// Only for output a run produces quickly and in full. A long-running <c>Main()</c> that logs as
    /// it goes must not be wrapped in one, or its progress stays invisible until it finishes.
    /// </para>
    ///
    /// <para>
    /// Internal on purpose: this is host machinery, not something user code should reach. It is
    /// paired and stateful, and a sketch that opened one and never closed it would silence its own
    /// console for the rest of the session. <c>VizConsole.Log</c> stays the API for scripting.
    /// </para>
    /// </remarks>
    internal void BeginRewrite()
    {
        lock (_lock)
        {
            _staging ??= new List<ConsoleEntry>();
        }
    }

    /// <summary>
    /// Ends the rewrite started by <see cref="BeginRewrite"/>, swapping the staged output in.
    /// Announces the change only if the text actually differs from what is already displayed.
    /// Safe to call without a matching <see cref="BeginRewrite"/>, so it belongs in a <c>finally</c>.
    /// </summary>
    internal void EndRewrite()
    {
        lock (_lock)
        {
            var staged = _staging;
            _staging = null;
            if (staged == null) return;

            if (Same(_entries, staged)) return;

            _entries.Clear();
            _entries.AddRange(staged);
        }

        _pendingUpdate = false;
        OutputChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool Same(List<ConsoleEntry> a, List<ConsoleEntry> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].SameContentAs(b[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Flushes any pending updates immediately. Call after code execution completes.
    /// </summary>
    public void Flush()
    {
        _throttleTimer.Stop();
        if (_pendingUpdate)
        {
            _pendingUpdate = false;
            OutputChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string GetFormattedOutput()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var entry in _entries)
            {
                var prefix = $"[{entry.ModuleName}:{entry.LineNumber}] ";
                sb.Append(prefix);
                sb.Append(entry.Message);
                if (entry.IsNewLine)
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
    }

    public IReadOnlyList<ConsoleEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }
}
