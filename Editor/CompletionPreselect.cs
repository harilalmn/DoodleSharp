using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace DoodleSharp.Editor;

/// <summary>
/// Which row the completion list opens on.
///
/// <para>
/// The list itself is alphabetical and stays that way (note 115) — a list you scan by eye needs an
/// order with a rule you can see, and ranking by "what we guessed you meant" is exactly the order
/// that made a <c>VLine</c> member list open End, Flip, Move, Clone. But the *selection* is a
/// different question from the *order*: after <c>VXYZ p = new </c> there is only one type the
/// caret can be about, and leaving the highlight on <c>AccessViolationException</c> because the
/// alphabet put it first means the one useful key press (Tab) inserts the wrong thing. Visual
/// Studio draws the same line: it never reorders for the expected type, it selects it.
/// </para>
/// </summary>
public static class CompletionPreselect
{
    /// <summary>
    /// Index of the row to highlight when the window opens: the item whose name is exactly
    /// <paramref name="expectedType"/> if the list holds one, else the first row.
    /// Returns -1 for an empty list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A snippet at the top keeps the selection</b> (note 101). Snippets are poured in ahead of
    /// the symbols precisely so that item 0 is the one Tab expands, and that rule outranks this one:
    /// where snippets are offered at all the user is typing a statement, not naming a type, and the
    /// two cases do not overlap in practice — no snippets are offered after <c>new</c>.
    /// </para>
    /// <para>
    /// The name match is ordinal and exact. Case-insensitive would match nothing extra worth having
    /// (C# type names are the ones being compared) and a prefix match would preselect
    /// <c>VXYZ</c> for an expected <c>VX</c>.
    /// </para>
    /// </remarks>
    public static int IndexOf(IList<ICompletionData> items, string? expectedType)
    {
        if (items == null || items.Count == 0) return -1;
        if (string.IsNullOrEmpty(expectedType)) return 0;
        if (items[0] is SnippetCompletionData) return 0;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is SnippetCompletionData) continue;
            if (string.Equals(items[i].Text, expectedType, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }
}
