using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DoodleSharp.Documentation;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the one documentation defect that reading the prose cannot catch: describing a member that
/// does not exist. It has shipped repeatedly (CLAUDE.md note 62) — an entirely fabricated
/// <c>DoubleExtensions</c> class with runnable-looking samples, every documented member of
/// <c>VPlane</c>/<c>VCoordinateSystem</c>/<c>VTransform</c>, and most recently eight exporter members
/// of which several were **constructor parameters written up as properties**
/// (<c>GifEncoder.FrameDelay</c>, <c>GifEncoder.Repeat</c>, <c>SvgExporter.Width</c>). A fabricated
/// entry reads exactly like a real one, so only a diff against the real surface finds it.
///
/// <para>
/// This runs that diff on every build, which is what makes the reflection audit a standing guarantee
/// rather than something remembered at release time.
/// </para>
/// </summary>
public class DocumentationAccuracyTests
{
    [Fact]
    public void EveryDocumentedMemberExists()
    {
        var generator = new DocGenerator();
        var types = generator.GetDocumentableTypes()
            .GroupBy(t => DocGenerator.GetCleanTypeName(t))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var offenders = new List<string>();

        foreach (var key in MemberDescriptionKeys())
        {
            // Keys are "Type.Member". A dotted member name is not a thing, so the split is safe.
            var split = key.LastIndexOf('.');
            if (split <= 0) continue;

            var typeName = key.Substring(0, split);
            var memberName = key.Substring(split + 1);

            // A type outside the documented namespaces is not this test's business — the help window
            // never renders it, so a description for it is dead weight rather than a wrong claim.
            if (!types.TryGetValue(typeName, out var type)) continue;

            if (!MemberExists(type, memberName))
                offenders.Add(key);
        }

        Assert.True(offenders.Count == 0,
            "Documentation/DocGenerator.cs describes members that do not exist on the real API. "
            + "Either the member was never implemented, or it is a constructor/method PARAMETER "
            + "written up as a property, or it has been renamed. Check the signature in the source "
            + "before correcting the text.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// True when the name resolves to anything publicly reachable on the type — instance or static,
    /// inherited or declared, including enum values and constants. Deliberately permissive: this
    /// test is hunting for names that exist nowhere, not for members the help window happens to list.
    /// </summary>
    private static bool MemberExists(Type type, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        if (type.GetMember(memberName, flags).Length > 0) return true;

        // FlattenHierarchy does not walk base interfaces, and ICurve inherits IDrawable's members.
        if (type.IsInterface
            && type.GetInterfaces().Any(i => i.GetMember(memberName, flags).Length > 0))
            return true;

        // Explicit interface implementations are non-public on the concrete type: VLine satisfies
        // ICurve.StartPoint/EndPoint that way (note 33), and documenting them is correct.
        if (type.GetInterfaces().Any(i => i.GetMember(memberName, flags).Length > 0))
            return true;

        // Indexers are reflected as "Item"; the docs name them for what they are.
        if (memberName is "Item" or "this") return true;

        return false;
    }

    [Fact]
    public void EveryDocumentedTypeExists()
    {
        // _summaries had NO accuracy guard, and that is exactly how 13 summaries for the retired
        // DoodleSharp.Geometry namespace (Arc2D, Circle2D, Line2D, Point2D, …) survived every build
        // long after the types were deleted. EveryDocumentedMemberExists could never have caught
        // them: it reflects _memberDescriptions only, and it deliberately SKIPS any key whose type
        // is not in GetDocumentableTypes — which is precisely the state a deleted type is in.
        var generator = new DocGenerator();

        var known = new HashSet<string>(
            generator.GetDocumentableTypes().Select(DocGenerator.GetCleanTypeName), StringComparer.Ordinal);

        // Namespace keys are legitimate: the tree has a page per namespace as well as per type.
        var namespaces = new HashSet<string>(
            generator.GetDocumentableTypes()
                     .Select(t => t.Namespace)
                     .Where(n => n != null)
                     .SelectMany(n => Prefixes(n!)),
            StringComparer.Ordinal);

        var offenders = SummaryKeys()
            .Where(k => !known.Contains(k) && !namespaces.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Documentation/DocGenerator.cs summarises types that do not exist, or that are not "
            + "reachable in the help tree. A summary for a deleted type is dead weight no reader "
            + "can ever see; one for a real but unreachable type means it needs adding to "
            + "DocGenerator.AllowedInternalTypes." + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", offenders));

        // "DoodleSharp.Animation" also makes "DoodleSharp" a legitimate key.
        static IEnumerable<string> Prefixes(string ns)
        {
            var parts = ns.Split('.');
            for (var i = 1; i <= parts.Length; i++)
                yield return string.Join(".", parts.Take(i));
        }
    }

    [Fact]
    public void EveryAllowlistedInternalTypeIsReachable()
    {
        // The allowlist is full names typed by hand, so a rename or a namespace move turns an entry
        // into a silent no-op — the page simply disappears again, which is the failure it exists to
        // prevent. Assert both halves: the type resolves, and it actually lands in the tree.
        var generator = new DocGenerator();
        var tree = generator.GetDocumentableTypes().Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);

        foreach (var name in DocGenerator.AllowedInternalTypes)
        {
            Assert.True(Type.GetType($"{name}, DoodleSharp") != null, $"{name} does not resolve");
            Assert.Contains(name, tree);
        }
    }

    private static IEnumerable<string> SummaryKeys()
    {
        var field = typeof(DocGenerator).GetField("_summaries",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field); // a renamed field would silently make this test vacuous

        var value = field!.GetValue(new DocGenerator()) as Dictionary<string, string>;

        Assert.NotNull(value);
        Assert.NotEmpty(value!);

        return value!.Keys;
    }

    private static IEnumerable<string> MemberDescriptionKeys()
    {
        var field = typeof(DocGenerator).GetField("_memberDescriptions",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field); // renamed field would silently make this test vacuous

        var value = field!.GetValue(new DocGenerator()) as Dictionary<string, string>;

        Assert.NotNull(value);
        Assert.NotEmpty(value!);

        return value!.Keys;
    }
}
