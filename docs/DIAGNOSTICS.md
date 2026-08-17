# Diagnostic Journals

DoodleSharp writes a detailed journal of everything it does to
**`%TEMP%\C2V`** (typically `C:\Users\<you>\AppData\Local\Temp\C2V`). The journal exists so that a
crash on any machine can be diagnosed from a single file the user sends back.

- One file per application run, named with the start time: **`YYYYMMDDhhmmss.log`**.
- Writing starts before the first window appears and continues until the process ends.
- Every record is flushed as it is written, so **the last line in the file is the last thing the app
  did before it died** — including for failures no handler can catch.

Fastest route to the folder: **Help > Open Diagnostic Journals** (this also dumps a fresh state
snapshot first). **Help > Copy Current Journal Path** puts this session's file path on the clipboard.

## Which file do I send?

Open `%TEMP%\C2V\crashes.txt`. Each launch appends a line for any earlier session that ended
abnormally, naming the file:

```
2026-08-13 10:03:59  detected abnormal end: 20260813100317.log  (last write 2026-08-13 10:03:47, 14509 bytes)
```

Send the named `.log`. If in doubt, send the newest few files — journals older than 30 days (or
beyond the most recent 60) are pruned automatically, and a busy session is a few hundred KB.

## Reading a journal

### Header

The header records everything about the machine that a bug report normally forgets: app version and
build hash, exact OS and .NET build, CPU, RAM, free disk, locale, elevation, screen geometry, **GPU
model and display-driver version/date**, WPF render tier, and every loaded assembly with its version
and path.

```
# DoodleSharp diagnostic journal — DoodleSharp
# session      = 20260813100317
# started      = 2026-08-13 10:03:17.314 +05:30 (UTC 2026-08-13 04:33:17)
# app.version = 2026.8.1.0
# os.description = Microsoft Windows 10.0.26200
# clr.framework = .NET 9.0.18
# gpu[0].name = Intel(R) UHD Graphics
# gpu[0].driver = 32.0.101.5542 (6-8-2024)
```

### Records

```
2026-08-13 10:03:17.587 | #000005 | +0.285s | T2   | INFO  | APP.STARTUP | App.xaml.cs:21 OnStartup | Application starting | args=0
└─ wall clock        │ sequence │ uptime │ thread │ level │ site key │ source location │ message │ data
```

| Field | Why it is there |
|---|---|
| **timestamp** | Correlates with when the user says it crashed |
| **sequence** | Monotonic; a gap means records were dropped, so the timeline can be trusted |
| **uptime** | Seconds since process start — easier to reason about than wall clock |
| **thread** | Managed thread id; separates UI-thread work from background work |
| **level** | `TRACE DEBUG INFO WARN ERROR FATAL` |
| **site key** | Repo-unique identifier of the emitting line (see below) |
| **source location** | `File.cs:line Member`, captured by the compiler — always accurate |

Exceptions follow their record as `!!`-prefixed lines carrying the type, message, HResult, `Data`
entries and the **full inner-exception chain with stacks**.

### Site keys

A site key such as `EXEC.MAIN.INVOKE` names one specific line of code. Keys are hierarchical
(`AREA.SUBSYSTEM.EVENT`) and **must be unique across the repository** — `Tests/JournalSiteKeyTests.cs`
fails the build if two call sites share a key or a key breaks the format. That uniqueness is what
makes a key in a shared journal resolve unambiguously back to a location: `grep -rn '"EXEC.MAIN.INVOKE"'`.

Current areas:

| Prefix | Covers |
|---|---|
| `APP.*` | Application startup and lifecycle |
| `WELCOME.*` | Welcome window: new project, open project, recent list |
| `PROJ.*` | Project and file I/O — load, open, save, discover, vanish |
| `MW.*` | Main window — construction, subsystem init, file selection, run, close |
| `EXEC.*` | Compilation and execution of user code |
| `CANVAS.*` | Rendering |
| `CRASH.*` | Unhandled exceptions |
| `DIAG.*` | The diagnostics layer itself — install, assembly loads, WPF probes, UI hangs |
| `JRNL.*` | The journal's own lifecycle — heartbeat, retention, previous-session scan |

### Scopes

Risky operations are bracketed by `ENTER` / `EXIT (elapsed)` records sharing one key:

```
... | DEBUG | EXEC.RUN | ENTER Compile and execute | project=Demo
... | DEBUG | EXEC.RUN | EXIT (412.7 ms)
```

