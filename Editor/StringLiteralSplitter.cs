using System;
using System.Collections.Generic;
using System.Text;

namespace DoodleSharp.Editor;

/// <summary>
/// Works out what Enter should do when the caret is inside a single-line string literal.
///
/// <para>
/// A raw newline between the quotes of <c>"…"</c> or <c>$"…"</c> is not legal C#, so inserting one —
/// which is what a plain Enter did — leaves the file not compiling. Instead the literal is closed
/// and continued: <c>$"hello " +</c> on the line the caret is on, <c>$"world"</c> on the next, caret
/// just inside the reopened quote.
/// </para>
///
/// <para>
/// Verbatim (<c>@"…"</c>) and raw (<c>"""…"""</c>) literals already accept newlines, so they are
/// left alone, as is the caret sitting inside an interpolation hole (<c>$"{ … }"</c>), where a
/// newline is legal too. Deciding which of those the caret is in needs a real scan — a search
/// backwards for the nearest quote gets comments, escapes and nesting wrong — so this walks the
/// text once from the start with an explicit context stack. It is pure, which is what makes it
/// testable without a window.
/// </para>
/// </summary>
public static class StringLiteralSplitter
{
    /// <summary>The edit Enter should make: insert <see cref="InsertedText"/> at <see cref="Offset"/>
    /// and leave the caret at the end of it.</summary>
    public readonly record struct StringSplit(int Offset, string InsertedText)
    {
        /// <summary>Where the caret belongs once the insertion is applied.</summary>
        public int CaretOffset => Offset + InsertedText.Length;
    }

    private enum ContextKind { Code, String }

    private sealed class Context
    {
        public ContextKind Kind;
        public bool Verbatim;      // @"…"
        public bool Interpolated;  // $"…"
        public bool Raw;           // """…"""
        public int ContentStart;   // first character after the opening quote(s)
        public int BraceDepth;     // interpolation holes only: nesting of { } inside the hole
    }

    /// <summary>
    /// Returns the edit to apply, or null when Enter should behave normally (the caret is not
    /// inside a splittable literal).
    /// </summary>
    /// <param name="text">The whole document text.</param>
    /// <param name="caretOffset">Caret offset within <paramref name="text"/>.</param>
    /// <param name="newLine">Line separator to emit; defaults to the environment's.</param>
    /// <param name="indentSize">Width of one indent level for the continuation line.</param>
    public static StringSplit? Compute(string text, int caretOffset, string? newLine = null, int indentSize = 4)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (caretOffset < 0 || caretOffset > text.Length) return null;

        var literal = FindEnclosingLiteral(text, caretOffset);
        if (literal == null) return null;

        newLine ??= Environment.NewLine;

        var (lineStart, indent) = LineIndent(text, caretOffset);

        // A line that already *begins* with a string is itself a continuation, so it keeps the
        // indent it has; anything else gets one level, the way a wrapped expression is written.
        var continuationIndent = BeginsWithStringLiteral(text, lineStart)
            ? indent
            : indent + new string(' ', indentSize);

        var reopen = literal.Interpolated ? "$\"" : "\"";
        var inserted = "\" +" + newLine + continuationIndent + reopen;

