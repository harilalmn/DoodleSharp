using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards for the MCP surface (note 146). Source-text guards in the idiom of
/// <see cref="AutoUpdateRemovalTests"/>, because what they pin is not reachable by calling it: the
/// commands need a live <c>MainWindow</c> on a WPF dispatcher, and the failures they protect
/// against are silent — the run reports success either way, and only the shape count gives it
/// away.
/// </summary>
public class McpSurfaceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relative));

    private static string McpCommands() => Read(Path.Combine("Mcp", "MainWindow.Mcp.cs"));

    /// <summary>
    /// The run command must refresh through the <b>window</b>, not through the project.
    ///
    /// <para>
    /// This shipped broken. <c>VizCodeProject.RefreshFilesFromDisk</c> updates the file model but
    /// only <c>MainWindow.RefreshProjectFromDisk</c> pushes the new text into the open editor —
    /// and <c>RunSilentlyAsync</c> begins by saving the editor back over the file it just re-read.
    /// So calling the project-level method (alone, or worse, before the window-level one, which
    /// then correctly reports nothing left to do) means the agent's edit is read from disk,
    /// discarded, and the old code runs while the tool cheerfully reports success.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRunCommandRefreshesThroughTheWindow()
    {
        var code = McpCommands();

        Assert.Contains("RefreshProjectFromDisk()", code, System.StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshFilesFromDisk()", code, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// And exactly once. Two refreshes are worse than one: the second finds nothing to do, so the
    /// editor is never updated at all.
    /// </summary>
    [Fact]
    public void TheRunCommandRefreshesExactlyOnce()
    {
        Assert.Equal(1, Regex.Matches(McpCommands(), @"RefreshProjectFromDisk\(\)").Count);
    }

    /// <summary>
    /// Diagnostics reach the agent with a line but never a column. They come from the execute-path
    /// compilation, which injects a stack guard at the top of every method body (note 21); the
    /// injection is trivia-free so lines survive, but it is ~86 characters wide and every column on
    /// the line it was inserted into is wrong by that much. A column that points confidently at the
    /// wrong character is worse than no column.
    /// </summary>
    [Fact]
    public void ReportedDiagnosticsCarryNoColumn()
    {
        var formatter = Read(Path.Combine("McpBridge", "DoodleSharpTools.cs"));

        Assert.DoesNotContain("{d.Line}:{d.Column}", formatter, System.StringComparison.Ordinal);
        Assert.Contains("{d.Line}", formatter, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>ImageContentBlock.Data</c> is a <c>ReadOnlyMemory&lt;byte&gt;</c> holding the
    /// <b>already-base64 text</b> as UTF-8 — not the image bytes.
    ///
    /// <para>
    /// Handing it the raw PNG compiles cleanly and ships a corrupt picture: the serializer decodes
    /// whatever is in there as UTF-8 into a JSON string, so every byte that is not valid UTF-8 —
    /// beginning with the PNG magic <c>0x89</c> — is replaced by U+FFFD. Nothing throws, the agent
    /// receives an image block with a plausible MIME type, and the bytes are irrecoverable. Caught
    /// only by reading them back off the wire, which is why the shape of the fix is pinned here.
    /// </para>
    /// </summary>
    [Fact]
    public void TheImageBlockCarriesBase64TextNotRawBytes()
    {
        var tools = Read(Path.Combine("McpBridge", "DoodleSharpTools.cs"));

        Assert.Contains("Data = Encoding.UTF8.GetBytes(shot.PngBase64)", tools, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Convert.FromBase64String", tools, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The bridge links the protocol file rather than referencing DoodleSharp. A project reference
    /// would drag WPF into a console process; two hand-maintained copies would drift. Either
    /// failure is a wire-format mismatch that only shows up at runtime.
    /// </summary>
    [Fact]
    public void TheBridgeLinksTheProtocolRatherThanReferencingTheApp()
    {
        var csproj = Read(Path.Combine("McpBridge", "DoodleSharp.Mcp.csproj"));

        Assert.Contains(@"Compile Include=""..\Mcp\McpBridgeProtocol.cs""", csproj, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", csproj, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The bridge's output is excluded from the app's compile glob. Without this the app would
    /// compile the bridge's <c>Program.cs</c> — a second top-level entry point — and the build
    /// would fail in a way that points nowhere near the cause (note 69 records the same trap for
    /// the reflection dumper).
    /// </summary>
    [Fact]
    public void TheAppDoesNotCompileTheBridge()
    {
        Assert.Contains(@"<Compile Remove=""McpBridge\**"" />", Read("DoodleSharp.csproj"),
            System.StringComparison.Ordinal);
    }

    /// <summary>
    /// One window serves. A second DoodleSharp must fail to take the pipe rather than race for an
    /// agent's commands, which is what <c>maxNumberOfServerInstances: 1</c> buys.
    /// </summary>
    [Fact]
    public void OnlyOneWindowCanServe()
    {
        Assert.Contains("maxNumberOfServerInstances: 1", Read(Path.Combine("Mcp", "McpPipeServer.cs")),
            System.StringComparison.Ordinal);
    }
}