**An `ENTER` with no matching `EXIT` is the single most valuable signal in the file** — it means the
process died inside that scope. An `EXIT` over 2 seconds is automatically promoted to `WARN`.

### Heartbeat

Every 10 seconds:

```
| DEBUG | JRNL.HEARTBEAT | last=ui.ping | ws=206.2MB priv=151.8MB gcheap=4.3MB threads=23 handles=494 gdi=22 user=25 gc=0/0/0 cpu=2.3s ws.delta=210160KB activity[ui.ping=4 canvas.redraw=180]
```

This is the liveness pulse and the leak detector in one. A climbing `handles`, `gdi` or `user` count
is the classic precursor to a WPF process dying "for no reason" at the 10,000-handle limit, and the
last heartbeat timestamp brackets a hard crash to within ten seconds. High-frequency events
(frames, redraws) are counted rather than logged individually, so they cost nothing per occurrence
but still show up here.

### State dumps

Written on every crash, on shutdown, and on demand from the Help menu. Contains GC/memory
statistics, the open project and every file in it (path, size, mtime, content hash), the active
file, canvas shape count, global parameter values, and **the full text of the editor buffer** — so a
crash can be reproduced against the exact source that caused it.

### Crash records

| Key | Meaning |
|---|---|
| `CRASH.WPF.DISPATCHER` | Exception escaped a UI-thread event handler — the most common WPF crash |
| `CRASH.APPDOMAIN` | Exception on a background thread; `terminating=True` means the process is dying |
| `CRASH.TASK.UNOBSERVED` | A faulted `Task` nobody awaited; often the first symptom of a later crash |
| `CANVAS.DRAW.THREW` | Rendering threw — names the exact shape (id, name, type, bounds, finiteness) |
| `DIAG.UI.HANG` / `DIAG.UI.HANG_END` | UI thread stopped answering for 5 s or more |
| `DIAG.FIRSTCHANCE` | An exception was *thrown* (before any catch), including ones the app swallows |
| `DIAG.WPF.TIER_CHANGED` | WPF's render tier changed mid-run — usually a display-driver reset |

### What cannot be caught

`StackOverflowException`, `AccessViolationException` and `Environment.FailFast` terminate a .NET
process immediately; no handler runs. For these there is no `CRASH.*` record — the evidence is the
shape of the file itself: an unclosed scope, the last records before the cut, and the last
heartbeat. This is precisely why records are flushed synchronously rather than buffered.

(User-code runaway recursion is handled separately — `Execution/StackGuardRewriter.cs` converts it
into a catchable exception before the stack overflows.)

## Configuration

Journaling is on by default and needs no setup. Environment variables, read once at startup:

| Variable | Effect |
|---|---|
| `C2V_JOURNAL=0` | Disable journaling entirely |
| `C2V_JOURNAL_LEVEL=Trace\|Debug\|Info\|Warn\|Error\|Fatal` | Minimum level (default `Debug`) |
| `C2V_JOURNAL_SYNC=1` | Write through to disk instead of the OS cache — survives a BSOD or power loss, at a real cost in speed |
| `C2V_JOURNAL_DIR=<path>` | Write journals somewhere other than `%TEMP%\C2V` |

Housekeeping is automatic: files older than 30 days are deleted, at most 60 are kept, and a single
journal is capped at 64 MB.

## Privacy

Journals stay on the local machine unless the user chooses to send one. They contain the user name,
machine name, file paths, and the source code in the editor at the time of a crash. They do not
collect or transmit anything on their own — there is no network path in this subsystem.

## Adding instrumentation

```csharp
using DoodleSharp.Diagnostics;

Journal.Info("AREA.THING.HAPPENED", "human readable", $"key=value key2={value2}");
Journal.Error("AREA.THING.FAILED", "what was being attempted", ex);

using (Journal.Scope("AREA.RISKY", "what this does"))
{
    // an ENTER with no EXIT points here if the process dies inside
}

Journal.Activity("hot.path");   // counted, not written; summarised by the heartbeat
```

Rules:

1. **Pick a new, unique key.** The test suite enforces it. Format: `UPPER.DOTTED.SEGMENTS`.
2. **Never pass user data through `message` that you would not want in a shared file.** Paths and
   hashes, yes; secrets, no.
3. **Do not journal per-frame** — use `Journal.Activity` instead.
4. Source lives in `Diagnostics/`.
