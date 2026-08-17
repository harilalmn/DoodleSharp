namespace DoodleSharp.Editor;

/// <summary>
/// Provides fuzzy subsequence matching with scoring for IntelliSense filtering.
/// Typing "clr" will match "color" and "clear"; "VPt" matches "VPoint".
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

        // Quick check: pattern must be subsequence of candidate
        int pi = 0;
        for (int ci = 0; ci < candidateLower.Length && pi < patternLower.Length; ci++)
        {
            if (candidateLower[ci] == patternLower[pi])
                pi++;
        }
        if (pi < patternLower.Length)
            return null; // Not a subsequence

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