        return new StringSplit(caretOffset, inserted);
    }

    /// <summary>
    /// The literal the caret is inside, or null. Null covers "not in a literal at all" as well as
    /// the literals that must not be split (verbatim and raw, where a newline is already legal).
    /// </summary>
    private static Context? FindEnclosingLiteral(string text, int caretOffset)
    {
        var stack = new Stack<Context>();
        stack.Push(new Context { Kind = ContextKind.Code });

        int len = text.Length;
        int i = 0;

        while (i < caretOffset)
        {
            var ctx = stack.Peek();
            char c = text[i];

            if (ctx.Kind == ContextKind.Code)
            {
                if (c == '/' && i + 1 < len && text[i + 1] == '/')
                {
                    while (i < len && text[i] != '\n') i++;
                    continue;
                }

                if (c == '/' && i + 1 < len && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < len && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i = Math.Min(len, i + 2);
                    continue;
                }

                if (c == '\'')
                {
                    i++;
                    while (i < len && text[i] != '\'')
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }
                    i++;
                    continue;
                }

                // A string opener is any run of @ and $ followed by a quote — $", @", $@", @$", ".
                int j = i;
                bool verbatim = false, interpolated = false;
                while (j < len && (text[j] == '@' || text[j] == '$'))
                {
                    if (text[j] == '@') verbatim = true; else interpolated = true;
                    j++;
                }

                if (j < len && text[j] == '"')
                {
                    bool raw = j + 2 < len && text[j + 1] == '"' && text[j + 2] == '"';
                    int quoteLength = raw ? 3 : 1;
                    stack.Push(new Context
                    {
                        Kind = ContextKind.String,
                        Verbatim = verbatim,
                        Interpolated = interpolated,
                        Raw = raw,
                        ContentStart = j + quoteLength
                    });
                    i = j + quoteLength;
                    continue;
                }

                // Braces matter only inside an interpolation hole, where the closing one ends it.
                if (c == '{')
                {
                    ctx.BraceDepth++;
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    if (ctx.BraceDepth > 0) ctx.BraceDepth--;
                    else if (stack.Count > 1) stack.Pop();   // end of an interpolation hole
                    i++;
                    continue;
                }

                i++;
                continue;
            }

            // Inside a string literal.
            if (ctx.Interpolated && c == '{')
            {
                if (!ctx.Raw && i + 1 < len && text[i + 1] == '{') { i += 2; continue; }  // {{ escape
                stack.Push(new Context { Kind = ContextKind.Code });
                i++;
                continue;
            }

            if (ctx.Interpolated && c == '}' && !ctx.Raw && i + 1 < len && text[i + 1] == '}')
            {
                i += 2;
                continue;
            }

            if (!ctx.Verbatim && !ctx.Raw && c == '\\')
            {
                // Splitting between a backslash and the character it escapes would produce `\"`,
                // an escaped quote, not a closed literal. Refuse rather than corrupt the string.
                if (i + 2 > caretOffset) return null;
                i += 2;
                continue;
            }

            if (c == '"')
            {
                if (ctx.Raw)
                {
                    if (i + 2 < len && text[i + 1] == '"' && text[i + 2] == '"') { stack.Pop(); i += 3; continue; }
                    i++;
                    continue;
                }

                if (ctx.Verbatim && i + 1 < len && text[i + 1] == '"') { i += 2; continue; }  // "" escape

                stack.Pop();
                i++;
                continue;
            }

            // An unterminated single-line literal ends at the line break; without this every line
            // below a stray quote would look like string content.
            if (!ctx.Verbatim && !ctx.Raw && c == '\n')
            {
                stack.Pop();
                i++;
                continue;
            }

            i++;
        }

        var top = stack.Peek();
        if (top.Kind != ContextKind.String) return null;   // code, or an interpolation hole
        if (top.Verbatim || top.Raw) return null;          // a newline is already legal in these
        if (caretOffset < top.ContentStart) return null;   // caret is inside the opener itself

        return top;
    }

    /// <summary>Start offset of the caret's line, and the whitespace that line begins with.</summary>
    private static (int LineStart, string Indent) LineIndent(string text, int caretOffset)
    {
        int lineStart = caretOffset;
        while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;

        var indent = new StringBuilder();
        for (int i = lineStart; i < text.Length && (text[i] == ' ' || text[i] == '\t'); i++)
            indent.Append(text[i]);

        return (lineStart, indent.ToString());
    }

    /// <summary>True when the first non-whitespace thing on the line is a string literal.</summary>
    private static bool BeginsWithStringLiteral(string text, int lineStart)
    {
        int i = lineStart;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        while (i < text.Length && (text[i] == '@' || text[i] == '$')) i++;
        return i < text.Length && text[i] == '"';
    }
}
