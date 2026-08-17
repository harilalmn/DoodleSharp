using C2VGeometry;

namespace DoodleSharp.Project;

/// <summary>
/// Locates the value argument of the <c>GlobalParameters.Set(...)</c> call that declared a parameter
/// and computes the text span to replace, so an edit made in the Global Parameters panel is written
/// back into the user's source rather than living only for the current run.
///
/// <para>
/// The call site comes from <c>[CallerFilePath]</c>/<c>[CallerLineNumber]</c> recorded on
/// <see cref="Parameter.SourceFile"/>/<see cref="Parameter.SourceLine"/> at declaration time, so no
/// searching or guessing is needed — only argument-list scanning from that line. The scan respects
/// nesting, string literals, char literals and comments, so a value like
/// <c>Set("W", Math.Max(1, 2), min: 0)</c> is still identified correctly.
/// </para>
/// </summary>
public static class ParameterCodeWriter
{
    /// <summary>The span of source text holding a parameter's declared value.</summary>
    public readonly record struct ValueSpan(int Offset, int Length, string CurrentText);

    /// <summary>
    /// Finds the value argument of the <c>Set(...)</c> call that declared <paramref name="p"/>.
    /// Returns false when the call cannot be located unambiguously — the caller should then leave the
    /// source alone and keep the edit as a runtime override.
    /// </summary>
    public static bool TryFindValueSpan(string source, Parameter p, out ValueSpan span) =>
        TryFindValueSpan(source, p.Name, p.SourceLine, out span);

