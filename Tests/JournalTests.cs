using System;
using System.IO;
using System.Linq;
using System.Threading;
using DoodleSharp.Diagnostics;

namespace DoodleSharp.Tests;

/// <summary>
/// Behavioural tests for the crash journal. They run against a temp folder via the
/// <c>C2V_JOURNAL_DIR</c> override rather than the real <c>%TEMP%\C2V</c>.
///
/// <para>
/// <see cref="Journal"/> is process-global and starts exactly once, so these tests share one session
/// and must not run in parallel with each other — hence the collection.
/// </para>
/// </summary>
[Collection("JournalSession")]
public class JournalTests : IDisposable
{
    private readonly string _directory;

    public JournalTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "C2V_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("C2V_JOURNAL_DIR", _directory);
        Journal.ResetDirectoryCache();
        Journal.Start("DoodleSharp.Tests");
    }

    public void Dispose()
    {
        Journal.Flush();
        Environment.SetEnvironmentVariable("C2V_JOURNAL_DIR", null);
    }

    private string ReadJournal()
    {
        Journal.Flush();
        Assert.NotNull(Journal.FilePath);

        // FileShare.ReadWrite because the writer still holds the file open.
        using var stream = new FileStream(Journal.FilePath!, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Start_CreatesFileNamedWithTheSessionTimestamp()
    {
        Assert.True(Journal.IsEnabled, "Journal should be enabled in tests");
        var name = Path.GetFileNameWithoutExtension(Journal.FilePath!);

        // YYYYMMDDhhmmss, optionally with a "-N" collision suffix.
        var stamp = name.Split('-')[0];
        Assert.Equal(14, stamp.Length);
        Assert.True(stamp.All(char.IsDigit), $"'{stamp}' should be all digits");
        Assert.Equal(".log", Path.GetExtension(Journal.FilePath!));
    }

    [Fact]
    public void Header_RecordsTheMachineAndRuntimeFacts()
    {
        var text = ReadJournal();

        // These are the facts triage starts from; losing any of them silently would be expensive.
        Assert.Contains("os.description", text);
        Assert.Contains("clr.framework", text);
        Assert.Contains("cpu.count", text);
        Assert.Contains("proc.id", text);
        Assert.Contains("assemblies.loaded", text);
    }

    [Fact]
    public void Write_EmitsSiteKeyLevelAndCallerLocation()
    {
        Journal.Info("TEST.WRITE.BASIC", "hello", "k=v");

        var line = ReadJournal().Split('\n').Single(l => l.Contains("TEST.WRITE.BASIC"));
        Assert.Contains("INFO", line);
        Assert.Contains("hello", line);
        Assert.Contains("k=v", line);
        // The caller file and line are captured automatically.
        Assert.Contains("JournalTests.cs:", line);
        Assert.Contains(nameof(Write_EmitsSiteKeyLevelAndCallerLocation), line);
    }

    [Fact]
    public void Write_KeepsMultiLineMessagesOnASingleRecord()
    {
        Journal.Info("TEST.WRITE.MULTILINE", "line one\nline two\r\nline three");

        var lines = ReadJournal().Split('\n');
        var matches = lines.Where(l => l.Contains("line one")).ToList();

        // One physical line: the file has to stay greppable and machine-parseable.
        Assert.Single(matches);
        Assert.Contains("line two", matches[0]);
        Assert.Contains("line three", matches[0]);
    }

    [Fact]
    public void Write_RecordsTheFullExceptionChain()
    {
        var inner = new InvalidOperationException("the real cause");
        var outer = new ApplicationException("the symptom", inner);

        Journal.Error("TEST.WRITE.EXCEPTION", "something broke", outer);

        var text = ReadJournal();
        Assert.Contains("the symptom", text);
        Assert.Contains("the real cause", text);
        Assert.Contains("hresult=0x", text);
    }

    [Fact]
    public void MinimumLevel_FiltersLowerSeverities()
    {
        var previous = Journal.MinimumLevel;
        try
        {
            Journal.MinimumLevel = JournalLevel.Warn;
            Journal.Debug("TEST.LEVEL.DROPPED", "should not appear");
            Journal.Warn("TEST.LEVEL.KEPT", "should appear");

            var text = ReadJournal();
            Assert.DoesNotContain("TEST.LEVEL.DROPPED", text);
            Assert.Contains("TEST.LEVEL.KEPT", text);
        }
        finally
        {
            Journal.MinimumLevel = previous;
        }
    }

    [Fact]
    public void Scope_LogsEnterAndExitWithElapsedTime()
    {
        using (Journal.Scope("TEST.SCOPE.TIMED", "doing work"))
        {
            Thread.Sleep(5);
        }

        var text = ReadJournal();
        Assert.Contains("ENTER doing work", text);
        Assert.Matches(@"TEST\.SCOPE\.TIMED.*EXIT \(\d+", text.Replace("\r", ""));
    }

    [Fact]
    public void CaptureState_InvokesRegisteredProviders()
    {
        Journal.RegisterStateProvider("TestProvider", () => "answer = 42");
        Journal.CaptureState("unit test");

        var text = ReadJournal();
        Assert.Contains("STATE (unit test)", text);
        Assert.Contains("[TestProvider]", text);
        Assert.Contains("answer = 42", text);
    }

    [Fact]
    public void CaptureState_SurvivesAThrowingProvider()
    {
        Journal.RegisterStateProvider("Exploding", () => throw new InvalidOperationException("boom"));
        Journal.CaptureState("throwing provider");

        var text = ReadJournal();
        Assert.Contains("provider threw", text);
        Assert.Contains("boom", text);
        // Still terminated properly, so later records are not lost.
        Assert.Contains("END STATE", text);
    }

    [Fact]
    public void DescribeFile_ReportsSizeAndContentHash()
    {
        var path = Path.Combine(_directory, "sample.cs");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "class A {}");

        var described = Journal.DescribeFile(path, "class A {}");

        Assert.Contains("bytes=", described);
        Assert.Contains("mtime=", described);
        Assert.Contains("sha=" + Journal.ShortHash("class A {}"), described);
    }

    [Fact]
    public void DescribeFile_HandlesMissingAndEmptyPaths()
    {
        Assert.Contains("path=<empty>", Journal.DescribeFile(null));
        Assert.Contains("exists=0", Journal.DescribeFile(Path.Combine(_directory, "nope.cs")));
    }

    [Fact]
    public void ShortHash_ChangesWithContent()
    {
        Assert.NotEqual(Journal.ShortHash("a"), Journal.ShortHash("b"));
        Assert.Equal(Journal.ShortHash("same"), Journal.ShortHash("same"));
    }

    [Fact]
    public void WriteBlock_TruncatesOversizedContent()
    {
        Journal.WriteBlock("BIG", new string('x', 500), maxChars: 100);

        var text = ReadJournal();
        Assert.Contains("BEGIN BIG", text);
        Assert.Contains("truncated, 400 more chars", text);
        Assert.Contains("END BIG", text);
    }

    [Fact]
    public void Activity_DoesNotWriteARecordPerCall()
    {
        var before = ReadJournal().Split('\n').Length;
        for (var i = 0; i < 1000; i++)
            Journal.Activity("test.tick");

        var after = ReadJournal().Split('\n').Length;
        Assert.Equal(before, after);
    }
}

/// <summary>
/// Journal state is process-global (one file per process), so its tests must not interleave.
/// </summary>
[CollectionDefinition("JournalSession", DisableParallelization = true)]
public class JournalSessionCollection { }
