using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DoodleSharp.Mcp;

/// <summary>
/// The tools an agent sees.
///
/// <para>
/// Deliberately <b>not</b> a file-editing surface. Claude Code already reads and writes the
/// project's <c>.cs</c> files perfectly well; what it cannot do from the filesystem is run them and
/// find out what happened. So these tools expose only what the app knows — and
/// <c>doodle_run_project</c> re-reads from disk first, which is what joins the two halves together.
/// </para>
///
/// <para>
/// Every tool returns prose rather than JSON. The caller is a language model: a sentence saying
/// which line failed to compile is more use to it than a nested object it has to narrate anyway.
/// Failures come back as text too, not as exceptions — a tool that throws tells the agent only that
/// something went wrong.
/// </para>
/// </summary>
[McpServerToolType]
public static class DoodleSharpTools
{
    [McpServerTool(Name = "doodle_get_status")]
    [Description("Reports which DoodleSharp project is currently open, how many files and shapes " +
                 "it has, and whether it has unsaved edits. Use this first to confirm the running " +
                 "window holds the project you mean before running anything.")]
    public static async Task<string> GetStatus(CancellationToken ct)
    {
        try
        {
            var status = await BridgeClient.CallAsync<StatusPayload>(
                McpBridgeProtocol.CmdGetStatus, ct: ct).ConfigureAwait(false);

            if (status.ProjectPath == null)
                return $"DoodleSharp {status.AppVersion} is running, but no project is open.";

            var unsaved = status.HasUnsavedChanges
                ? " There are unsaved edits in the editor — those files will NOT be replaced by what is on disk when you run."
                : string.Empty;

            return $"DoodleSharp {status.AppVersion} — project '{status.ProjectName}' " +
                   $"({status.FileCount} file{(status.FileCount == 1 ? "" : "s")}) at {status.ProjectPath}. " +
                   $"{status.ShapeCount} shape{(status.ShapeCount == 1 ? "" : "s")} on the canvas.{unsaved}";
        }
        catch (BridgeException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "doodle_run_project")]
    [Description("Re-reads the open project's files from disk, compiles and runs it, and reports " +
                 "compiler diagnostics, any runtime error, the program's console output, and how " +
                 "many shapes were drawn. Call this after editing the project's .cs files to see " +
                 "whether the change works.")]
    public static async Task<string> RunProject(CancellationToken ct)
    {
        try
        {
            var run = await BridgeClient.CallAsync<RunPayload>(
                McpBridgeProtocol.CmdRunProject, ct: ct).ConfigureAwait(false);

            return FormatRun(run);
        }
        catch (BridgeException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "doodle_capture_canvas")]
    [Description("Renders the DoodleSharp canvas to a PNG and returns it as an image, so you can " +
                 "see what the code actually drew rather than only whether it compiled. Fits the " +
                 "whole drawing in frame by default. Call this after doodle_run_project when the " +
                 "result is meant to look like something.")]
    public static async Task<CallToolResult> CaptureCanvas(
        [Description("Longest width of the returned image in pixels (default 1024). The canvas is " +
                     "scaled down to fit, never up.")]
        int maxWidth = 1024,
        [Description("Longest height of the returned image in pixels (default 1024).")]
        int maxHeight = 1024,
        [Description("Draw the reference grid behind the shapes (default false).")]
        bool includeGrid = false,
        [Description("Fit the whole drawing in frame before capturing (default true). Set false to " +
                     "photograph the user's current view instead.")]
        bool zoomExtents = true,
        CancellationToken ct = default)
    {
        try
        {
            var args = new CaptureArgs
            {
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                IncludeGrid = includeGrid,
                ZoomExtents = zoomExtents,
            };

            var shot = await BridgeClient.CallAsync<CapturePayload>(
                McpBridgeProtocol.CmdCaptureCanvas, args, ct).ConfigureAwait(false);

            // An empty canvas is the single most confusing thing to hand back as a picture, because
            // a blank image looks identical to a rendering failure. Say which it is in words.
            var caption = shot.ShapeCount == 0
                ? $"The canvas is empty — no shapes are on it. ({shot.Width}x{shot.Height} capture.)"
                : $"DoodleSharp canvas, {shot.ShapeCount} shape{(shot.ShapeCount == 1 ? "" : "s")} " +
                  $"({shot.Width}x{shot.Height}).";

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = caption },
                    // Data is a ReadOnlyMemory<byte> holding the ALREADY-BASE64 text as UTF-8, not
                    // the image bytes. Handing it the raw PNG compiles and then silently ships a
                    // corrupt picture: the serializer decodes whatever is there as UTF-8 into a
                    // JSON string, and every byte that is not valid UTF-8 — starting with the PNG
                    // magic 0x89 — becomes U+FFFD. Verified by reading the bytes back off the wire.
                    new ImageContentBlock
                    {
                        Data = Encoding.UTF8.GetBytes(shot.PngBase64),
                        MimeType = "image/png",
                    },
                ],
            };
        }
        catch (BridgeException ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = ex.Message }],
            };
        }
    }

    private static string FormatRun(RunPayload run)
    {
        var sb = new StringBuilder();

        sb.AppendLine(run.Success
            ? $"Run succeeded — {run.ShapeCount} shape{(run.ShapeCount == 1 ? "" : "s")} drawn."
            : "Run FAILED.");

        // The conflict case is why this is reported at all: a file the user is part-way through
        // editing is kept as they left it, so the run the agent is reading did not include its edit.
        if (!string.IsNullOrEmpty(run.Refresh))
            sb.AppendLine($"Files from disk: {run.Refresh}.");

        var errors = run.Diagnostics.Where(d => d.Severity == "Error").ToList();
        var warnings = run.Diagnostics.Where(d => d.Severity == "Warning").ToList();

        // Error already contains the compiler's own rendering of the same diagnostics that are
        // about to be listed one per line. Print it only when it is saying something they are not
        // — which is exactly the runtime-failure case, where there are no diagnostics at all.
        if (!string.IsNullOrEmpty(run.Error) && errors.Count == 0)
            sb.AppendLine($"Error: {run.Error}");

        foreach (var group in new[] { errors, warnings })
        {
            if (group.Count == 0) continue;

            sb.AppendLine();
            sb.AppendLine($"{group[0].Severity}s ({group.Count}):");
            foreach (var d in group.Take(25))
            {
                // Line only, never the column. These diagnostics come from the execute-path
                // compilation, which injects a stack guard at the top of every method body
                // (note 21). The injection carries no trivia, so line numbers are faithful — but
                // it is ~86 characters wide, and every column on the line it was inserted into is
                // off by that much. A column that points confidently at the wrong character is
                // worse than no column at all.
                var where = d.File == null ? $"line {d.Line}" : $"{Path.GetFileName(d.File)}:{d.Line}";
                sb.AppendLine($"  {where}  {d.Id}: {d.Message}");
            }
            if (group.Count > 25)
                sb.AppendLine($"  ... and {group.Count - 25} more");
        }

        if (run.Console.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Console output:");
            foreach (var line in run.Console.Take(200))
                sb.AppendLine($"  {line}");
            if (run.Console.Count > 200)
                sb.AppendLine($"  ... and {run.Console.Count - 200} more lines");
        }

        return sb.ToString().TrimEnd();
    }
}
