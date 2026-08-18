using System;

namespace DoodleSharp.Editor;

/// <summary>
/// The keyboard rules that decide what an open completion list does when the next character arrives.
///
/// <para>
/// Pure functions, deliberately separated from the editor host so they can be tested: getting these
/// wrong is destructive rather than merely unhelpful, because the wrong answer silently rewrites
/// what the user typed.
/// </para>
/// </summary>
public static class CompletionInteraction
{
    /// <summary>
    /// True when typing <paramref name="c"/> should accept the highlighted completion.
    ///
    /// <para>
    /// <b>Space is deliberately excluded.</b> It is far more often the end of a keyword the list
    /// cannot contain than a choice being confirmed: the list opens while typing <c>new</c>, and
    /// committing on the following space replaced the keyword with the selected type
    /// (<c>new</c> → <c>VXYZ</c>). The same happened mid-argument, where <c>new VXYZ(10, </c>
    /// turned into <c>new VXYZ(10,Viz )</c>. Only characters that both end an identifier and imply
    /// the user has finished choosing commit.
    /// </para>
    /// </summary>
    public static bool Commits(char c) => c is '(' or '[' or '{' or ';' or ',' or ')';

    /// <summary>
    /// True when typing <paramref name="c"/> should accept the highlighted completion, given whether
    /// that highlighted item is a snippet.
    ///
    /// <para>
    /// <b>A snippet is never accepted by a commit character.</b> Committing one rewrites a single
    /// identifier into a multi-line construct with placeholders, so the cost of getting it wrong is
    /// far higher than for a symbol — and snippets now sort first and win the selection, which means
    /// <c>for(</c> would expand an entire loop around a parenthesis the user was typing by hand.
    /// The caller closes the list instead, leaving what was typed intact.
    ///
    /// <para>
    /// <b>Tab is the only key that expands a snippet.</b> Enter is excluded too, but not here — it
    /// reaches AvalonEdit's own accept path, so the exclusion lives in
    /// <c>SnippetCompletionData.Complete</c>, which returns early for <c>Key.Enter</c> and clears
    /// <c>Handled</c> so the editor performs its normal newline. Reading this file alone therefore
    /// suggests Enter still expands one; it does not. (A documentation pass drew exactly that wrong
    /// conclusion from the earlier wording.)
    /// </para>
    /// </para>
    /// </summary>
    public static bool Commits(char c, bool selectedItemIsSnippet)
        => !selectedItemIsSnippet && Commits(c);

    /// <summary>
    /// True when <paramref name="c"/> cannot continue an identifier, so an open list is no longer
    /// filtering anything meaningful and should be dismissed.
    /// </summary>
    public static bool Dismisses(char c) => !char.IsLetterOrDigit(c) && c != '_' && !Commits(c);

    /// <summary>
    /// Keywords after which a completion list is worth opening on the following space, because what
    /// comes next is necessarily a type name.
    /// </summary>
    public static bool IsPrimingKeyword(string? word) => word is "new" or "is" or "as";

    /// <summary>
    /// Pulls the identifier immediately before <paramref name="offset"/>, skipping any spaces
    /// between it and the caret. Returns null when there is no word there.
    /// </summary>
    public static string? WordBefore(string text, int offset)
    {
        if (text == null || offset < 0 || offset > text.Length) return null;

        var i = offset;
        while (i > 0 && text[i - 1] == ' ') i--;

        var end = i;
        while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) i--;

        return end == i ? null : text.Substring(i, end - i);
    }
}
