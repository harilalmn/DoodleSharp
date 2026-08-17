using System;
using System.Collections.Generic;

namespace DoodleSharp.Editor;

/// <summary>
/// Turns a "Generate method" quick action into the text to insert and the offset to insert it at.
///
/// <para>
/// Kept separate from the window that applies it so the interesting part — signature, indentation,
/// placement — is testable without a WPF editor. The offset itself comes from the semantic model
/// (see <c>RefactoringProvider.ResolveGenerationTarget</c>), not from scanning the text for braces.
/// </para>
/// </summary>
public static class MethodStubBuilder
{
    /// <summary>Everything the host needs to write a generated method.</summary>
    public readonly record struct Stub(string Text, int Offset, string? TargetFilePath, string TargetType)
    {
        /// <summary>False when the action carried no resolved insertion point.</summary>
        public bool IsValid => Offset >= 0;
    }

    /// <summary>
    /// Builds the stub text and placement from a quick action's data bag.
    /// </summary>
    /// <param name="data">The <c>QuickActionItem.Data</c> produced by the analyser.</param>
    /// <param name="newLine">Line ending to use, so generated code matches the host document.</param>
    public static Stub Build(IReadOnlyDictionary<string, string> data, string? newLine = null)
    {
        newLine ??= Environment.NewLine;

        if (!data.TryGetValue("MethodName", out var methodName) || string.IsNullOrWhiteSpace(methodName))
            return new Stub("", -1, null, "");

        var staticModifier = Get(data, "IsStatic") == "True" ? "static " : "";
        var parameters = Get(data, "Parameters") ?? "";
        var returnType = Get(data, "ReturnType");
        returnType = string.IsNullOrEmpty(returnType) ? "void" : returnType;

        var accessibility = Get(data, "TargetAccessibility");
        accessibility = string.IsNullOrEmpty(accessibility) ? "private" : accessibility;

        var indent = Get(data, "TargetIndent");
        indent = string.IsNullOrEmpty(indent) ? "        " : indent;

        // One more level for the body, matching whatever the surrounding file uses.
        var bodyIndent = indent + (indent.Contains('\t') ? "\t" : "    ");

        // Restores whatever preceded the closing brace when the stub is inserted at the brace itself
        // rather than at the start of its line (a one-liner such as `class X { }`). Empty otherwise.
        var closeIndent = Get(data, "TargetCloseIndent") ?? "";

        var text =
            $"{newLine}{indent}{accessibility} {staticModifier}{returnType} {methodName}({parameters}){newLine}" +
            $"{indent}{{{newLine}" +
            $"{bodyIndent}throw new NotImplementedException();{newLine}" +
            $"{indent}}}{newLine}{closeIndent}";

        var offset = int.TryParse(Get(data, "TargetInsertOffset"), out var parsed) ? parsed : -1;

        return new Stub(text, offset, Get(data, "TargetFilePath"), Get(data, "TargetType") ?? "");
    }

    private static string? Get(IReadOnlyDictionary<string, string> data, string key)
        => data.TryGetValue(key, out var value) ? value : null;
}
