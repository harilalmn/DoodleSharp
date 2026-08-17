using System.Text.RegularExpressions;
using C2VGeometry;

namespace DoodleSharp.Canvas;

/// <summary>
/// Manages synchronization between shapes on the canvas and their source code.
/// </summary>
public class CodeSyncManager
{
    private static CodeSyncManager? _instance;
    public static CodeSyncManager Instance => _instance ??= new CodeSyncManager();

    // Maps shape ID to source information
    private readonly Dictionary<long, ShapeSourceInfo> _shapeSourceMap = new();

    /// <summary>
    /// Source code information for a shape.
    /// </summary>
    public class ShapeSourceInfo
    {
        public string FilePath { get; set; } = "";
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string CodeSnippet { get; set; } = "";
    }

    /// <summary>
    /// Registers a shape with its source code location.
    /// </summary>
    public void RegisterShape(Shape shape, string filePath, int startLine, int endLine, string code)
    {
        _shapeSourceMap[shape.Id] = new ShapeSourceInfo
        {
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            CodeSnippet = code
        };
    }

    /// <summary>
    /// Gets the source info for a shape.
    /// </summary>
    public ShapeSourceInfo? GetSourceInfo(Shape shape)
    {
        return _shapeSourceMap.TryGetValue(shape.Id, out var info) ? info : null;
    }

    /// <summary>
    /// Removes source tracking for a shape.
    /// </summary>
    public void UnregisterShape(Shape shape)
    {
        _shapeSourceMap.Remove(shape.Id);
    }

    /// <summary>
    /// Clears all shape-source mappings.
    /// </summary>
    public void Clear()
    {
        _shapeSourceMap.Clear();
    }

    /// <summary>
    /// Updates shape constructor in code with current property values.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="shape">The shape with updated properties.</param>
    /// <returns>Tuple of (new content, was found and updated).</returns>
    public static (string newContent, bool found) UpdateShapeCode(string content, Shape shape)
    {
        var shapeType = shape.GetType().Name;

        // Find "new VCircle(" and scan for balanced closing paren. Handles nested
        // parentheses like new VCircle(new VPoint(0, 5), 5).
        var openPattern = new Regex($@"new\s+{shapeType}\s*\(", RegexOptions.Singleline);
        var match = openPattern.Match(content);

        if (match.Success)
        {
            int depth = 1;
            int pos = match.Index + match.Length;
            while (pos < content.Length && depth > 0)
            {
                if (content[pos] == '(') depth++;
                else if (content[pos] == ')') depth--;
                pos++;
            }

            var newConstructor = CodeGenerator.GenerateConstructorCall(shape);
            var newContent = content.Substring(0, match.Index) + newConstructor + content.Substring(pos);
            return (newContent, true);
        }

        return (content, false);
    }

    /// <summary>
    /// Renames a shape variable throughout the code (whole-word replacement).
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="oldName">The old variable name.</param>
    /// <param name="newName">The new variable name.</param>
    /// <returns>Tuple of (new content, was found and updated).</returns>
    public static (string newContent, bool found) RenameShapeVariable(string content, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName)
            return (content, false);

