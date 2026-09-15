using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoodleSharp.Canvas;
using DoodleSharp.Diagnostics;
using DoodleSharp.Mcp;
using Microsoft.CodeAnalysis;

namespace DoodleSharp;

/// <summary>
/// The window's half of the MCP surface: the commands <see cref="McpPipeServer"/> exposes to an
/// agent, and the listener's lifetime.
///
/// <para>
/// Kept in its own partial rather than added to <c>MainWindow.xaml.cs</c>, which is long enough
/// already, and because everything here shares one unusual contract: <b>these methods are called
/// from a pipe, not from a click</b>. They run on the UI thread (the server marshals), they must
/// not show a dialog — there is no one to answer it — and they have to report what happened as
/// data rather than as a status-bar string.
/// </para>
/// </summary>
public partial class MainWindow
{
    private McpPipeServer? _mcpServer;

    /// <summary>
    /// Starts the MCP listener. Called once the window is up, because every command it can serve
    /// needs a live canvas to act on.
    /// </summary>
    private void StartMcpServer()
    {
        try
        {
            var handlers = new Dictionary<string, Func<JsonElement?, Task<object?>>>(StringComparer.Ordinal)
            {
                [McpBridgeProtocol.CmdGetStatus] = _ => Task.FromResult<object?>(McpGetStatus()),
                [McpBridgeProtocol.CmdRunProject] = async _ => await McpRunProjectAsync().ConfigureAwait(true),
                [McpBridgeProtocol.CmdCaptureCanvas] = args => Task.FromResult<object?>(McpCaptureCanvas(ReadCaptureArgs(args))),
            };

            _mcpServer = new McpPipeServer(Dispatcher, handlers);
            _mcpServer.Start();
        }
        catch (Exception ex)
        {
            // Never fatal: the app is fully usable without an agent attached to it.
            Journal.Error("MCP.WIRE.FAILED", "MCP server could not be wired up", ex);
        }
    }

    private void StopMcpServer()
    {
        try
        {
            _mcpServer?.Dispose();
            _mcpServer = null;
        }
        catch (Exception ex)
        {
            Journal.Warn("MCP.UNWIRE.FAILED", "MCP server did not shut down cleanly", null, ex);
        }
    }

