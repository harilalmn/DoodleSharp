using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the property the whole crash-triage workflow rests on: <b>a journal site key identifies
/// exactly one line of code in this repository</b>.
///
/// <para>
/// When a user sends in a journal, the site key in a record is looked up in the source to find where
/// it was written. If two call sites shared a key, that lookup would be ambiguous and the journal
/// would stop being able to pin down a location — so this test fails the build rather than letting
/// the ambiguity ship. The key format is also enforced so keys stay greppable and sortable by area.
/// </para>
/// </summary>
public class JournalSiteKeyTests
{
    /// <summary>AREA.SUB[.SUB...] in upper case, e.g. <c>MW.RUN.BEGIN</c> or <c>EXEC.EMIT.OK</c>.</summary>
    private static readonly Regex KeyFormat = new(@"^[A-Z][A-Z0-9]*(\.[A-Z0-9_]+)+$", RegexOptions.Compiled);

    /// <summary>Matches <c>Journal.Info("KEY"</c>, <c>Journal.Scope("KEY"</c>, <c>Journal.Write(level, "KEY"</c>, ...</summary>
    private static readonly Regex CallSite = new(
        @"Journal\s*\.\s*(?:Trace|Debug|Info|Warn|Error|Fatal|Scope)\s*\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex WriteCallSite = new(
        @"Journal\s*\.\s*Write\s*\(\s*JournalLevel\.\w+\s*,\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    private sealed record Occurrence(string Key, string File, int Line);

    [Fact]
    public void EverySiteKeyIsUsedAtExactlyOneCallSite()
    {
        var occurrences = CollectOccurrences();
        Assert.NotEmpty(occurrences);

        var duplicated = occurrences
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicated.Count > 0)
        {
            var report = string.Join(Environment.NewLine, duplicated.Select(g =>
                $"  {g.Key} used at: " + string.Join(", ", g.Select(o => $"{o.File}:{o.Line}"))));
            Assert.Fail(
                "Journal site keys must be unique so a key in a shared journal maps to one line of code." +
                Environment.NewLine + report);
        }
    }

    [Fact]
    public void EverySiteKeyFollowsTheAreaDottedFormat()
    {
        var malformed = CollectOccurrences()
            .Where(o => !KeyFormat.IsMatch(o.Key))
            .ToList();

        if (malformed.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                malformed.Select(o => $"  '{o.Key}' at {o.File}:{o.Line}"));
            Assert.Fail(
                "Journal site keys must be UPPER.DOTTED.SEGMENTS (e.g. MW.RUN.BEGIN)." +
                Environment.NewLine + report);
        }
    }

    [Fact]
    public void CriticalPathsAreInstrumented()
    {
        // A regression guard with teeth: if someone strips journaling out of startup, file opening,
        // execution or the crash handlers, the journal silently stops being able to explain a crash.
        var keys = CollectOccurrences().Select(o => o.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[]
                 {
                     "APP.STARTUP",          // process start
                     "PROJ.FILE.OPEN",       // a file was opened
                     "MW.FILE.SELECT",       // a file became active in the editor
                     "EXEC.MAIN.INVOKE",     // user code entered
                     "CRASH.WPF.DISPATCHER", // UI-thread crash
                     "CRASH.APPDOMAIN",      // background-thread crash
                     "DIAG.UI.HANG",         // frozen UI
                     "JRNL.HEARTBEAT",       // liveness pulse
                 })
        {
            Assert.True(keys.Contains(required),
                $"Site key '{required}' is missing — a critical diagnostic path lost its instrumentation.");
        }
    }

    private static List<Occurrence> CollectOccurrences()
    {
        var root = FindRepositoryRoot();
        var results = new List<Occurrence>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (IsExcluded(relative)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CallSite.Matches(lines[i]))
                    results.Add(new Occurrence(match.Groups["key"].Value, relative, i + 1));
                foreach (Match match in WriteCallSite.Matches(lines[i]))
                    results.Add(new Occurrence(match.Groups["key"].Value, relative, i + 1));
            }
        }

        return results;
    }

    private static bool IsExcluded(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // bin/obj hold generated and copied sources; Tests keys are fixtures, not shipping call sites.
        return parts.Any(p =>
            p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Sample Projects", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DoodleSharp.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (DoodleSharp.sln) from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
