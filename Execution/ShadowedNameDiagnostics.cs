using System;
using System.Collections.Generic;
using System.Linq;
using DoodleSharp.Project;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DoodleSharp.Execution;

/// <summary>
/// Rewrites the compiler's report of a shadowed DoodleSharp name so it points at the declaration
/// that caused it instead of at the innocent use site.
/// </summary>
/// <remarks>
/// <para>
/// A project named <c>Mouse</c> generates <c>namespace Mouse</c>, and C# searches the enclosing
/// namespace before any <c>using</c> — so <c>Mouse.OnMove(...)</c> is looked up inside the user's
/// own namespace and Roslyn reports <c>CS0234: The type or namespace name 'OnMove' does not exist
/// in the namespace 'Mouse'</c>, underlining <c>OnMove</c>. That names the one token which is not
/// wrong. The declaration is the fault, and the declaration is what has to change, so this maps the
/// error back onto it: <c>Mouse is a keyword. try another name</c>, underlining <c>Mouse</c> on the
/// <c>namespace</c> line.
/// </para>
/// <para>
/// The same shadowing happens for a type, a local, a field, a property, a parameter and a
/// <c>foreach</c> variable, each with its own compiler error id, so all of them are remapped and
/// all of them produce the one message.
/// </para>
/// <para>
/// <b>Three conditions must all hold before a diagnostic is remapped</b>, and none of them is
/// redundant — dropping any one turns an unrelated mistake into a misleading "is a keyword" report:
/// the name must be declared in the user's own source; the compiler error must be one of the
/// lookup failures that shadowing actually produces (<see cref="ShadowingErrorIds"/>); and the
/// qualifier must <em>bind to that source declaration</em> rather than to the library type, which
/// is the part that proves the shadowing really happened and is answered by the semantic model,
/// not by matching the message text.
/// </para>
/// </remarks>
public static class ShadowedNameDiagnostics
{
    /// <summary>The id carried by the remapped diagnostic.</summary>
    public const string DiagnosticId = "DS0001";

    /// <summary>The message format, with the offending name substituted for <c>{0}</c>.</summary>
    public const string MessageFormat = "{0} is a keyword. try another name";

    /// <summary>
    /// The lookup failures a shadowed name produces. Restricting to these is what keeps an ordinary
    /// mistake elsewhere in the expression (a wrong argument count, an inaccessible member) from
    /// being reported as a naming problem.
    /// </summary>
    private static readonly HashSet<string> ShadowingErrorIds = new(StringComparer.Ordinal)
    {
        "CS0234",   // the type or namespace name X does not exist in the namespace Y
        "CS0426",   // the type name X does not exist in the type Y
        "CS0117",   // Y does not contain a definition for X
        "CS1061",   // Y does not contain a definition for X and no accessible extension method
    };

    // RS1032/RS2008 are aimed at authors shipping a Roslyn analyzer package, which this is not: the
    // descriptor exists only to carry a message DoodleSharp reports about the user's own project.
    // RS1032 objects that the message reads as two sentences without a trailing period — that
    // wording is the specified user-facing text, and it is quoted verbatim in README.md, F1 Help and
    // CHANGELOG.md, so "fixing" the punctuation here silently desynchronises three documents.
    // RS2008 wants an AnalyzerReleases.md tracking file, which only makes sense for a shipped
    // analyzer whose rule ids form a public contract.
#pragma warning disable RS1032, RS2008
    private static readonly DiagnosticDescriptor Descriptor = new(
        id: DiagnosticId,
        title: "Reserved DoodleSharp name",
        messageFormat: MessageFormat,
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS1032, RS2008

    /// <summary>
    /// Returns <paramref name="diagnostics"/> with every shadowing consequence replaced by one
    /// error per offending declaration, reported at the declaration. Returns the input untouched
    /// when nothing shadows — the overwhelmingly common case, which costs one syntax walk.
    /// </summary>
    public static IEnumerable<Diagnostic> Remap(IEnumerable<Diagnostic>? diagnostics, Compilation? compilation)
    {
        if (diagnostics == null)
            return Enumerable.Empty<Diagnostic>();
        if (compilation == null)
            return diagnostics;

        var all = diagnostics as IList<Diagnostic> ?? diagnostics.ToList();
        if (all.Count == 0)
            return all;

        // Cheap gate: unless the user actually declared a reserved name, there is nothing to do.
        var declarations = CollectReservedDeclarations(compilation);
        if (declarations.Count == 0)
            return all;

        var models = new Dictionary<SyntaxTree, SemanticModel>();
        var kept = new List<Diagnostic>(all.Count);
        var culprits = new List<string>();

        foreach (var diagnostic in all)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error &&
                ShadowingErrorIds.Contains(diagnostic.Id) &&
                TryFindShadowedName(diagnostic, declarations, compilation, models, out var name))
            {
                if (!culprits.Contains(name, StringComparer.Ordinal))
                    culprits.Add(name);
                continue;   // the use site is not the problem; drop it
            }

            kept.Add(diagnostic);
        }

        if (culprits.Count == 0)
            return all;

