using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards <c>installer.iss</c>, which is otherwise exercised **only at release time** — by Inno Setup,
/// on a CI runner, after the tag has already been pushed. That is far too late to find out it does not
/// parse.
///
/// <para>
/// It has already cost a release. The Direct3D commit wrote the path
/// <c>runtimes\win-x64\native</c> into a comment; the <c>\n</c> was taken as a newline escape, which
/// split the comment in two and left <c>ative folder -- so they land flat in</c> as a bare line. Inno
/// Setup parsed that as a <c>[Files]</c> parameter and aborted with "Unrecognized parameter name".
/// The file sat broken on main for four commits because nothing reads it until a release runs.
/// </para>
/// </summary>
public class InstallerScriptTests
{
    /// <summary>
    /// Directives that may legally begin a line inside each section. Anything else in a section body
    /// is either a typo or — the case that actually happened — the tail of a mangled comment.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Files"] = ["Source:"],
        ["Icons"] = ["Name:"],
        ["Registry"] = ["Root:"],
        ["Run"] = ["Filename:"],
        ["Tasks"] = ["Name:"],
        ["Languages"] = ["Name:"],
        ["Dirs"] = ["Name:"],
        ["InstallDelete"] = ["Type:"],
        ["UninstallDelete"] = ["Type:"],
    };

    [Fact]
    public void EverySectionLineIsAValidDirective()
    {
        var offenders = new List<string>();
        var section = "";
        var lineNo = 0;

        foreach (var raw in File.ReadAllLines(InstallerPath()))
        {
            lineNo++;
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            if (!AllowedKeys.TryGetValue(section, out var keys)) continue;

            if (!keys.Any(k => line.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                offenders.Add($"line {lineNo} in [{section}] does not start with "
                            + $"{string.Join("/", keys)}: {Truncate(raw)}");
        }

        Assert.True(offenders.Count == 0,
            "installer.iss has lines Inno Setup cannot parse. The usual cause is a comment whose "
            + "continuation lost its leading ';' — check for a backslash-escape splitting a line.\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The installer enumerates its payload explicitly rather than globbing, so a new dependency that
    /// is not listed simply does not ship. These are the ones whose absence breaks the app outright:
    /// Clipper2 backs every polygon boolean (note 32) and the eight Vortice assemblies are the GPU
    /// backend, which ships managed-only and therefore lands flat in the build output (note 88).
    /// </summary>
    [Theory]
    [InlineData("DoodleSharp.exe")]
    [InlineData("C2VGeometry.dll")]
    [InlineData("Clipper2Lib.dll")]
    [InlineData("Vortice.Direct3D11.dll")]
    [InlineData("Vortice.Direct3D9.dll")]
    [InlineData("Vortice.D3DCompiler.dll")]
    [InlineData("Vortice.DXGI.dll")]
    [InlineData("Vortice.DirectX.dll")]
    public void RequiredPayloadIsEnumerated(string fileName)
    {
        var text = File.ReadAllText(InstallerPath());

        Assert.Contains(fileName, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The two version sources must agree. The release workflow's first step fails when
    /// <c>Directory.Build.props</c> does not match the tag, but nothing checks it against
    /// <c>installer.iss</c> — and only <c>scripts/release.ps1</c> writes both, so a hand edit to
    /// either drifts silently until an installer ships with the wrong version stamped on it.
    /// </summary>
    [Fact]
    public void InstallerVersionMatchesDirectoryBuildProps()
    {
        var props = XDocument.Load(Path.Combine(ArrowheadConsistencyTests.RepoRoot(),
                                                "Directory.Build.props"));
        var propsVersion = props.Descendants("Version").First().Value.Trim();

        var match = Regex.Match(File.ReadAllText(InstallerPath()),
                                @"^#define\s+MyAppVersion\s+""([^""]+)""",
                                RegexOptions.Multiline);

        Assert.True(match.Success, "installer.iss has no #define MyAppVersion line.");
        Assert.Equal(propsVersion, match.Groups[1].Value);
    }

    private static string InstallerPath()
    {
        var path = Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "installer.iss");
        Assert.True(File.Exists(path), $"installer.iss not found at {path}");
        return path;
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..80] + "...";
}
