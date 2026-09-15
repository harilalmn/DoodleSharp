using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DoodleSharp.Diagnostics;

namespace DoodleSharp.Mcp;

/// <summary>
/// Listens on a named pipe for commands from the <c>DoodleSharp.Mcp</c> bridge and runs them
/// against the live window.
///
/// <para>
/// <b>Why a pipe and not an HTTP endpoint.</b> The MCP C# SDK offers a Streamable HTTP transport
/// that would let the app serve MCP directly, but it needs the ASP.NET Core shared framework —
/// which would add a second runtime prerequisite to an installer that already has to talk the user
/// through installing the .NET Desktop Runtime. A pipe adds no runtime requirement, no listening
/// TCP port, and no firewall prompt. That last point is not cosmetic: the commands on the other end
/// of this pipe compile and execute arbitrary C# <i>in this process</i>, so the transport's access
/// control is the whole security story. The pipe is ACL'd to the user who started the app.
/// </para>
///
/// <para>
/// <b>Every handler runs on the UI thread and cannot be allowed to throw.</b> Commands arrive on a
/// pool thread and touch <c>MainWindow</c>, the canvas and the compiler, all of which are thread
/// affine — hence the <see cref="Dispatcher"/> hop in <see cref="DispatchAsync"/>. And an exception
/// that escapes a handler closes the application, exactly as notes 134 and 137 describe for the
/// app's own event handlers; a crash an agent can trigger remotely is the same bug with a worse
/// story, so every layer here catches.
/// </para>
/// </summary>
internal sealed class McpPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Dispatcher _dispatcher;
    private readonly IReadOnlyDictionary<string, Func<JsonElement?, Task<object?>>> _handlers;

    private CancellationTokenSource? _cts;
    private Task? _listenLoop;
    private bool _disposed;

    /// <summary>True once the listener owns the pipe name. False means another window got there first.</summary>
    public bool IsServing { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public McpPipeServer(
        Dispatcher dispatcher,
        IReadOnlyDictionary<string, Func<JsonElement?, Task<object?>>> handlers,
        string pipeName = McpBridgeProtocol.DefaultPipeName)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _pipeName = pipeName;
    }

    /// <summary>
    /// Starts listening in the background. Never throws: a server that cannot start is a feature
    /// the user does not get, not a reason the app fails to open.
    /// </summary>
    public void Start()
    {
        if (_listenLoop != null) return;

        try
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _listenLoop = Task.Run(() => ListenLoopAsync(token), token);
            Journal.Info("MCP.START", "MCP pipe listener starting", $"pipe={_pipeName}");
        }
        catch (Exception ex)
        {
            Journal.Error("MCP.START.FAILED", "MCP pipe listener could not start", ex);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
            }
            catch (IOException ex)
            {
                // All instances busy: another DoodleSharp window owns the name. Documented
                // behaviour, not an error — say so once and stop trying.
                Journal.Warn("MCP.PIPE.TAKEN",
                    "Another DoodleSharp instance owns the MCP pipe; this window will not serve MCP",
                    $"pipe={_pipeName}", ex);
                IsServing = false;
                return;
            }
            catch (Exception ex)
            {
                Journal.Error("MCP.PIPE.CREATE_FAILED", "Could not create the MCP pipe", ex);
                return;
            }

            try
            {
                IsServing = true;
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                Journal.Info("MCP.CLIENT.CONNECTED", "MCP bridge connected");

                await ServeClientAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                Journal.Warn("MCP.CLIENT.FAILED", "MCP connection ended in an error", null, ex);
            }
            finally
            {
                try { pipe.Dispose(); } catch { /* the connection is already gone */ }
            }
        }
    }

    /// <summary>
    /// Creates the pipe with an ACL that admits only the user running the app. A default ACL would
    /// be wider than the thing behind it deserves.
    /// </summary>
    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var self = WindowsIdentity.GetCurrent().User;
        if (self != null)
        {
            security.AddAccessRule(new PipeAccessRule(
                self, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    /// <summary>
    /// Reads newline-delimited requests until the bridge disconnects. Commands are handled one at a
    /// time and in order — the app has one canvas, so there is nothing to gain from overlapping a
    /// second run on top of a running one.
    /// </summary>
    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;                       // bridge closed the pipe
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = await HandleLineAsync(line).ConfigureAwait(false);

            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;                                     // bridge vanished mid-reply
            }
        }

        Journal.Info("MCP.CLIENT.DISCONNECTED", "MCP bridge disconnected");
    }

    /// <summary>
    /// Turns one request line into one response. Every failure mode — malformed JSON, unknown
    /// command, a handler that threw — becomes an <c>ok: false</c> reply the agent can read and act
    /// on, never an exception that escapes this method.
    /// </summary>
    private async Task<BridgeResponse> HandleLineAsync(string line)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new BridgeResponse { Id = string.Empty, Ok = false, Error = $"Malformed request: {ex.Message}" };
        }

        if (request == null || string.IsNullOrEmpty(request.Command))
            return new BridgeResponse { Id = request?.Id ?? string.Empty, Ok = false, Error = "Request had no command." };

        if (!_handlers.TryGetValue(request.Command, out var handler))
        {
            return new BridgeResponse
            {
                Id = request.Id,
                Ok = false,
                Error = $"Unknown command '{request.Command}'. Known: {string.Join(", ", _handlers.Keys)}.",
            };
        }

        JsonElement? args = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.ArgsJson))
                args = JsonDocument.Parse(request.ArgsJson!).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new BridgeResponse { Id = request.Id, Ok = false, Error = $"Malformed arguments: {ex.Message}" };
        }

        try
        {
            var result = await DispatchAsync(handler, args).ConfigureAwait(false);
            return new BridgeResponse
            {
                Id = request.Id,
                Ok = true,
                ResultJson = result == null ? null : JsonSerializer.Serialize(result, JsonOptions),
            };
        }
        catch (Exception ex)
        {
            Journal.Error("MCP.COMMAND.THREW", "An MCP command threw", ex);
            return new BridgeResponse { Id = request.Id, Ok = false, Error = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    /// <summary>
    /// Runs a handler on the UI thread. <c>Dispatcher.InvokeAsync</c> hands back a task for the
    /// <i>outer</i> delegate, so the handler's own task has to be unwrapped — awaiting only the
    /// outer one would report success the moment the compile was scheduled.
    /// </summary>
    private async Task<object?> DispatchAsync(Func<JsonElement?, Task<object?>> handler, JsonElement? args)
    {
        var outer = _dispatcher.InvokeAsync(() => handler(args));
        var inner = await outer.Task.ConfigureAwait(false);
        return await inner.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch (Exception ex)
        {
            Journal.Warn("MCP.STOP.FAILED", "MCP pipe listener did not stop cleanly", null, ex);
        }

        Journal.Info("MCP.STOP", "MCP pipe listener stopped");
    }
}
