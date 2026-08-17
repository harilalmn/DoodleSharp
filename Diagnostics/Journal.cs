using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace DoodleSharp.Diagnostics;

/// <summary>Severity of a journal record. <see cref="Journal.MinimumLevel"/> filters below this.</summary>
public enum JournalLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5
}

/// <summary>
/// Crash-forensics journal. Writes a flushed, line-oriented log to
/// <c>%TEMP%\DoodleSharp\YYYYMMDDhhmmss.log</c> for the lifetime of the process.
///
/// <para>
/// Design constraints, in order of importance:
/// <list type="number">
///   <item><b>Survive the crash.</b> Every record is written and flushed to the OS synchronously,
///   so the last line on disk is the last thing that happened before the process died — including
///   for uncatchable failures (StackOverflowException, AccessViolationException, FailFast) where no
///   handler ever runs. That is why there is no background writer queue.</item>
///   <item><b>Pin the exact source location.</b> Each record carries a <i>site key</i> (a
///   hand-assigned, repo-unique identifier like <c>MW.RUN.BEGIN</c>) <i>and</i> the compiler-captured
///   <c>file:line member</c> of the call site. The key survives refactoring that moves line numbers;
///   the line number survives a key being copy-pasted. Uniqueness of keys is enforced by
///   <c>Tests/JournalSiteKeyTests.cs</c> — which scans for <c>Journal.</c>-qualified calls, so even
///   the self-calls inside this file are written <c>Journal.Info(...)</c> rather than <c>Info(...)</c>.</item>
///   <item><b>Never take the app down.</b> Every public entry point swallows its own exceptions.
///   A broken journal must never be the reason the app fails.</item>
/// </list>
/// </para>
///
/// <para>Environment overrides (read once, at <see cref="Start"/>):
/// <list type="bullet">
///   <item><c>DOODLESHARP_JOURNAL=0</c> — disable journaling entirely.</item>
///   <item><c>DOODLESHARP_JOURNAL_LEVEL=Trace|Debug|Info|Warn|Error|Fatal</c> — minimum level (default Debug).</item>
///   <item><c>DOODLESHARP_JOURNAL_SYNC=1</c> — write-through to disk (survives a machine-level crash/BSOD; slower).</item>
///   <item><c>DOODLESHARP_JOURNAL_DIR=&lt;path&gt;</c> — override the journal folder (used by tests).</item>
/// </list>
/// </para>
/// </summary>
public static class Journal
{
    /// <summary>Folder created under the current user's temp directory.</summary>
    public const string FolderName = "DoodleSharp";

    private const long DefaultMaxFileBytes = 64L * 1024 * 1024;
    private const int MaxRetainedFiles = 60;
    private static readonly TimeSpan RetentionAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();
    private static readonly List<(string Name, Func<string?> Provider)> StateProviders = new();
    private static readonly Dictionary<string, long> ActivityCounts = new(StringComparer.Ordinal);

    private static StreamWriter? _writer;
    private static Timer? _heartbeat;
    private static Process? _self;
    private static long _sequence;
    private static long _bytesWritten;
    private static bool _started;
    private static bool _enabled;
    private static bool _truncated;
    private static string _appName = "DoodleSharp";
    private static string _lastActivityKey = "-";
    private static long _lastHeartbeatWorkingSet;

    /// <summary>True once <see cref="Start"/> has successfully opened a journal file.</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>Full path of the journal file for this session, or null when disabled.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>The folder journals are written to (<c>%TEMP%\DoodleSharp</c> unless overridden).</summary>
    public static string Directory => _directory ??= ResolveDirectory();
    private static string? _directory;

    /// <summary>Session id — matches the journal file name (<c>YYYYMMDDhhmmss</c>).</summary>
    public static string SessionId { get; private set; } = string.Empty;

    /// <summary>Records below this level are dropped. Default <see cref="JournalLevel.Debug"/>.</summary>
    public static JournalLevel MinimumLevel { get; set; } = JournalLevel.Debug;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the journal for this process and writes the header. Idempotent, never throws.
    /// Call this as the very first thing in <c>App.OnStartup</c> — before any window exists — so a
    /// crash during startup is still captured.
    /// </summary>
    public static void Start(string appName, string[]? commandLineArgs = null)
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;

