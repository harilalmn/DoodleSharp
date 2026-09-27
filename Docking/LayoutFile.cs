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
    /// Removes every floating window that holds no content — no <c>LayoutAnchorable</c> and no
    /// <c>LayoutDocument</c> anywhere beneath it.
    ///
    /// <para>
    /// AvalonDock leaves such a window behind when the panel floating in it is hidden: the empty pane is
    /// kept as the panel's <c>PreviousContainer</c>, and it gets saved as it is. Restoring it is fatal.
    /// Assigning the layout builds a window for it and queues <c>Show()</c> on the dispatcher. The
    /// empty window is then garbage-collected out of the model, which closes that window, and the queued
    /// <c>Show()</c> throws on a closed window. It throws inside a dispatcher callback, outside any
    /// catch, on every launch (note 147). Removing the window here means AvalonDock never sees it.
    /// The hidden panel then comes back docked by default.
    /// </para>
    ///
    /// <para>Never throws. Input that will not parse comes back unchanged, for the deserializer to reject.</para>
    /// </summary>
    internal static string PruneEmptyFloatingWindows(string layoutXml, out int removed)
    {
        removed = 0;
        if (string.IsNullOrWhiteSpace(layoutXml)) return layoutXml;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(layoutXml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return layoutXml;
        }

        var empty = doc.Descendants()
            .Where(e => e.Name.LocalName == "FloatingWindows")
            .SelectMany(fw => fw.Elements())
            .Where(window => !window.Descendants().Any(d =>
                d.Name.LocalName is "LayoutAnchorable" or "LayoutDocument"))
            .ToList();

        if (empty.Count == 0) return layoutXml;

        foreach (var window in empty) window.Remove();
        removed = empty.Count;

        // XDocument.ToString drops the declaration; keep it when there was one so the result stays a
        // drop-in replacement for what the serializer produced.
        return doc.Declaration is { } decl ? decl + Environment.NewLine + doc.ToString() : doc.ToString();
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
