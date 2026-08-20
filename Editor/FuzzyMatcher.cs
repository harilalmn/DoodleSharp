namespace DoodleSharp.Editor;

/// <summary>
/// Provides fuzzy subsequence matching with scoring for IntelliSense filtering.
/// Typing "clr" will match "color" and "clear"; "VPt" matches "VPoint".
///
/// <para>
/// <b>The first character typed must begin a word in the candidate.</b> A plain subsequence test
/// is far too loose at the length that matters most — one character — because the filter runs on
/// every keystroke and the first keystroke is where the list is longest. Typing <c>x</c> inside an
/// argument list matched <c>AccessViolationException</c>, <c>BoundingBox</c>, <c>DoubleExtensions</c>
/// and every other name with an <c>x</c> buried in it, so the popup covered the code with hundreds
/// of rows, none of them wanted, and the alphabetically-first of them was the row Tab would take.
/// Requiring the anchor to sit at a word start — index 0, after <c>_</c> or <c>.</c>, or on a
/// camelCase hump — throws all of that out while keeping every real use: <c>clr</c> still finds
/// <c>Color</c>, <c>VPt</c> still finds <c>VPoint</c>, <c>x</c> finds the local <c>x</c>.
/// The later characters are still a free subsequence; only the anchor is constrained.
/// </para>
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Scores how well a pattern matches a candidate using subsequence matching.
    /// Returns null if the pattern is not a subsequence of the candidate.
    /// Higher scores indicate better matches.
    /// </summary>
    public static int? Score(string pattern, string candidate)
    {
        if (string.IsNullOrEmpty(pattern))
            return 0; // Empty pattern matches everything with neutral score

        if (string.IsNullOrEmpty(candidate))
            return null;

        var patternLower = pattern.ToLowerInvariant();
        var candidateLower = candidate.ToLowerInvariant();

        // The pattern must be a subsequence of the candidate AND start on a word boundary. See the
        // class summary for why the boundary half is not optional.
        if (!IsAnchoredSubsequence(patternLower, candidateLower, candidate))
            return null;

        int pi = 0;

        // Score the match using best-path greedy algorithm
        int score = 0;
        pi = 0;
        int lastMatchIndex = -1;

        for (int ci = 0; ci < candidate.Length && pi < pattern.Length; ci++)
        {
            if (char.ToLowerInvariant(candidate[ci]) == char.ToLowerInvariant(pattern[pi]))
            {
                // Exact prefix bonus: matching at position equal to pattern index
                if (ci == pi)
                    score += 10;

                // Word boundary bonus: start of word (after _, digit, or uppercase in camelCase)
                if (ci == 0 || candidate[ci - 1] == '_' || candidate[ci - 1] == '.' ||
                    (char.IsUpper(candidate[ci]) && ci > 0 && char.IsLower(candidate[ci - 1])))
                {
                    score += 8;
                }

                // Consecutive match bonus
                if (lastMatchIndex >= 0 && ci == lastMatchIndex + 1)
                    score += 5;

                // Exact case match bonus
                if (candidate[ci] == pattern[pi])
                    score += 1;

                // Gap penalty
                if (lastMatchIndex >= 0 && ci > lastMatchIndex + 1)
                    score -= (ci - lastMatchIndex - 1);

                lastMatchIndex = ci;
                pi++;
            }
        }

        // Bonus for exact match
        if (candidateLower == patternLower)
            score += 50;

        // Bonus for prefix match
        if (candidateLower.StartsWith(patternLower))
            score += 30;

        // Small penalty for candidate length (prefer shorter names)
        score -= (int)(candidate.Length * 0.1);

        return score;
    }

    /// <summary>
    /// True when <paramref name="candidate"/>[<paramref name="index"/>] starts a word: the first
    /// character, one following <c>_</c> or <c>.</c>, or a capital opening a camelCase hump.
    ///
    /// <para>
    /// The hump test accepts the last capital of an acronym run — the <c>S</c> of
    /// <c>HTTPServer</c>, the <c>P</c> of <c>VPoint</c> — by looking at whether a lowercase letter
    /// follows. Testing only "previous character is lowercase" misses both, which would mean typing
    /// <c>p</c> could not find <c>VPoint</c>, and nearly every type in this library is named that way.
    /// </para>
    /// </summary>
    internal static bool IsWordStart(string candidate, int index)
    {
        if (index <= 0)
            return index == 0;

        var previous = candidate[index - 1];
        if (previous == '_' || previous == '.')
            return true;

        var current = candidate[index];
        if (!char.IsUpper(current))
            return false;

        return !char.IsUpper(previous)
            || (index + 1 < candidate.Length && char.IsLower(candidate[index + 1]));
    }

    /// <summary>
    /// True when the pattern is a subsequence of the candidate whose <b>first</b> character lands on
    /// a word start.
    ///
    /// <para>
    /// Only the <i>earliest</i> qualifying anchor has to be tried. A later anchor's remaining text
    /// is a suffix of the earlier one's, so anything completable from the later one is completable
    /// from the earlier one too — searching the rest is provably wasted work, not a shortcut.
    /// </para>
    /// </summary>
    private static bool IsAnchoredSubsequence(string patternLower, string candidateLower, string candidate)
    {
        int anchor = -1;
        for (int ci = 0; ci < candidateLower.Length; ci++)
        {
            if (candidateLower[ci] == patternLower[0] && IsWordStart(candidate, ci))
            {
                anchor = ci;
                break;
            }
        }

        if (anchor < 0)
            return false;

        int pi = 1;
        for (int ci = anchor + 1; ci < candidateLower.Length && pi < patternLower.Length; ci++)
        {
            if (candidateLower[ci] == patternLower[pi])
                pi++;
        }

        return pi == patternLower.Length;
    }

    /// <summary>
    /// Returns the character indices in the candidate that match the pattern.
    /// Used for highlighting matched characters in the UI.
    /// Returns null if the pattern is not a subsequence.
    /// </summary>
    public static List<int>? GetMatchPositions(string pattern, string candidate)
    {
        if (string.IsNullOrEmpty(pattern))
            return new List<int>();

        if (string.IsNullOrEmpty(candidate))
            return null;

        var positions = new List<int>();
        int pi = 0;

        // First pass: prefer word-boundary matches
        var boundaryPositions = new List<int>();
        int bpi = 0;
        for (int ci = 0; ci < candidate.Length && bpi < pattern.Length; ci++)
        {
            if (char.ToLowerInvariant(candidate[ci]) == char.ToLowerInvariant(pattern[bpi]))
            {
                bool isBoundary = ci == 0 || candidate[ci - 1] == '_' || candidate[ci - 1] == '.' ||
                    (char.IsUpper(candidate[ci]) && ci > 0 && char.IsLower(candidate[ci - 1]));
                if (isBoundary || boundaryPositions.Count > 0)
                {
                    boundaryPositions.Add(ci);
                    bpi++;
                }
            }
        }

        if (bpi == pattern.Length)
            return boundaryPositions;

        // Fallback: simple greedy left-to-right match
        for (int ci = 0; ci < candidate.Length && pi < pattern.Length; ci++)
        {
            if (char.ToLowerInvariant(candidate[ci]) == char.ToLowerInvariant(pattern[pi]))
            {
                positions.Add(ci);
                pi++;
            }
        }

        return pi == pattern.Length ? positions : null;
    }
}