            try
            {
                if (string.Equals(Environment.GetEnvironmentVariable("DOODLESHARP_JOURNAL"), "0", StringComparison.Ordinal))
                    return;

                _appName = string.IsNullOrWhiteSpace(appName) ? "DoodleSharp" : appName;

                var levelText = Environment.GetEnvironmentVariable("DOODLESHARP_JOURNAL_LEVEL");
                if (!string.IsNullOrWhiteSpace(levelText) &&
                    Enum.TryParse<JournalLevel>(levelText, ignoreCase: true, out var configured))
                {
                    MinimumLevel = configured;
                }

                var dir = Directory;
                System.IO.Directory.CreateDirectory(dir);

                var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var writeThrough = string.Equals(Environment.GetEnvironmentVariable("DOODLESHARP_JOURNAL_SYNC"), "1", StringComparison.Ordinal);
                var options = writeThrough ? FileOptions.WriteThrough : FileOptions.None;

                // Two processes (two copies of the app) can start inside the same
                // second. CreateNew makes the collision explicit instead of interleaving two sessions
                // into one file, which would make the journal unreadable.
                FileStream? stream = null;
                for (var attempt = 0; attempt < 50 && stream == null; attempt++)
                {
                    var name = attempt == 0 ? $"{stamp}.log" : $"{stamp}-{attempt}.log";
                    try
                    {
                        stream = new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete, 4096, options);
                        SessionId = Path.GetFileNameWithoutExtension(name);
                        FilePath = stream.Name;
                    }
                    catch (IOException)
                    {
                        // Name taken by a concurrent session — try the next suffix.
                    }
                }