    /// <summary>
    /// Location-only overload: finds the value argument of the <c>Set("<paramref name="name"/>", …)</c>
    /// call at or just after <paramref name="sourceLine"/>. Kept independent of the registry so the
    /// scanner can be exercised directly.
    /// </summary>
    public static bool TryFindValueSpan(string source, string name, int sourceLine, out ValueSpan span)
    {
        span = default;
        if (string.IsNullOrEmpty(source) || sourceLine <= 0) return false;

        if (!TryGetLineStart(source, sourceLine, out var lineStart)) return false;

        // A declaration may wrap across lines, so scan forward from the declaring line rather than
        // limiting the search to it. Bound it so a mismatch cannot run away through the whole file.
        var searchEnd = Math.Min(source.Length, lineStart + 4000);

        int cursor = lineStart;
        while (cursor < searchEnd)
        {
            int setIdx = source.IndexOf("Set", cursor, searchEnd - cursor, StringComparison.Ordinal);
            if (setIdx < 0) return false;
            cursor = setIdx + 3;

            // Must be a whole identifier: `.Set` / `Set<...>` / `Set(`.
            if (setIdx > 0 && (char.IsLetterOrDigit(source[setIdx - 1]) || source[setIdx - 1] == '_'))
                continue;

            int open = SkipToOpenParen(source, cursor, searchEnd);
            if (open < 0) continue;

            if (!TryScanArguments(source, open, out var args) || args.Count < 2)
                continue;

            // First argument must be the string literal naming this parameter.
            var nameArg = source.Substring(args[0].Offset, args[0].Length).Trim();
            if (!TryReadStringLiteral(nameArg, out var declaredName)) continue;
            if (!string.Equals(declaredName, name, StringComparison.OrdinalIgnoreCase)) continue;

            var (offset, length) = TrimArgument(source, args[1]);
            span = new ValueSpan(offset, length, source.Substring(offset, length));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <paramref name="source"/> with the parameter's declared value replaced by its current
    /// value, or null when the call site could not be located.
    /// </summary>
    public static string? TryRewrite(string source, Parameter p) =>
        TryRewrite(source, p.Name, p.SourceLine, p.ToLiteral());

    /// <summary>Location-only overload of <see cref="TryRewrite(string, Parameter)"/>.</summary>
    public static string? TryRewrite(string source, string name, int sourceLine, string newLiteral)
    {
        if (!TryFindValueSpan(source, name, sourceLine, out var span)) return null;
        if (newLiteral == span.CurrentText) return source;
        return source.Remove(span.Offset, span.Length).Insert(span.Offset, newLiteral);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static bool TryGetLineStart(string source, int oneBasedLine, out int offset)
    {
        offset = 0;
        int line = 1;
        while (line < oneBasedLine)
        {
            int nl = source.IndexOf('\n', offset);
            if (nl < 0) return false;
            offset = nl + 1;
            line++;
        }
        return offset <= source.Length;
    }

    /// <summary>Skips an optional generic argument list, then lands on the opening parenthesis.</summary>
    private static int SkipToOpenParen(string source, int start, int end)
    {
        int i = start;
        while (i < end && char.IsWhiteSpace(source[i])) i++;

        if (i < end && source[i] == '<')
        {
            int depth = 0;
            while (i < end)
            {
                if (source[i] == '<') depth++;
                else if (source[i] == '>')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
                else if (source[i] is ';' or '\n') return -1;   // not a generic list after all
                i++;
            }
            while (i < end && char.IsWhiteSpace(source[i])) i++;
        }

        return i < end && source[i] == '(' ? i : -1;
    }

    /// <summary>
    /// Splits a parenthesised argument list into top-level argument spans, ignoring commas that sit
    /// inside nested brackets, strings, chars or comments.
    /// </summary>
    private static bool TryScanArguments(string source, int openParen, out List<(int Offset, int Length)> args)
    {
        args = new List<(int, int)>();
        int depth = 0;
        int argStart = openParen + 1;

        for (int i = openParen; i < source.Length; i++)
        {
            char c = source[i];

            switch (c)
            {
                case '"':
                    i = SkipStringLiteral(source, i);
                    if (i < 0) return false;
                    continue;
                case '\'':
                    i = SkipCharLiteral(source, i);
                    if (i < 0) return false;
                    continue;
                case '/' when i + 1 < source.Length && source[i + 1] == '/':
                    i = source.IndexOf('\n', i);
                    if (i < 0) return false;
                    continue;
                case '/' when i + 1 < source.Length && source[i + 1] == '*':
                    i = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (i < 0) return false;
                    i++;
                    continue;
                case '(' or '[' or '{':
                    depth++;
                    continue;
                case ')' or ']' or '}':
                    depth--;
                    if (depth == 0)
                    {
                        args.Add((argStart, i - argStart));
                        return true;
                    }
                    continue;
                case ',' when depth == 1:
                    args.Add((argStart, i - argStart));
                    argStart = i + 1;
                    continue;
            }
        }

        return false;
    }

    private static int SkipStringLiteral(string source, int quoteIndex)
    {
        // Verbatim strings have no escapes; regular ones do.
        bool verbatim = quoteIndex > 0 && source[quoteIndex - 1] == '@';
        for (int i = quoteIndex + 1; i < source.Length; i++)
        {
            char c = source[i];
            if (verbatim)
            {
                if (c != '"') continue;
                if (i + 1 < source.Length && source[i + 1] == '"') { i++; continue; }
                return i;
            }
            if (c == '\\') { i++; continue; }
            if (c == '"') return i;
            if (c == '\n') return -1;
        }
        return -1;
    }

    private static int SkipCharLiteral(string source, int quoteIndex)
    {
        for (int i = quoteIndex + 1; i < source.Length; i++)
        {
            if (source[i] == '\\') { i++; continue; }
            if (source[i] == '\'') return i;
            if (source[i] == '\n') return -1;
        }
        return -1;
    }

    /// <summary>
    /// Trims surrounding whitespace off an argument span, and skips a <c>name:</c> prefix so a call
    /// written with named arguments still resolves to the expression itself.
    /// </summary>
    private static (int Offset, int Length) TrimArgument(string source, (int Offset, int Length) arg)
    {
        int start = arg.Offset;
        int end = arg.Offset + arg.Length;
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;

        // `value: 10` → keep only `10`. A lone ':' cannot otherwise appear at depth 0 of an argument
        // except in a ternary, which always carries a '?' before it.
        int colon = source.IndexOf(':', start, end - start);
        if (colon > start)
        {
            var prefix = source.AsSpan(start, colon - start);
            bool isIdentifier = prefix.Length > 0 && !prefix.Contains('?');
            foreach (var ch in prefix)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_' && !char.IsWhiteSpace(ch)) { isIdentifier = false; break; }
            }
            if (isIdentifier && colon + 1 < end && source[colon + 1] != ':')
            {
                start = colon + 1;
                while (start < end && char.IsWhiteSpace(source[start])) start++;
            }
        }

        return (start, end - start);
    }

    private static bool TryReadStringLiteral(string text, out string value)
    {
        value = "";
        text = text.Trim();
        bool verbatim = text.StartsWith("@\"", StringComparison.Ordinal);
        if (!verbatim && !text.StartsWith('"')) return false;
        if (!text.EndsWith('"') || text.Length < (verbatim ? 3 : 2)) return false;

        var inner = verbatim ? text[2..^1] : text[1..^1];
        value = verbatim
            ? inner.Replace("\"\"", "\"")
            : inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        return true;
    }
}
