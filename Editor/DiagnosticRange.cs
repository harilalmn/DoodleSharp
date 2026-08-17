using System;
using ICSharpCode.AvalonEdit.Document;

namespace DoodleSharp.Editor;

/// <summary>
/// Maps a compiler diagnostic's line/column span onto a document range that can actually be
/// underlined.
///
/// <para>
/// The problem this solves: Roslyn reports "missing token" errors — a dropped <c>;</c>, <c>)</c> or
/// <c>(</c>, an incomplete expression — as <b>zero-width</b> spans at the point the token should
/// have been. A bare <c>for</c> produces seven diagnostics and every one of them is zero-width.
/// Code that requires a positive length before drawing a marker therefore renders nothing for the
/// most common class of mistake-while-typing, and the file looks clean while being unbuildable.
/// </para>
/// </summary>
public static class DiagnosticRange
{
    /// <summary>
    /// Resolves a range to underline. Returns false only when the position cannot be mapped into the
    /// document at all (a stale diagnostic against text that has since changed).
    /// </summary>
    /// <param name="document">The document the diagnostic refers to.</param>
    /// <param name="startLine">1-based start line.</param>
    /// <param name="startColumn">0-based start character, as Roslyn reports it.</param>
    /// <param name="endLine">1-based end line.</param>
    /// <param name="endColumn">0-based end character, as Roslyn reports it.</param>
    public static bool TryResolve(TextDocument document,
                                  int startLine, int startColumn,
                                  int endLine, int endColumn,
                                  out int offset, out int length)
    {
        offset = 0;
        length = 0;

        if (document == null) return false;
        if (startLine < 1 || startLine > document.LineCount) return false;

        var line = document.GetLineByNumber(startLine);
        offset = Math.Min(line.Offset + Math.Max(0, startColumn), line.EndOffset);

        if (endLine >= 1 && endLine <= document.LineCount)
        {
            var endLineObj = document.GetLineByNumber(endLine);
            var endOffset = Math.Min(endLineObj.Offset + Math.Max(0, endColumn), endLineObj.EndOffset);
            length = Math.Max(0, endOffset - offset);
        }

        if (length > 0) return true;

        // Empty span. Widen to something the user can see and hover.

        // 1. The identifier starting here, if any.
        var forward = offset;
        while (IsWordChar(document, forward)) forward++;
        if (forward > offset)
        {
            length = forward - offset;
            return true;
        }

        // 2. Otherwise the token just before — the usual case, because a missing token belongs
        //    *after* whatever was typed last.
        var back = offset;
        while (back > line.Offset && !char.IsWhiteSpace(document.GetCharAt(back - 1))) back--;
        if (back < offset)
        {
            length = offset - back;
            offset = back;
            return true;
        }

        // 3. Failing that, a single character — never the line break itself, which renders nothing.
        if (offset < line.EndOffset)
        {
            length = 1;
            return true;
        }
        if (offset > line.Offset)
        {
            offset--;
            length = 1;
            return true;
        }

        return false;
    }

    private static bool IsWordChar(TextDocument document, int index)
    {
        if (index < 0 || index >= document.TextLength) return false;
        var c = document.GetCharAt(index);
        return char.IsLetterOrDigit(c) || c == '_';
    }
}