        // Report the cause first: it is the only one of these the user can act on.
        var remapped = new List<Diagnostic>(culprits.Count + kept.Count);
        foreach (var name in culprits)
            remapped.Add(Diagnostic.Create(Descriptor, declarations[name], name));
        remapped.AddRange(kept);
        return remapped;
    }

    /// <summary>
    /// Every declaration in user source whose name is a DoodleSharp keyword, mapped to the location
    /// of its identifier — the token the error should underline.
    /// </summary>
    private static Dictionary<string, Location> CollectReservedDeclarations(Compilation compilation)
    {
        var found = new Dictionary<string, Location>(StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            SyntaxNode root;
            try
            {
                root = tree.GetRoot();
            }
            catch
            {
                continue;   // an unparseable tree must not take the whole compile report down
            }

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    // Covers both block and file-scoped namespaces. Every segment counts: inside
                    // `namespace A.Mouse` the name Mouse binds to A.Mouse, and inside
                    // `namespace Mouse.A` it binds to Mouse — either way the API type is hidden.
                    case BaseNamespaceDeclarationSyntax ns:
                        foreach (var token in ns.Name.DescendantTokens())
                            if (token.IsKind(SyntaxKind.IdentifierToken))
                                Consider(found, token);
                        break;

                    case BaseTypeDeclarationSyntax type:            // class, struct, record, interface, enum
                        Consider(found, type.Identifier);
                        break;
                    case DelegateDeclarationSyntax del:
                        Consider(found, del.Identifier);
                        break;
                    case PropertyDeclarationSyntax property:
                        Consider(found, property.Identifier);
                        break;
                    case VariableDeclaratorSyntax variable:         // locals and fields
                        Consider(found, variable.Identifier);
                        break;
                    case ParameterSyntax parameter:
                        Consider(found, parameter.Identifier);
                        break;
                    case ForEachStatementSyntax forEach:
                        Consider(found, forEach.Identifier);
                        break;
                    case SingleVariableDesignationSyntax designation:   // `is X y`, `out var y`
                        Consider(found, designation.Identifier);
                        break;
                }
            }
        }

        return found;
    }

    private static void Consider(Dictionary<string, Location> found, SyntaxToken identifier)
    {
        var name = identifier.ValueText;
        if (string.IsNullOrEmpty(name) || found.ContainsKey(name))
            return;
        if (!ReservedNames.IsApiName(name))
            return;

        found[name] = identifier.GetLocation();
    }

    private static bool TryFindShadowedName(
        Diagnostic diagnostic,
        Dictionary<string, Location> declarations,
        Compilation compilation,
        Dictionary<SyntaxTree, SemanticModel> models,
        out string name)
    {
        name = string.Empty;

        var tree = diagnostic.Location.SourceTree;
        if (tree == null || !compilation.ContainsSyntaxTree(tree))
            return false;

        SyntaxNode root;
        try
        {
            root = tree.GetRoot();
        }
        catch
        {
            return false;
        }

        var span = diagnostic.Location.SourceSpan;
        if (span.Start > root.FullSpan.End)
            return false;

        // Many of these diagnostics are zero-width (note 51), so anchor on the token rather than
        // asking FindNode for a node that may not exist.
        var node = root.FindToken(span.Start).Parent;
        if (node == null)
            return false;

        // Climb out of the qualified-name / member-access chain, then walk back down its left edge:
        // the qualifier is what the lookup was performed against, and it is what may be shadowed.
        while (node.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax or AliasQualifiedNameSyntax)
            node = node.Parent;

        var qualifier = LeftmostIdentifier(node);
        if (qualifier == null)
            return false;

        var candidate = qualifier.Identifier.ValueText;
        if (!declarations.ContainsKey(candidate))
            return false;

        // The decisive test: did the qualifier bind to the user's own declaration? If it resolved to
        // the library type then nothing was shadowed and this really is an ordinary error.
        if (!ResolvesToSource(qualifier, tree, compilation, models))
            return false;

        name = candidate;
        return true;
    }

    private static SimpleNameSyntax? LeftmostIdentifier(SyntaxNode node)
    {
        var current = node;
        while (true)
        {
            switch (current)
            {
                case QualifiedNameSyntax qualified:
                    current = qualified.Left;
                    continue;
                case MemberAccessExpressionSyntax member:
                    current = member.Expression;
                    continue;
                case InvocationExpressionSyntax invocation:
                    current = invocation.Expression;
                    continue;
                case ElementAccessExpressionSyntax element:
                    current = element.Expression;
                    continue;
                case AliasQualifiedNameSyntax alias:
                    return alias.Alias;
                case GenericNameSyntax generic:
                    return generic;
                case IdentifierNameSyntax identifier:
                    return identifier;
                default:
                    return null;
            }
        }
    }

    private static bool ResolvesToSource(
        SimpleNameSyntax qualifier,
        SyntaxTree tree,
        Compilation compilation,
        Dictionary<SyntaxTree, SemanticModel> models)
    {
        if (!models.TryGetValue(tree, out var model))
        {
            try
            {
                model = compilation.GetSemanticModel(tree);
            }
            catch
            {
                return false;
            }

            models[tree] = model;
        }

        SymbolInfo info;
        try
        {
            info = model.GetSymbolInfo(qualifier);
        }
        catch
        {
            return false;
        }

        // A qualifier that fails to bind offers the real candidate under CandidateSymbols (note 44).
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (symbol == null)
            return false;

        // A namespace declared in source can also carry metadata locations when it merges with an
        // assembly's own namespace, so "any location in source" is the right test, not "all".
        return symbol.Locations.Any(location => location.IsInSource);
    }
}