        var escapedOld = Regex.Escape(oldName);
        var pattern = $@"\b{escapedOld}\b";
        var newContent = Regex.Replace(content, pattern, newName);
        return (newContent, newContent != content);
    }

    /// <summary>
    /// Updates or inserts a style property assignment line for a shape.
    /// Finds existing "varName.Property = value;" and updates it, or inserts after the constructor/last property line.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="shape">The shape whose property changed.</param>
    /// <param name="propertyName">The property name (e.g., "Color", "FillColor").</param>
    /// <param name="valueCode">The formatted value code (e.g., "\"Red\"", "2.0").</param>
    /// <returns>Tuple of (new content, was found/inserted).</returns>
    public static (string newContent, bool found) UpdateShapeStyleProperty(string content, Shape shape, string propertyName, string valueCode)
    {
        var varName = shape.Name;
        if (string.IsNullOrEmpty(varName)) return (content, false);

        var escapedVar = Regex.Escape(varName);

        // Try to find existing assignment: varName.PropertyName = ...;
        var existingPattern = new Regex($@"^(\s*){escapedVar}\.{propertyName}\s*=\s*[^;]*;", RegexOptions.Multiline);
        var existingMatch = existingPattern.Match(content);

        if (existingMatch.Success)
        {
            // Update existing line
            var indent = existingMatch.Groups[1].Value;
            var newLine = $"{indent}{varName}.{propertyName} = {valueCode};";
            var newContent = content.Substring(0, existingMatch.Index) + newLine + content.Substring(existingMatch.Index + existingMatch.Length);
            return (newContent, true);
        }

        // No existing assignment found - insert after the declaration statement for this variable.
        // Find "var varName =" or "VType varName =" to locate the declaration line.
        var declPattern = new Regex(
            $@"^(\s*)(?:var|{Regex.Escape(shape.GetType().Name)})\s+{escapedVar}\s*=\s*",
            RegexOptions.Multiline);
        var declMatch = declPattern.Match(content);

        if (declMatch.Success)
        {
            var indent = declMatch.Groups[1].Value;

            // Find the end of the declaration statement (the ';' accounting for nested parens)
            var stmtEnd = FindStatementEnd(content, declMatch.Index);
            if (stmtEnd < 0) return (content, false);

            // Find the end of this line (past the ';')
            var lineEnd = content.IndexOf('\n', stmtEnd);
            if (lineEnd < 0) lineEnd = content.Length;

            // Scan forward past any existing property assignments for this variable
            var insertPos = lineEnd;
            var varUsagePattern = new Regex($@"^\s*{escapedVar}\.\w+", RegexOptions.None);

            while (insertPos < content.Length)
            {
                var nextLineStart = insertPos;
                if (nextLineStart < content.Length && content[nextLineStart] == '\n') nextLineStart++;
                if (nextLineStart >= content.Length) break;

                var nextLineEnd = content.IndexOf('\n', nextLineStart);
                if (nextLineEnd < 0) nextLineEnd = content.Length;

                var nextLine = content.Substring(nextLineStart, nextLineEnd - nextLineStart);
                if (varUsagePattern.IsMatch(nextLine))
                {
                    insertPos = nextLineEnd;
                }
                else
                {
                    break;
                }
            }

            var newLine = $"\n{indent}{varName}.{propertyName} = {valueCode};";
            var result = content.Insert(insertPos, newLine);
            return (result, true);
        }

        return (content, false);
    }

    /// <summary>
    /// Finds the index of the ';' that ends a statement, spanning nested parentheses, brackets and
    /// braces — so an object initializer or a nested constructor call does not end it early.
    /// </summary>
    /// <remarks>
    /// String, character and comment content is skipped: a semicolon or bracket inside
    /// <c>"a;b"</c> or a trailing <c>// note)</c> must not be mistaken for structure.
    /// </remarks>
    /// <returns>Index of the terminating ';', or -1 if the statement is unterminated.</returns>
    private static int FindStatementEnd(string content, int startIndex)
    {
        int depth = 0;
        bool inString = false, inChar = false, inLineComment = false, inBlockComment = false;

        for (int i = startIndex; i < content.Length; i++)
        {
            char c = content[i];
            char next = i + 1 < content.Length ? content[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (inString)
            {
                if (c == '\\') i++;                  // skip the escaped character
                else if (c == '"') inString = false;
                continue;
            }
            if (inChar)
            {
                if (c == '\\') i++;
                else if (c == '\'') inChar = false;
                continue;
            }

            if (c == '/' && next == '/') { inLineComment = true; i++; }
            else if (c == '/' && next == '*') { inBlockComment = true; i++; }
            else if (c == '"') inString = true;
            else if (c == '\'') inChar = true;
            else if (c == '(' || c == '{' || c == '[') depth++;
            else if (c == ')' || c == '}' || c == ']') depth--;
            else if (c == ';' && depth <= 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds and removes shape code from file content.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <param name="shape">The shape to find and remove.</param>
    /// <returns>Tuple of (new content, was found).</returns>
    public static (string newContent, bool found) RemoveShapeCode(string content, Shape shape)
    {
        // Generate patterns to match the shape creation code
        var patterns = GetShapePatterns(shape);

        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.Multiline);
            var match = regex.Match(content);
            if (match.Success)
            {
                // The pattern only reaches the opening parenthesis of the constructor call; the
                // argument list is measured here by counting brackets. It used to be matched with
                // `\([^)]*\)`, which cannot span a nested call — so `new VRay(p1, new VXYZ(1, 2));`
                // never matched and deleting that shape from the canvas silently left its code
                // behind. Nested constructor arguments are the norm, not an edge case.
                var semicolon = FindStatementEnd(content, match.Index + match.Length - 1);
                if (semicolon < 0)
                    continue;
                var statementEnd = semicolon + 1;

                // Find all lines related to this shape (declaration + property assignments + Draw call)
                var varName = shape.Name;
                var startIndex = content.LastIndexOf('\n', match.Index);
                if (startIndex < 0) startIndex = 0;
                else startIndex++; // Move past the newline

                var endIndex = content.IndexOf('\n', statementEnd);
                if (endIndex < 0) endIndex = content.Length;
                else endIndex++; // Include the newline

                // If we have a variable name, also remove subsequent lines that use it
                if (!string.IsNullOrEmpty(varName))
                {
                    // Look for lines like: varName.Color = ...; varName.Draw();
                    var varUsagePattern = new Regex($@"^\s*{Regex.Escape(varName)}\.\w+.*;\s*$", RegexOptions.Multiline);
                    var searchStart = endIndex;

                    while (searchStart < content.Length)
                    {
                        var nextLineEnd = content.IndexOf('\n', searchStart);
                        if (nextLineEnd < 0) nextLineEnd = content.Length;

                        var nextLine = content.Substring(searchStart, nextLineEnd - searchStart);
                        if (varUsagePattern.IsMatch(nextLine))
                        {
                            endIndex = nextLineEnd < content.Length ? nextLineEnd + 1 : nextLineEnd;
                            searchStart = endIndex;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                var newContent = content.Remove(startIndex, endIndex - startIndex);
                return (newContent, true);
            }
        }

        return (content, false);
    }

    /// <summary>
    /// Patterns that match a shape's declaration <b>up to and including the constructor's opening
    /// parenthesis</b>. The argument list is measured separately by
    /// <see cref="FindStatementEnd"/>, because a regex cannot match balanced brackets and the
    /// arguments routinely contain nested calls (<c>new VCircle(new VXYZ(0, 0), 10)</c>).
    /// </summary>
    private static List<string> GetShapePatterns(Shape shape)
    {
        var patterns = new List<string>();
        var shapeType = shape.GetType().Name;
        var varName = shape.Name;

        // If we have a specific variable name, use it for precise matching
        if (!string.IsNullOrEmpty(varName))
        {
            var escapedName = Regex.Escape(varName);

            // var line3 = new VLine(   /   VLine line3 = new VLine(
            patterns.Add($@"var\s+{escapedName}\s*=\s*new\s+{shapeType}\s*\(");
            patterns.Add($@"{shapeType}\s+{escapedName}\s*=\s*new\s+{shapeType}\s*\(");
        }
        else
        {
            // Fallback when the shape has no variable name (inline, explicitly drawn).
            patterns.Add($@"new\s+{shapeType}\s*\(");
            patterns.Add($@"var\s+\w+\s*=\s*new\s+{shapeType}\s*\(");
            patterns.Add($@"{shapeType}\s+\w+\s*=\s*new\s+{shapeType}\s*\(");
        }

        return patterns;
    }


    /// <summary>
    /// Removes multiple shapes from code.
    /// </summary>
    public static string RemoveShapesCode(string content, IEnumerable<Shape> shapes)
    {
        var result = content;
        foreach (var shape in shapes)
        {
            var (newContent, found) = RemoveShapeCode(result, shape);
            if (found)
            {
                result = newContent;
            }
        }
        return result;
    }

    /// <summary>One file's text before and after a planned deletion.</summary>
    public sealed class PlannedEdit<T>
    {
        public PlannedEdit(T file, string before, string after)
        {
            File = file;
            Before = before;
            After = after;
        }

        public T File { get; }
        public string Before { get; }
        public string After { get; }
    }

    /// <summary>
    /// Works out which files need editing to delete <paramref name="shapes"/>, without applying
    /// anything. Returns the before/after text for each file that changes, plus the shapes whose
    /// declaration could not be found anywhere.
    ///
    /// <para>
    /// Separated from the host so the multi-file search is testable: a shape can be declared in any
    /// module, each shape must stop being searched for once it is found, and a shape that is never
    /// found has to be reported rather than counted as deleted. Capturing before/after here is also
    /// what makes the deletion undoable as a single step.
    /// </para>
    /// </summary>
    public static (List<PlannedEdit<T>> Edits, List<Shape> NotFound) PlanDeletion<T>(
        IEnumerable<(T File, string Content)> files, IEnumerable<Shape> shapes)
    {
        var edits = new List<PlannedEdit<T>>();
        var pending = shapes.ToList();

        foreach (var (file, before) in files)
        {
            if (pending.Count == 0) break;
            if (string.IsNullOrEmpty(before)) continue;

            var content = before;
            var remaining = new List<Shape>();

            foreach (var shape in pending)
            {
                var (updated, found) = RemoveShapeCode(content, shape);
                if (found) content = updated;
                else remaining.Add(shape);
            }

            if (!string.Equals(content, before, StringComparison.Ordinal))
                edits.Add(new PlannedEdit<T>(file, before, content));

            pending = remaining;
        }

        return (edits, pending);
    }
}
