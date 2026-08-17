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
    [InlineData("AvalonDock.dll")]
    [InlineData("AvalonDock.Core.dll")]
    [InlineData("AvalonDock.Themes.VS2013.dll")]
    public void RequiredPayloadIsEnumerated(string fileName)
    {
        var text = File.ReadAllText(InstallerPath());

        Assert.Contains(fileName, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The general form of the test above, and the one that ends the whole class of bug: every
    /// assembly the app actually builds against must be named in the script.
    ///
    /// <para>
    /// Adding a NuGet package has now silently under-shipped three times, because a package's
    /// transitive closure is not guessable from its id — Vortice's three <c>PackageReference</c>s
    /// produce eight assemblies, and AvalonDock's packages are named <c>Dirkster.AvalonDock*</c> while
    /// their assemblies drop the prefix. Enumerating the build output removes the guesswork: whatever
    /// the restore actually produced is what has to ship.
    /// </para>
    ///
    /// <para>
    /// Reads the app's own build output, which the test run has necessarily just built (the test
    /// project references it) — deliberately not the *test* project's output, which also contains
    /// xunit and its friends, none of which ship.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryAssemblyInTheBuildOutputIsShipped()
    {
        var outputDir = FindAppBuildOutput();
        if (outputDir == null) return;   // no build output to compare against; nothing to assert

        var script = File.ReadAllText(InstallerPath());

        var missing = Directory.GetFiles(outputDir, "*.dll")
            .Select(Path.GetFileName)
            .Where(name => !script.Contains(name!, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(missing.Length == 0,
            "These assemblies are built but never installed, so the app would fail at run time on a "
            + "user's machine while working perfectly here:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Locates the DoodleSharp build output for whichever configuration was built most recently.
    /// Returns null rather than failing when there is none, so the suite still runs on a clean tree.
    /// </summary>
    private static string? FindAppBuildOutput()
    {
        var root = ArrowheadConsistencyTests.RepoRoot();

        return new[] { "Debug", "Release" }
            .Select(cfg => Path.Combine(root, "bin", cfg, "net9.0-windows"))
            .Where(Directory.Exists)
            .Where(dir => File.Exists(Path.Combine(dir, "DoodleSharp.dll")))
            .OrderByDescending(dir => File.GetLastWriteTimeUtc(Path.Combine(dir, "DoodleSharp.dll")))
            .FirstOrDefault();
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
