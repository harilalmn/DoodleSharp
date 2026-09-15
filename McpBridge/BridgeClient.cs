using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DoodleSharp.Mcp;

/// <summary>
/// Raised when the app could not be reached or refused a command. The message is written to be
/// read by an agent: it says what to do about it, because an agent cannot see the screen and has no
/// other way to find out that DoodleSharp simply is not open.
/// </summary>
internal sealed class BridgeException : Exception
{
    public BridgeException(string message) : base(message) { }
}

/// <summary>
/// Dials the running DoodleSharp window and runs one command.
///
/// <para>
/// <b>A connection per command</b>, rather than one held open for the life of the bridge. The cost
/// is a pipe handshake per call, which is nothing beside the Roslyn compile on the other side; what
/// it buys is that the app can be closed and reopened underneath a long-lived Claude session and
/// the next command simply works. A cached connection would have to detect that and reconnect
/// anyway, with a window where it reports a stale failure.
/// </para>
/// </summary>
internal static class BridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// How long to wait for the app's single pipe instance to come free.
    ///
    /// <para>
    /// The app serves one connection at a time on purpose — it has one canvas, and two commands
    /// mutating it at once is not a thing anyone wants. So a second concurrent tool call has to
    /// queue, and <c>ConnectAsync</c> queues for us: it returns as soon as the previous client
    /// disconnects. The wait therefore has to cover a whole command (a Roslyn compile plus the
    /// user's <c>Main()</c>), not a network round trip.
    /// </para>
    /// </summary>
    private const int ConnectTimeoutMs = 90_000;

    /// <summary>
    /// The pipe's path in the filesystem namespace. Windows exposes named pipes here, so this is
    /// how to ask "is DoodleSharp listening at all?" without joining the queue for its attention —
    /// which is the difference between "the app is not running" and "the app is busy", two answers
    /// an agent must not have confused for one another.
    /// </summary>
    private static readonly string PipePath = $@"\\.\pipe\{McpBridgeProtocol.DefaultPipeName}";

    /// <summary>
    /// Whether the app is listening, polled rather than sampled once.
    ///
    /// <para>
    /// The app holds a single pipe instance and creates the next one only after the previous client
    /// disconnects, so there is a brief window between commands where the path genuinely does not
    /// exist. Sampling once inside that window would report a running application as closed. Half a
    /// second of polling covers it and still fails fast when nothing is there.
    /// </para>
    /// </summary>
    private static async Task<bool> PipeExistsAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (File.Exists(PipePath)) return true;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        return File.Exists(PipePath);
    }

    /// <summary>
    /// Sends <paramref name="command"/> and returns its payload deserialized as
    /// <typeparamref name="T"/>. Throws <see cref="BridgeException"/> for anything the agent can
    /// act on — app not running, command refused, malformed reply.
    /// </summary>
    public static async Task<T> CallAsync<T>(string command, object? args = null, CancellationToken ct = default)
    {
        // Distinguish "not running" from "busy" before joining the queue, so the two cannot be
        // reported as the same thing.
        if (!await PipeExistsAsync(ct).ConfigureAwait(false))
        {
            throw new BridgeException(
                "DoodleSharp is not running. These tools drive the open application window, so ask " +
                "the user to start DoodleSharp and open a project, then retry.");
        }

        await using var pipe = new NamedPipeClientStream(
            ".", McpBridgeProtocol.DefaultPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new BridgeException(
                "DoodleSharp is running but stayed busy for " +
                $"{ConnectTimeoutMs / 1000}s — it serves one command at a time, and the one ahead of " +
                "this never finished. The program it is running may be stuck in a loop.");
        }
        catch (IOException ex)
        {
            throw new BridgeException($"Could not connect to DoodleSharp: {ex.Message}");
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);

        var request = new BridgeRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Command = command,
            ArgsJson = args == null ? null : JsonSerializer.Serialize(args, JsonOptions),
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);

        // The command on the far side may be compiling and then running the user's program, so the
        // wait is bounded by that rather than by anything network-shaped.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(McpBridgeProtocol.DefaultCommandTimeoutMs);

        string? line;
        try
        {
            line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeException(
                $"DoodleSharp did not answer '{command}' within " +
                $"{McpBridgeProtocol.DefaultCommandTimeoutMs / 1000}s. The program it is running may " +
                "be stuck in a loop — check the application window.");
        }

        if (line == null)
            throw new BridgeException($"DoodleSharp closed the connection without answering '{command}'.");

        BridgeResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<BridgeResponse>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BridgeException($"DoodleSharp sent a reply that could not be parsed: {ex.Message}");
        }

        if (response == null)
            throw new BridgeException($"DoodleSharp sent an empty reply to '{command}'.");

        if (!response.Ok)
            throw new BridgeException(response.Error ?? $"DoodleSharp refused '{command}' without saying why.");

        if (string.IsNullOrWhiteSpace(response.ResultJson))
            throw new BridgeException($"DoodleSharp answered '{command}' with no payload.");

        var payload = JsonSerializer.Deserialize<T>(response.ResultJson!, JsonOptions);
        if (payload == null)
            throw new BridgeException($"DoodleSharp's payload for '{command}' did not deserialize.");

        return payload;
    }
}
