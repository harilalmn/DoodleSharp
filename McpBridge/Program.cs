using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The DoodleSharp MCP bridge.
//
// Claude Code launches this over stdio and manages its lifetime; it forwards each tool call to the
// running DoodleSharp window over a named pipe (see Mcp/McpBridgeProtocol.cs, which is compiled
// into both this process and the app).
//
// Register it once with:
//   claude mcp add doodlesharp -- "C:\Program Files\DoodleSharp\DoodleSharp.Mcp.exe"

var builder = Host.CreateApplicationBuilder(args);

// stdout IS the protocol. Anything written there that is not a JSON-RPC message corrupts the
// stream and the client drops the connection, so every log line goes to stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
