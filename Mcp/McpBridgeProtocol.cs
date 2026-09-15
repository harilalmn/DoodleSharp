using System.Collections.Generic;

namespace DoodleSharp.Mcp;

/// <summary>
/// The wire format spoken between the running app (<see cref="McpPipeServer"/>) and the
/// <c>DoodleSharp.Mcp</c> bridge process that Claude Code launches over stdio.
///
/// <para>
/// <b>This file is compiled into both assemblies</b> — the bridge project links it rather than
/// referencing DoodleSharp, because DoodleSharp is a <c>net9.0-windows</c> <c>WinExe</c> and the
/// bridge is a plain console app that must not drag WPF in. One definition, two compilations, so
/// the two ends cannot drift apart. Keep it free of anything that isn't in the base framework.
/// </para>
///
/// <para>
/// Framing is newline-delimited JSON over a named pipe, which is what the MCP specification itself
/// recommends for byte-stream transports: it is exactly the stdio binding's framing, minus the
/// process-lifecycle rules that only make sense for a spawned child.
/// </para>
/// </summary>
internal static class McpBridgeProtocol
{
    /// <summary>
    /// The pipe the app listens on and the bridge dials.
    ///
    /// <para>
    /// A fixed name, so the bridge needs no discovery step and no port file. The consequence is
    /// that <b>the first DoodleSharp window to open owns it</b>; a second instance finds the name
    /// taken, logs that it did, and simply does not serve. That is the honest behaviour for a
    /// single-canvas tool — an agent asking "run the project" must not be answered by whichever of
    /// three windows happened to win a race.
    /// </para>
    /// </summary>
    public const string DefaultPipeName = "DoodleSharp.mcp";

    /// <summary>
    /// How long the bridge waits for the app to answer one command. Generous, because the command
    /// it is usually waiting on is a Roslyn compile plus the user's <c>Main()</c>.
    /// </summary>
    public const int DefaultCommandTimeoutMs = 120_000;

    public const string CmdGetStatus = "get_status";
    public const string CmdRunProject = "run_project";
    public const string CmdCaptureCanvas = "capture_canvas";
}

/// <summary>One command, bridge → app.</summary>
internal sealed class BridgeRequest
{
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;

    /// <summary>Raw JSON object of arguments, or null. Phase-1 commands take none.</summary>
    public string? ArgsJson { get; set; }
}

/// <summary>One reply, app → bridge. <see cref="ResultJson"/> is the payload for the command.</summary>
internal sealed class BridgeResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
}

/// <summary>Payload for <see cref="McpBridgeProtocol.CmdGetStatus"/>.</summary>
internal sealed class StatusPayload
{
    public string? ProjectName { get; set; }
    public string? ProjectPath { get; set; }
    public int FileCount { get; set; }
    public int ShapeCount { get; set; }
    public bool HasUnsavedChanges { get; set; }
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>Payload for <see cref="McpBridgeProtocol.CmdRunProject"/>.</summary>
internal sealed class RunPayload
{
    public bool Success { get; set; }
    public int ShapeCount { get; set; }

    /// <summary>What re-reading the project from disk actually did, or null if it did nothing.</summary>
    public string? Refresh { get; set; }

    /// <summary>Runtime or compiler error text, when the run did not succeed.</summary>
    public string? Error { get; set; }

    public List<DiagnosticPayload> Diagnostics { get; set; } = new();

    /// <summary>The running program's console output, already formatted one entry per line.</summary>
    public List<string> Console { get; set; } = new();
}

/// <summary>Arguments for <see cref="McpBridgeProtocol.CmdCaptureCanvas"/>.</summary>
internal sealed class CaptureArgs
{
    /// <summary>
    /// Longest edge of the returned image. The capture is taken at the canvas's real size and
    /// scaled down to fit, never up — a drawing photographed larger than it was rendered shows no
    /// more detail and costs the caller image tokens for the privilege.
    /// </summary>
    public int MaxWidth { get; set; } = 1024;

    public int MaxHeight { get; set; } = 1024;

    /// <summary>Whether the reference grid is drawn. Off by default: it is chrome, not geometry.</summary>
    public bool IncludeGrid { get; set; }

    /// <summary>
    /// Fit the whole drawing in the frame before capturing. On by default, because the alternative
    /// is photographing whatever region the viewport happened to be showing and letting the caller
    /// conclude from an empty picture that its code drew nothing.
    /// </summary>
    public bool ZoomExtents { get; set; } = true;
}

/// <summary>Payload for <see cref="McpBridgeProtocol.CmdCaptureCanvas"/>.</summary>
internal sealed class CapturePayload
{
    /// <summary>The PNG, base64-encoded.</summary>
    public string PngBase64 { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
    public int ShapeCount { get; set; }
}

internal sealed class DiagnosticPayload
{
    public string Severity { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? File { get; set; }

    /// <summary>
    /// 1-based line. Faithful to the user's file: the execute path's stack-guard injection is
    /// deliberately trivia-free so that line numbers survive it (note 21).
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// 1-based column, <b>not to be trusted and not reported to the agent</b>. The same injection
    /// that preserves line numbers shifts every column on the line it was inserted into by the
    /// ~86 characters it occupies. Carried here because it is what Roslyn said, not because a
    /// caller should use it.
    /// </summary>
    public int Column { get; set; }
}
