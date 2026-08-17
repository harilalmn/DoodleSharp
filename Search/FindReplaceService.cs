using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DoodleSharp.Search
{
    /// <summary>
    /// Options for search operations.
    /// </summary>
    public class SearchOptions
    {
        public string SearchText { get; set; } = string.Empty;
        public string ReplaceText { get; set; } = string.Empty;
        public bool UseRegex { get; set; }
        public bool CaseSensitive { get; set; }
        public bool WholeWord { get; set; }
        public SearchScope Scope { get; set; } = SearchScope.CurrentFile;
    }

    /// <summary>
    /// Scope of the search operation.
    /// </summary>
    public enum SearchScope
    {
        CurrentFile,
        EntireProject
    }

    /// <summary>
    /// Represents a single search result.
    /// </summary>
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public int LineNumber { get; set; }
        public int Column { get; set; }
        public int MatchLength { get; set; }
        public string LineContent { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;

        /// <summary>
        /// Returns display text for the result: [File:Line:Column] LineContent
        /// </summary>
        public string DisplayText => $"[{FileName}:{LineNumber}:{Column}] {LineContent.Trim()}";
    }

    /// <summary>
    /// Service for finding and replacing text in code files.
    /// </summary>
    public class FindReplaceService
    {
        private static RegexOptions BuildRegexOptions(SearchOptions options)
        {
            var flags = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            if (options.UseRegex)
                flags |= RegexOptions.Multiline;
            return flags;
        }

        /// <summary>
        /// Finds all matches in the given content.
        /// </summary>
        /// <param name="content">The text content to search in.</param>
        /// <param name="filePath">The file path for result metadata.</param>
        /// <param name="options">Search options.</param>
        /// <returns>List of search results.</returns>
        public List<SearchResult> FindAll(string content, string filePath, SearchOptions options)
        {
            var results = new List<SearchResult>();

            if (string.IsNullOrEmpty(options.SearchText) || string.IsNullOrEmpty(content))
                return results;

            try
            {
                var pattern = options.UseRegex
                    ? options.SearchText
                    : Regex.Escape(options.SearchText);

                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";

                var regexOptions = BuildRegexOptions(options);

                var regex = new Regex(pattern, regexOptions);
                var lines = content.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    foreach (Match match in regex.Matches(line))
                    {
                        results.Add(new SearchResult
                        {
                            FilePath = filePath,
                            LineNumber = i + 1,
                            Column = match.Index + 1,
                            MatchLength = match.Length,
                            LineContent = line,
                            MatchText = match.Value
                        });
                    }
                }
            }
            catch (RegexParseException)
            {
                // Invalid regex pattern - return empty results
            }

            return results;
        }

        /// <summary>
        /// Finds all matches across multiple files.
        /// </summary>
        /// <param name="files">Dictionary of file path to content.</param>
        /// <param name="options">Search options.</param>
        /// <returns>List of search results from all files.</returns>
        public List<SearchResult> FindInProject(Dictionary<string, string> files, SearchOptions options)
        {
            var results = new List<SearchResult>();

            foreach (var kvp in files)
            {
                var fileResults = FindAll(kvp.Value, kvp.Key, options);
                results.AddRange(fileResults);
            }

            return results;
        }

        /// <summary>
        /// Replaces all matches in the given content.
        /// </summary>
        /// <param name="content">The text content to search in.</param>
        /// <param name="options">Search and replace options.</param>
        /// <returns>The content with replacements made, or null if no matches found.</returns>
        public (string NewContent, int ReplacementCount) ReplaceAll(string content, SearchOptions options)
        {
            if (string.IsNullOrEmpty(options.SearchText) || string.IsNullOrEmpty(content))
                return (content, 0);

            try
            {
                var pattern = options.UseRegex
                    ? options.SearchText
                    : Regex.Escape(options.SearchText);

                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";

                var regexOptions = BuildRegexOptions(options);

                var regex = new Regex(pattern, regexOptions);

                // Count matches first
                var matches = regex.Matches(content);
                int count = matches.Count;

                if (count == 0)
                    return (content, 0);

                // Perform replacement
                var newContent = regex.Replace(content, options.ReplaceText ?? string.Empty);
                return (newContent, count);
            }
            catch (RegexParseException)
            {
                return (content, 0);
            }
        }

        /// <summary>
        /// Replaces the first match in the given content starting from the specified position.
        /// </summary>
        /// <param name="content">The text content to search in.</param>
        /// <param name="options">Search and replace options.</param>
        /// <param name="startIndex">Starting index for search.</param>
        /// <returns>Tuple of new content and the match info, or null if no match found.</returns>
        public (string NewContent, int MatchStart, int MatchLength)? ReplaceNext(
            string content, SearchOptions options, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(options.SearchText) || string.IsNullOrEmpty(content))
                return null;

            try
            {
                var pattern = options.UseRegex
                    ? options.SearchText
                    : Regex.Escape(options.SearchText);

                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";

                var regexOptions = BuildRegexOptions(options);

                var regex = new Regex(pattern, regexOptions);
                var match = regex.Match(content, startIndex);

                if (!match.Success)
                {
                    // Wrap around to the beginning
                    match = regex.Match(content, 0);
                    if (!match.Success)
                        return null;
                }

                var before = content.Substring(0, match.Index);
                var after = content.Substring(match.Index + match.Length);
                var replacement = options.ReplaceText ?? string.Empty;

                return (before + replacement + after, match.Index, replacement.Length);
            }
            catch (RegexParseException)
            {
                return null;
            }
        }

        /// <summary>
        /// Finds the next match in the content starting from the specified position.
        /// </summary>
        /// <param name="content">The text content to search in.</param>
        /// <param name="options">Search options.</param>
        /// <param name="startIndex">Starting index for search.</param>
        /// <returns>Tuple of match start and length, or null if no match found.</returns>
        public (int Start, int Length)? FindNext(string content, SearchOptions options, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(options.SearchText) || string.IsNullOrEmpty(content))
                return null;

            try
            {
                var pattern = options.UseRegex
                    ? options.SearchText
                    : Regex.Escape(options.SearchText);

                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";

                var regexOptions = BuildRegexOptions(options);

                var regex = new Regex(pattern, regexOptions);
                var match = regex.Match(content, startIndex);

                if (!match.Success)
                {
                    // Wrap around to the beginning
                    match = regex.Match(content, 0);
                    if (!match.Success)
                        return null;
                }

                return (match.Index, match.Length);
            }
            catch (RegexParseException)
            {
                return null;
            }
        }

        /// <summary>
        /// Finds the previous match in the content before the specified position.
        /// </summary>
        /// <param name="content">The text content to search in.</param>
        /// <param name="options">Search options.</param>
        /// <param name="startIndex">Starting index for backward search.</param>
        /// <returns>Tuple of match start and length, or null if no match found.</returns>
        public (int Start, int Length)? FindPrevious(string content, SearchOptions options, int startIndex)
        {
            if (string.IsNullOrEmpty(options.SearchText) || string.IsNullOrEmpty(content))
                return null;

            try
            {
                var pattern = options.UseRegex
                    ? options.SearchText
                    : Regex.Escape(options.SearchText);

                if (options.WholeWord)
                    pattern = $@"\b{pattern}\b";

                var regexOptions = BuildRegexOptions(options);

                var regex = new Regex(pattern, regexOptions | RegexOptions.RightToLeft);
                var match = regex.Match(content, startIndex);

                if (!match.Success)
                {
                    // Wrap around to the end
                    match = regex.Match(content, content.Length);
                    if (!match.Success)
                        return null;
                }

                return (match.Index, match.Length);
            }
            catch (RegexParseException)
            {
                return null;
            }
        }

        /// <summary>
        /// Validates if the search pattern is a valid regex.
        /// </summary>
        public bool IsValidPattern(SearchOptions options)
        {
            if (string.IsNullOrEmpty(options.SearchText))
                return false;

            if (!options.UseRegex)
                return true;

            try
            {
                _ = new Regex(options.SearchText);
                return true;
            }
            catch (RegexParseException)
            {
                return false;
            }
        }
    }
}
