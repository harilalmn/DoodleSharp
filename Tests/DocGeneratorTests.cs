using System;
using System.Linq;
using DoodleSharp.Documentation;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the F1 Help window against the failure that has already shipped once: `DocGenerator`
/// builds its three lookup dictionaries with collection initialisers keyed by `type.Name`, and a
/// duplicate key throws <see cref="ArgumentException"/> straight out of the constructor — so the
/// Help window dies on open rather than showing a slightly wrong entry.
///
/// <para>
/// Documentation is edited far more often than it is exercised, and by more than one author at a
/// time, so this needs to fail in CI rather than in someone's Help window.
/// </para>
/// </summary>
public class DocGeneratorTests
{
    [Fact]
    public void ConstructsWithoutDuplicateKeys()
    {
        // The constructor populates every dictionary; a duplicate key throws here.
        var exception = Record.Exception(() => new DocGenerator());

        Assert.True(exception == null,
            "DocGenerator failed to construct — most likely a duplicate key in _summaries, " +
            $"_csharpSamples or _memberDescriptions. This crashes F1 Help on open. {exception?.Message}");
    }

    [Fact]
    public void DocumentsTheTypesUsersActuallyWriteAgainst()
    {
        var generator = new DocGenerator();
        var types = generator.GetDocumentableTypes();

        Assert.NotEmpty(types);

        // VXYZ especially: it is the coordinate type every example depends on, and it was recently
        // being filtered out of IntelliSense by an all-uppercase heuristic.
        var names = types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "VXYZ", "VCircle", "VLine", "VPolygon", "VText" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void EveryDocumentedTypeRendersWithoutThrowing()
    {
        // GenerateDocForType reads the sample and member dictionaries per type; a malformed or
        // mismatched entry surfaces here rather than when the user opens that page in Help.
        var generator = new DocGenerator();

        foreach (var type in generator.GetDocumentableTypes())
        {
            var exception = Record.Exception(() => generator.GenerateDocForType(type));
            Assert.True(exception == null, $"Help page for {type.Name} threw: {exception?.Message}");
        }
    }
}
