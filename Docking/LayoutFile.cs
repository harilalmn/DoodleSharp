using System;
using System.IO;
using System.Xml.Linq;

namespace DoodleSharp.Docking;

/// <summary>
/// The on-disk wrapper around a serialized AvalonDock layout.
///
/// <para>
/// A layout is a preference, not data. If the file cannot be understood the only sane response is to
/// throw it away and use the default — an app that will not start, or that hides a panel the user
/// cannot find, costs far more than one forgotten arrangement. So there is deliberately **no
/// migration path**: <see cref="CurrentSchema"/> is bumped whenever a panel is renamed or removed, and
/// a file stamped with anything else is discarded whole.
/// </para>
///
/// <para>
/// The parsing lives here, away from WPF, because this runs on every launch and its failure mode is
/// "the window does not come up". That makes it worth having as a plain, directly testable function
/// over a string rather than something only reachable through a live <c>DockingManager</c>.
/// </para>
/// </summary>
internal static class LayoutFile
{
    /// <summary>
    /// Bump when a ContentId is renamed or removed, or the default arrangement changes materially.
    /// Every existing saved layout is then ignored, and users get the new default once.
    /// </summary>
    internal const int CurrentSchema = 1;

    private const string RootElement = "DoodleSharpLayout";
    private const string SchemaAttribute = "schema";
    private const string AppAttribute = "app";

    /// <summary>Wraps a raw AvalonDock layout in the versioned envelope that gets written to disk.</summary>
    /// <param name="layoutXml">The <c>&lt;LayoutRoot&gt;</c> document AvalonDock serialized.</param>
    /// <param name="appVersion">Recorded for support only; never used to decide anything.</param>
    internal static string Wrap(string layoutXml, string appVersion)
    {
        var inner = XElement.Parse(layoutXml);

        var doc = new XElement(RootElement,
            new XAttribute(SchemaAttribute, CurrentSchema),
            new XAttribute(AppAttribute, appVersion ?? ""),
            inner);

        return doc.ToString();
    }

    /// <summary>
    /// Extracts the AvalonDock layout from a wrapped file, or null when it cannot be trusted —
    /// unparseable, wrong root, missing or unrecognised schema, or empty.
    ///
    /// <para>Never throws. Every failure is the same failure: use the default instead.</para>
    /// </summary>
    internal static string? Unwrap(string? fileContents)
    {
        if (string.IsNullOrWhiteSpace(fileContents)) return null;

        try
        {
            var doc = XElement.Parse(fileContents);
            if (doc.Name != RootElement) return null;

            var schema = (int?)doc.Attribute(SchemaAttribute);
            if (schema != CurrentSchema) return null;

            var inner = doc.Elements().FirstOrDefault();
            return inner?.ToString();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// The registered panels that a restored layout does not mention — panels added since it was
    /// saved. AvalonDock simply omits them, so without this they would not exist at all and their
    /// menu entry would silently do nothing. The caller re-inserts each one, hidden.
    /// </summary>
    internal static IReadOnlyList<string> FindMissingIds(
        IEnumerable<string> registered, IEnumerable<string> presentInLayout)
    {
        var present = new HashSet<string>(presentInLayout, StringComparer.Ordinal);
        return registered.Where(id => !present.Contains(id)).ToList();
    }
}