                if (stream == null) return;

                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true   // push to the OS on every line; the OS cache survives a process crash
                };
                _enabled = true;

                WriteHeader(commandLineArgs, writeThrough);

                _self = Process.GetCurrentProcess();
                _heartbeat = new Timer(_ => Heartbeat(), null, HeartbeatInterval, HeartbeatInterval);
            }
            catch
            {
                // A journal that cannot start must not stop the app.
                _enabled = false;
                _writer = null;
            }
        }

        // Housekeeping runs outside the lock and off the startup path.
        try
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ReportPreviousSessions();
                PruneOldJournals();
            });
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Records that the process is shutting down normally and closes the file. A journal without
    /// this marker is, by definition, a crashed session — that is how the next launch (and the
    /// <c>crashes.txt</c> index) identifies one.
    /// </summary>
    public static void MarkCleanExit(string reason = "normal shutdown")
    {
        if (!_enabled) return;
        try
        {
            Journal.Write(JournalLevel.Info, "JRNL.EXIT.CLEAN", reason, $"uptime={Uptime.Elapsed.TotalSeconds:F1}s");
            lock (Gate)
            {
                WriteRawLocked($"# SESSION END (clean) at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} after {Uptime.Elapsed.TotalSeconds:F1}s, {_sequence} records");
                _heartbeat?.Dispose();
                _heartbeat = null;
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _enabled = false;
            }
        }
        catch { /* shutting down anyway */ }
    }

    /// <summary>Forces buffered content out. Records are already auto-flushed; this is belt and braces.</summary>
    public static void Flush()
    {
        try { lock (Gate) { _writer?.Flush(); } } catch { }
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one record. <paramref name="siteKey"/> must be unique across the repository — it is
    /// the primary handle for locating this exact line of code from a shared journal file.
    /// </summary>
    public static void Write(
        JournalLevel level,
        string siteKey,
        string? message = null,
        string? data = null,
        Exception? exception = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        if (!_enabled || level < MinimumLevel) return;

        try
        {
            var builder = new StringBuilder(192);
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.Append(" | #").Append(Interlocked.Increment(ref _sequence).ToString("D6", CultureInfo.InvariantCulture));
            builder.Append(" | +").Append(Uptime.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append('s');
            builder.Append(" | T").Append(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture).PadRight(3));
            builder.Append(" | ").Append(LevelText(level));
            builder.Append(" | ").Append(siteKey.PadRight(24));
            builder.Append(" | ").Append(Site(callerFile, callerLine, callerMember).PadRight(34));

            if (!string.IsNullOrEmpty(message))
                builder.Append(" | ").Append(Sanitize(message));
            if (!string.IsNullOrEmpty(data))
                builder.Append(" | ").Append(Sanitize(data));

            lock (Gate)
            {
                WriteRawLocked(builder.ToString());
                if (exception != null)
                    WriteExceptionLocked(exception);
            }
        }
        catch { /* journaling must never throw into the caller */ }
    }

    public static void Trace(string siteKey, string? message = null, string? data = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Trace, siteKey, message, data, null, callerFile, callerLine, callerMember);

    public static void Debug(string siteKey, string? message = null, string? data = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Debug, siteKey, message, data, null, callerFile, callerLine, callerMember);

    public static void Info(string siteKey, string? message = null, string? data = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Info, siteKey, message, data, null, callerFile, callerLine, callerMember);

    public static void Warn(string siteKey, string? message = null, string? data = null, Exception? exception = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Warn, siteKey, message, data, exception, callerFile, callerLine, callerMember);

    public static void Error(string siteKey, string? message = null, Exception? exception = null, string? data = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Error, siteKey, message, data, exception, callerFile, callerLine, callerMember);

    public static void Fatal(string siteKey, string? message = null, Exception? exception = null, string? data = null,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0, [CallerMemberName] string callerMember = "")
        => Write(JournalLevel.Fatal, siteKey, message, data, exception, callerFile, callerLine, callerMember);

    /// <summary>
    /// Opens a timed scope: logs entry immediately and exit with an elapsed time. Use around
    /// anything that could hang or die — the presence of an ENTER with no matching EXIT is what
    /// localises a hard crash.
    /// </summary>
    public static IDisposable Scope(
        string siteKey,
        string? message = null,
        string? data = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
    {
        return new JournalScope(siteKey, message, data, callerFile, callerLine, callerMember);
    }

    /// <summary>
    /// Records a high-frequency event without touching the disk. Counts are summarised by the next
    /// heartbeat. Use for per-frame / per-keystroke paths where a line each would drown the journal;
    /// the surviving signal is "what was the app doing, and how fast" in the seconds before a crash.
    /// </summary>
    public static void Activity(string activityKey)
    {
        if (!_enabled) return;
        try
        {
            lock (Gate)
            {
                ActivityCounts.TryGetValue(activityKey, out var count);
                ActivityCounts[activityKey] = count + 1;
                _lastActivityKey = activityKey;
            }
        }
        catch { }
    }

    // ── State providers ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers a callback that describes some part of the live application state (open project,
    /// active file, canvas contents...). Providers are invoked on <see cref="CaptureState"/> — in
    /// particular from every crash handler — so the journal ends with a picture of what the app was
    /// holding when it died. Providers must be cheap and must not throw.
    /// </summary>
    public static void RegisterStateProvider(string name, Func<string?> provider)
    {
        if (!_enabled) return;
        try
        {
            lock (Gate)
            {
                StateProviders.RemoveAll(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                StateProviders.Add((name, provider));
            }
        }
        catch { }
    }

    /// <summary>Dumps every registered state provider into the journal under a reason banner.</summary>
    public static void CaptureState(string reason)
    {
        if (!_enabled) return;
        try
        {
            (string Name, Func<string?> Provider)[] providers;
            lock (Gate) { providers = StateProviders.ToArray(); }

            lock (Gate)
            {
                WriteRawLocked($"# ---- STATE ({reason}) ----");
                WriteRawLocked($"#   runtime.uptime = {Uptime.Elapsed.TotalSeconds:F1}s");
                foreach (var line in SystemSnapshot.DescribeRuntimeState())
                    WriteRawLocked("#   " + line);
            }

            foreach (var (name, provider) in providers)
            {
                string? text;
                try { text = provider(); }
                catch (Exception ex) { text = $"<provider threw: {ex.GetType().Name}: {ex.Message}>"; }
                if (string.IsNullOrEmpty(text)) continue;

                lock (Gate)
                {
                    WriteRawLocked($"#   [{name}]");
                    foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                        WriteRawLocked("#     " + line);
                }
            }

            lock (Gate) { WriteRawLocked("# ---- END STATE ----"); }
        }
        catch { }
    }

    // ── Helpers exposed to instrumentation call sites ────────────────────────

    /// <summary>
    /// Describes a file the way crash triage needs it: existence, size, last-write time and a short
    /// content hash. The hash is what lets a journal be matched against the source the user still
    /// has on disk — "the file you sent me is not the file that crashed" is otherwise unfalsifiable.
    /// </summary>
    public static string DescribeFile(string? path, string? inMemoryContent = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return "path=<empty>";

            var builder = new StringBuilder();
            builder.Append("path=").Append(path);

            var info = new FileInfo(path);
            if (info.Exists)
            {
                builder.Append(" bytes=").Append(info.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(" mtime=").Append(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                builder.Append(" ro=").Append(info.IsReadOnly ? "1" : "0");
            }
            else
            {
                builder.Append(" exists=0");
            }

            if (inMemoryContent != null)
            {
                builder.Append(" memchars=").Append(inMemoryContent.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(" memlines=").Append(CountLines(inMemoryContent).ToString(CultureInfo.InvariantCulture));
                builder.Append(" sha=").Append(ShortHash(inMemoryContent));
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"path={path} <describe failed: {ex.GetType().Name}>";
        }
    }

    /// <summary>First 8 hex chars of the SHA-256 of a string. Enough to tell two versions apart.</summary>
    public static string ShortHash(string? text)
    {
        if (text == null) return "-";
        try
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
        }
        catch { return "-"; }
    }

    /// <summary>
    /// Writes a block of text (typically the user's source at crash time) into the journal, capped so
    /// a runaway file cannot fill the disk.
    /// </summary>
    public static void WriteBlock(string title, string? content, int maxChars = 200_000)
    {
        if (!_enabled || string.IsNullOrEmpty(content)) return;
        try
        {
            var text = content.Length > maxChars
                ? content[..maxChars] + $"\n<truncated, {content.Length - maxChars} more chars>"
                : content;

            lock (Gate)
            {
                WriteRawLocked($"# ---- BEGIN {title} (sha={ShortHash(content)}, chars={content.Length}) ----");
                var lineNumber = 0;
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                    WriteRawLocked($"#{++lineNumber,5}| {line}");
                WriteRawLocked($"# ---- END {title} ----");
            }
        }
        catch { }
    }

    /// <summary>Opens the journal folder in Explorer. Used by the Help menu.</summary>
    public static void OpenFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            Process.Start(new ProcessStartInfo { FileName = Directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Error("JRNL.FOLDER.OPEN_FAIL", "Could not open the journal folder", ex);
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static string ResolveDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("DOODLESHARP_JOURNAL_DIR");
        return !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Path.GetTempPath(), FolderName);
    }

    /// <summary>Test hook: forget the cached folder so <c>DOODLESHARP_JOURNAL_DIR</c> is re-read.</summary>
    internal static void ResetDirectoryCache() => _directory = null;

    private static void WriteHeader(string[]? args, bool writeThrough)
    {
        WriteRawLocked("# ============================================================================");
        WriteRawLocked($"# DoodleSharp diagnostic journal — {_appName}");
        WriteRawLocked($"# session      = {SessionId}");
        WriteRawLocked($"# started      = {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} (UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss})");
        WriteRawLocked($"# level        = {MinimumLevel}  writeThrough = {writeThrough}");
        WriteRawLocked($"# args         = {(args is { Length: > 0 } ? string.Join(" ", args) : "<none>")}");
        WriteRawLocked("# ----------------------------------------------------------------------------");
        foreach (var line in SystemSnapshot.DescribeEnvironment())
            WriteRawLocked("# " + line);
        WriteRawLocked("# ----------------------------------------------------------------------------");
        foreach (var line in SystemSnapshot.DescribeLoadedAssemblies())
            WriteRawLocked("# " + line);
        WriteRawLocked("# ============================================================================");
        WriteRawLocked("# timestamp               | seq     | uptime    | thr  | level | site key                 | source location                    | message | data");
        WriteRawLocked("# ----------------------------------------------------------------------------");
    }

    private static void WriteRawLocked(string line)
    {
        var writer = _writer;
        if (writer == null) return;

        if (_bytesWritten > DefaultMaxFileBytes)
        {
            if (_truncated) return;
            _truncated = true;
            writer.WriteLine($"# !! JOURNAL CAP REACHED ({DefaultMaxFileBytes / (1024 * 1024)} MB) — further records dropped.");
            return;
        }

        writer.WriteLine(line);
        _bytesWritten += line.Length + 2;
    }

    private static void WriteExceptionLocked(Exception ex)
    {
        WriteRawLocked($"    !! {ex.GetType().FullName}: {ex.Message}");
        WriteRawLocked($"    !! hresult=0x{ex.HResult:X8} source={ex.Source ?? "-"} site={ex.TargetSite?.ToString() ?? "-"}");

        try
        {
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
                WriteRawLocked($"    !! data[{entry.Key}] = {entry.Value}");
        }
        catch { }

        // ToString() already includes inner exceptions and their stacks, which is exactly the
        // chain needed to walk back from the symptom to the throwing line.
        foreach (var line in (ex.ToString() ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            WriteRawLocked("    !! " + line.TrimEnd());
    }

    private static void Heartbeat()
    {
        if (!_enabled) return;
        try
        {
            string activitySummary;
            string lastActivity;
            lock (Gate)
            {
                activitySummary = ActivityCounts.Count == 0
                    ? "-"
                    : string.Join(" ", ActivityCounts.Select(kv => $"{kv.Key}={kv.Value}"));
                lastActivity = _lastActivityKey;
                ActivityCounts.Clear();
            }

            var stats = SystemSnapshot.DescribeProcessCounters(_self);
            var workingSet = Environment.WorkingSet;
            var delta = workingSet - _lastHeartbeatWorkingSet;
            _lastHeartbeatWorkingSet = workingSet;

            Journal.Write(JournalLevel.Debug, "JRNL.HEARTBEAT", $"last={lastActivity}",
                $"{stats} ws.delta={delta / 1024}KB activity[{activitySummary}]");
        }
        catch { }
    }

    /// <summary>
    /// On startup, looks for journals from previous sessions that never wrote a clean-exit marker and
    /// records them here (plus in a running <c>crashes.txt</c> index). This is what turns "the app
    /// crashed some time last week" into a specific file name to send.
    /// </summary>
    private static void ReportPreviousSessions()
    {
        try
        {
            var files = new DirectoryInfo(Directory)
                .GetFiles("*.log")
                .Where(f => !string.Equals(f.FullName, FilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(10)
                .ToList();

            var crashed = new List<FileInfo>();
            foreach (var file in files)
            {
                if (!HasCleanExitMarker(file.FullName))
                    crashed.Add(file);
            }

            if (crashed.Count == 0)
            {
                Journal.Info("JRNL.PREV.CLEAN", "No abnormal terminations among recent sessions", $"checked={files.Count}");
                return;
            }

            Journal.Warn("JRNL.PREV.CRASHED",
                $"{crashed.Count} recent session(s) ended without a clean-exit marker",
                string.Join(", ", crashed.Select(f => $"{f.Name}@{f.LastWriteTime:yyyy-MM-dd HH:mm}")));

            AppendCrashIndex(crashed);
        }
        catch (Exception ex)
        {
            Journal.Warn("JRNL.PREV.SCAN_FAIL", "Could not scan previous journals", null, ex);
        }
    }

    private static bool HasCleanExitMarker(string path)
    {
        try
        {
            // The marker is the last line; read the tail rather than the whole (possibly large) file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var tailSize = (int)Math.Min(2048, length);
            stream.Seek(length - tailSize, SeekOrigin.Begin);
            var buffer = new byte[tailSize];
            var read = stream.Read(buffer, 0, tailSize);
            return Encoding.UTF8.GetString(buffer, 0, read).Contains("# SESSION END (clean)", StringComparison.Ordinal);
        }
        catch
        {
            // A file we cannot read (still locked by a live session) is not evidence of a crash.
            return true;
        }
    }

    private static void AppendCrashIndex(List<FileInfo> crashed)
    {
        try
        {
            var indexPath = Path.Combine(Directory, "crashes.txt");
            var known = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;
            var builder = new StringBuilder();
            foreach (var file in crashed)
            {
                if (known.Contains(file.Name, StringComparison.OrdinalIgnoreCase)) continue;
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                       .Append("  detected abnormal end: ").Append(file.Name)
                       .Append("  (last write ").Append(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                       .Append(", ").Append(file.Length).AppendLine(" bytes)");
            }
            if (builder.Length > 0)
                File.AppendAllText(indexPath, builder.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private static void PruneOldJournals()
    {
        try
        {
            var files = new DirectoryInfo(Directory)
                .GetFiles("*.log")
                .Where(f => !string.Equals(f.FullName, FilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - RetentionAge;
            var removed = 0;

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var tooOld = file.LastWriteTimeUtc < cutoff;
                var tooMany = i >= MaxRetainedFiles;
                if (!tooOld && !tooMany) continue;

                try { file.Delete(); removed++; }
                catch { /* in use by another session */ }
            }

            if (removed > 0)
                Journal.Debug("JRNL.RETENTION.PRUNE", $"Deleted {removed} old journal(s)",
                    $"kept={files.Count - removed} maxAge={RetentionAge.TotalDays}d maxFiles={MaxRetainedFiles}");
        }
        catch (Exception ex)
        {
            Journal.Warn("JRNL.RETENTION.FAIL", "Journal pruning failed", null, ex);
        }
    }

    private static string LevelText(JournalLevel level) => level switch
    {
        JournalLevel.Trace => "TRACE",
        JournalLevel.Debug => "DEBUG",
        JournalLevel.Info => "INFO ",
        JournalLevel.Warn => "WARN ",
        JournalLevel.Error => "ERROR",
        _ => "FATAL"
    };

    private static string Site(string file, int line, string member)
    {
        var name = string.IsNullOrEmpty(file) ? "?" : Path.GetFileName(file);
        return $"{name}:{line} {member}";
    }

    /// <summary>Keeps every record on exactly one line so the file stays greppable.</summary>
    private static string Sanitize(string value)
        => value.Replace("\r\n", " / ").Replace('\n', '/').Replace('\r', '/');

    private static int CountLines(string text)
    {
        var count = 1;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }

    private sealed class JournalScope : IDisposable
    {
        private readonly string _siteKey;
        private readonly string _callerFile;
        private readonly int _callerLine;
        private readonly string _callerMember;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        internal JournalScope(string siteKey, string? message, string? data,
            string callerFile, int callerLine, string callerMember)
        {
            _siteKey = siteKey;
            _callerFile = callerFile;
            _callerLine = callerLine;
            _callerMember = callerMember;
            _stopwatch = Stopwatch.StartNew();

            Write(JournalLevel.Debug, siteKey, message == null ? "ENTER" : "ENTER " + message, data,
                null, callerFile, callerLine, callerMember);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopwatch.Stop();

            // An EXIT that never appears is the signal that matters: it means the process died inside
            // the scope. When the scope does close, an unusually long elapsed time is the next best
            // clue (a hang or a pathological input), so anything over 2 s is promoted to a warning.
            var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
            Write(elapsed > 2000 ? JournalLevel.Warn : JournalLevel.Debug, _siteKey,
                $"EXIT ({elapsed:F1} ms)", null, null, _callerFile, _callerLine, _callerMember);
        }
    }
}