    /// <summary>
    /// What is currently open. Cheap, and the command an agent should lead with — it is the only
    /// way to find out whether the window it is talking to holds the project it means.
    /// </summary>
    internal StatusPayload McpGetStatus()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        return new StatusPayload
        {
            ProjectName = _currentProject?.ProjectFile.Name,
            ProjectPath = _currentProject?.ProjectFilePath,
            FileCount = _currentProject?.Files.Count ?? 0,
            ShapeCount = CanvasRenderer.Instance.GetShapes().Count,
            HasUnsavedChanges = _currentProject?.Files.Any(f => f.HasUnsavedChanges) ?? false,
            AppVersion = version,
        };
    }

    /// <summary>
    /// Re-reads the project from disk, runs it, and reports the outcome.
    ///
    /// <para>
    /// The refresh is the point of the command, not a nicety: an agent edits the <c>.cs</c> files
    /// with its own file tools, so without it this would compile whatever the editor was last told
    /// about and report a result for code that no longer exists. <c>RefreshProjectFromDisk</c>
    /// already does the right thing with files the user is part-way through editing — it keeps
    /// unsaved buffers and lists them as conflicts (note 63) — so those surface here as a warning
    /// rather than being silently overwritten.
    /// </para>
    /// </summary>
    internal async Task<RunPayload> McpRunProjectAsync()
    {
        using var scope = Journal.Scope("MCP.RUN", "Run requested over MCP",
            $"project={_currentProject?.ProjectFile.Name ?? "<none>"}");

        var payload = new RunPayload();

        if (_currentProject == null || _currentProject.Files.Count == 0)
        {
            payload.Error = "No project is open. Open one in DoodleSharp first.";
            return payload;
        }

        // Pick up whatever the agent just wrote, and say so — a run against stale text that quietly
        // succeeded would be the worst possible answer here.
        //
        // It has to be this method and not VizCodeProject.RefreshFilesFromDisk directly, even though
        // the latter is what returns the result: the project-level call updates the file model, but
        // only the window pushes the new text into the open editor. Skip that and the run still uses
        // the stale buffer, because RunSilentlyAsync starts by saving the editor over the file it
        // just re-read. Calling both in sequence is worse than calling either — the second refresh
        // correctly reports nothing to do, so the editor is never updated at all.
        var refresh = RefreshProjectFromDisk();
        payload.Refresh = refresh == null ? null : DescribeRefresh(refresh);

        if (_currentProject.EntryPointFile == null)
        {
            payload.Error = "No entry point: the project has no StartViz.cs.";
            return payload;
        }

        // The console is the running program's output, so it has to be read after the run and
        // scoped to it. ConsoleOutput.GetEntries answers for the running program (note 136), which
        // is the half an agent wants; the panel's own display list is not.
        var result = await RunSilentlyAsync("MCP").ConfigureAwait(true);

        if (result == null)
        {
            payload.Error = "The run did not start — the project has no files or no entry point.";
            return payload;
        }

        payload.Success = result.Success;
        payload.Error = result.Error;
        payload.ShapeCount = CanvasRenderer.Instance.GetShapes().Count;

        if (result.Diagnostics != null)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error &&
                    diagnostic.Severity != DiagnosticSeverity.Warning)
                    continue;

                var span = diagnostic.Location.GetLineSpan();
                payload.Diagnostics.Add(new DiagnosticPayload
                {
                    Severity = diagnostic.Severity.ToString(),
                    Id = diagnostic.Id,
                    Message = diagnostic.GetMessage(),
                    File = string.IsNullOrEmpty(span.Path) ? null : span.Path,
                    Line = span.StartLinePosition.Line + 1,
                    Column = span.StartLinePosition.Character + 1,
                });
            }
        }

        foreach (var entry in Console.ConsoleOutput.Instance.GetEntries())
        {
            var prefix = entry.IsError ? "error: " : string.Empty;
            payload.Console.Add($"{prefix}{entry.Message}");
        }

        return payload;
    }

    /// <summary>
    /// Arguments for a capture, with every field optional — an agent calling
    /// <c>doodle_capture_canvas</c> with no arguments is the common case and must get sensible
    /// defaults rather than a 0×0 image.
    /// </summary>
    private static CaptureArgs ReadCaptureArgs(JsonElement? args)
    {
        var parsed = new CaptureArgs();
        if (args is not { ValueKind: JsonValueKind.Object } obj) return parsed;

        if (obj.TryGetProperty("maxWidth", out var w) && w.TryGetInt32(out var wv) && wv > 0)
            parsed.MaxWidth = Math.Clamp(wv, 64, 4096);
        if (obj.TryGetProperty("maxHeight", out var h) && h.TryGetInt32(out var hv) && hv > 0)
            parsed.MaxHeight = Math.Clamp(hv, 64, 4096);
        if (obj.TryGetProperty("includeGrid", out var g) && g.ValueKind is JsonValueKind.True or JsonValueKind.False)
            parsed.IncludeGrid = g.GetBoolean();
        if (obj.TryGetProperty("zoomExtents", out var z) && z.ValueKind is JsonValueKind.True or JsonValueKind.False)
            parsed.ZoomExtents = z.GetBoolean();

        return parsed;
    }

    /// <summary>
    /// Renders the canvas to a PNG the agent can look at. This is the tool that makes the rest
    /// worth having: an agent that can only read diagnostics knows whether its code <i>compiled</i>,
    /// not whether it drew the right thing.
    ///
    /// <para>
    /// <b>The overlay must be suppressed</b> (note 93). It is a visual child of the canvas, so
    /// without the scope the F10 frame-timing readout, selection handles, the rubber band and snap
    /// markers are all rendered into the image — and an agent has no way to know that the handles
    /// it can see are not part of the drawing.
    /// </para>
    /// </summary>
    internal CapturePayload McpCaptureCanvas(CaptureArgs args)
    {
        EnsureCanvasReadyForCapture();

        var wasGridShown = ViewportHost.ShowGrid;
        using var overlayOff = ViewportHost.SuppressOverlayForCapture();

        try
        {
            ViewportHost.ShowGrid = args.IncludeGrid;

            if (args.ZoomExtents)
            {
                ViewportHost.ForEach(c =>
                    c.ZoomExtents(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));
            }

            ViewportHost.UpdateLayout();

            var canvasWidth = (int)ViewportHost.ActualWidth;
            var canvasHeight = (int)ViewportHost.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                throw new InvalidOperationException($"The canvas has no size to capture ({canvasWidth}x{canvasHeight}).");

            var rtb = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(ViewportHost);

            // Scale to fit, never up.
            BitmapSource image = rtb;
            var scale = Math.Min(1.0, Math.Min(
                (double)args.MaxWidth / canvasWidth,
                (double)args.MaxHeight / canvasHeight));

            if (scale < 1.0)
                image = new TransformedBitmap(rtb, new ScaleTransform(scale, scale));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);

            return new CapturePayload
            {
                PngBase64 = Convert.ToBase64String(buffer.ToArray()),
                Width = image.PixelWidth,
                Height = image.PixelHeight,
                ShapeCount = CanvasRenderer.Instance.GetShapes().Count,
            };
        }
        finally
        {
            ViewportHost.ShowGrid = wasGridShown;
            ViewportHost.UpdateLayout();
        }
    }

    /// <summary>
    /// One line saying what the disk refresh did, or null when it did nothing. Conflicts are named
    /// individually because they are the one outcome the agent has to act on: those files were
    /// <i>not</i> taken from disk, so the run it is about to read the result of did not include its
    /// edit.
    /// </summary>
    private static string? DescribeRefresh(Project.VizCodeProject.DiskRefreshResult refresh)
    {
        if (!refresh.AnythingChanged && refresh.Unreadable.Count == 0) return null;

        var parts = new List<string>();
        if (refresh.Reloaded.Count > 0) parts.Add($"{refresh.Reloaded.Count} reloaded");
        if (refresh.Added.Count > 0) parts.Add($"{refresh.Added.Count} added");
        if (refresh.Removed.Count > 0) parts.Add($"{refresh.Removed.Count} removed");

        if (refresh.Conflicted.Count > 0)
        {
            var names = string.Join(", ", refresh.Conflicted.Select(f => f.FileName));
            parts.Add($"KEPT the editor's unsaved version of {names} — your change to those files was NOT run");
        }

        if (refresh.Unreadable.Count > 0)
            parts.Add($"{refresh.Unreadable.Count} unreadable");

        return string.Join("; ", parts);
    }
}
