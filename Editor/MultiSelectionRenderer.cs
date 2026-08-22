using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Brushes = System.Windows.Media.Brushes;
using Colors = System.Windows.Media.Colors;
using Rect = System.Windows.Rect;

namespace DoodleSharp.Editor;

/// <summary>
/// Renders multiple selection highlights and manages multi-cursor editing for Ctrl+D feature.
/// </summary>
public class MultiSelectionRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private readonly TextArea _textArea;
    private readonly List<TextSegment> _selections = new();

    // Track anchor and caret points for each selection (anchor stays fixed, caret moves)
    // Parallel to _selections - anchor[i] and caret[i] correspond to _selections[i]
    private readonly List<int> _anchors = new();
    private readonly List<int> _carets = new();

    /// <summary>
    /// Indent width used by Tab/Shift+Tab at every cursor. Matches the editor's
    /// <c>Options.IndentationSize</c> default and <see cref="EnterAtAllCursors"/>'s brace indent.
    /// </summary>
    public const int DefaultIndentSize = 4;

    // Selection highlight styling - matches the editor's selection color
    private static readonly Brush SelectionBrush;
    private static readonly Brush CaretBrush;

    static MultiSelectionRenderer()
    {
        // Use a color similar to VS Code's multi-selection highlight
        SelectionBrush = new SolidColorBrush(Color.FromArgb(100, 38, 79, 120));
        SelectionBrush.Freeze();
        CaretBrush = new SolidColorBrush(Colors.White);
        CaretBrush.Freeze();
    }

    public MultiSelectionRenderer(TextView textView)
    {
        _textView = textView;
        _textArea = textView.GetService(typeof(TextArea)) as TextArea
                    ?? throw new InvalidOperationException("TextArea not found");
    }

    public KnownLayer Layer => KnownLayer.Caret;

    /// <summary>
    /// Gets the list of additional selection segments (besides the main caret selection).
    /// </summary>
    public List<TextSegment> Selections => _selections;

    /// <summary>
    /// Adds a new selection segment. Anchor is at start, caret at end.
    /// </summary>
    public void AddSelection(int offset, int length)
    {
        _selections.Add(new TextSegment { StartOffset = offset, Length = length });
        _anchors.Add(offset); // Anchor at start
        _carets.Add(offset + length); // Caret at end
        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Clears all additional selections.
    /// </summary>
    public void ClearSelections()
    {
        if (_selections.Count > 0)
        {
            _selections.Clear();
            _anchors.Clear();
            _carets.Clear();
            _textView.InvalidateLayer(Layer);
        }
    }

    /// <summary>
    /// Checks if there are any additional selections.
    /// </summary>
    public bool HasSelections => _selections.Count > 0;

    /// <summary>
    /// Adds a cursor above the current cursor position(s).
    /// </summary>
    public bool AddCursorAbove()
    {
        var document = _textView.Document;
        var caret = _textArea.Caret;

        // Get all current cursor positions (main + additional)
        var cursorPositions = GetAllCursorPositions();

        // Find the topmost cursor
        var topmost = cursorPositions.OrderBy(p => p.Line).First();

        // Can't go above line 1
        if (topmost.Line <= 1)
            return false;

        // Get the target line
        int targetLine = topmost.Line - 1;
        var targetLineObj = document.GetLineByNumber(targetLine);

        // Calculate target column (clamped to line length)
        int targetColumn = Math.Min(topmost.Column, targetLineObj.Length + 1);
        int targetOffset = targetLineObj.Offset + targetColumn - 1;

        // Add the new cursor
        AddSelection(targetOffset, 0);
        return true;
    }

    /// <summary>
    /// Adds a cursor below the current cursor position(s).
    /// </summary>
    public bool AddCursorBelow()
    {
        var document = _textView.Document;
        var caret = _textArea.Caret;

        // Get all current cursor positions (main + additional)
        var cursorPositions = GetAllCursorPositions();

        // Find the bottommost cursor
        var bottommost = cursorPositions.OrderByDescending(p => p.Line).First();

        // Can't go below last line
        if (bottommost.Line >= document.LineCount)
            return false;

        // Get the target line
        int targetLine = bottommost.Line + 1;
        var targetLineObj = document.GetLineByNumber(targetLine);

        // Calculate target column (clamped to line length)
        int targetColumn = Math.Min(bottommost.Column, targetLineObj.Length + 1);
        int targetOffset = targetLineObj.Offset + targetColumn - 1;

        // Add the new cursor
        AddSelection(targetOffset, 0);
        return true;
    }

    /// <summary>
    /// Gets all cursor positions including main caret and additional selections.
    /// </summary>
    private List<(int Line, int Column, int Offset)> GetAllCursorPositions()
    {
        var document = _textView.Document;
        var positions = new List<(int Line, int Column, int Offset)>();

        // Add main caret
        var caretLine = _textArea.Caret.Line;
        var caretColumn = _textArea.Caret.Column;
        var caretOffset = _textArea.Caret.Offset;
        positions.Add((caretLine, caretColumn, caretOffset));

        // Add additional cursors
        foreach (var sel in _selections)
        {
            var loc = document.GetLocation(sel.EndOffset);
            positions.Add((loc.Line, loc.Column, sel.EndOffset));
        }

        return positions;
    }

    /// <summary>
    /// Inserts the same text at every cursor position (main + additional selections),
    /// replacing whatever each one has selected.
    /// </summary>
    public void InsertTextAtAllCursors(string text) => InsertTextsAtAllCursors(new[] { text });

    /// <summary>
    /// Replaces every cursor's selection with its own text. <paramref name="texts"/> is matched to
    /// the cursors in <em>document order</em>; a single-element list means "the same text at every
    /// cursor". This is the one place the document is edited for a multi-cursor insert, so the
    /// offset bookkeeping below is the only copy of it.
    /// </summary>
    public void InsertTextsAtAllCursors(IReadOnlyList<string> texts)
    {
        if (_selections.Count == 0 || texts.Count == 0) return;

        var document = _textView.Document;
        var mainSegment = _textArea.Selection.SurroundingSegment;

        // Collect every cursor: the additional ones, plus the main selection (SurroundingSegment is
        // null when there is no selection, in which case the bare caret is the cursor).
        var cursors = new List<(int Offset, int Length, bool IsMain)>();
        for (int i = 0; i < _selections.Count; i++)
            cursors.Add((_selections[i].StartOffset, _selections[i].Length, false));

        if (mainSegment != null)
            cursors.Add((mainSegment.Offset, mainSegment.Length, true));
        else
            cursors.Add((_textArea.Caret.Offset, 0, true));

        // Document order is what "one clipboard line per cursor" means, and it is what the
        // ascending offset-adjustment pass at the bottom assumes.
        cursors.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var edits = new List<(int Offset, int Length, string Text, bool IsMain)>(cursors.Count);
        for (int i = 0; i < cursors.Count; i++)
        {
            var text = texts.Count == 1 ? texts[0] : texts[Math.Min(i, texts.Count - 1)];
            edits.Add((cursors[i].Offset, cursors[i].Length, text, cursors[i].IsMain));
        }

        // Apply from the end backwards so an earlier edit never shifts a later one's offset.
        document.BeginUpdate();
        try
        {
            for (int i = edits.Count - 1; i >= 0; i--)
                document.Replace(edits[i].Offset, edits[i].Length, edits[i].Text);
        }
        finally
        {
            document.EndUpdate();
        }

        _selections.Clear();
        _anchors.Clear();
        _carets.Clear();

        int adjustment = 0;
        int mainNewOffset = 0;

        foreach (var (offset, length, text, isMain) in edits)
        {
            var newOffset = offset + adjustment + text.Length;
            adjustment += text.Length - length;

            if (isMain)
            {
                mainNewOffset = newOffset;
            }
            else
            {
                _selections.Add(new TextSegment { StartOffset = newOffset, Length = 0 });
                _anchors.Add(newOffset);
                _carets.Add(newOffset);
            }
        }

        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Inserts a newline with auto-indentation at all cursor positions.
    /// </summary>
    public void EnterAtAllCursors(bool autoIndent)
    {
        if (_selections.Count == 0) return;

        var document = _textView.Document;
        var mainSelection = _textArea.Selection;
        var mainSegment = mainSelection.SurroundingSegment;

        // Collect all cursor positions including main
        var allSelections = new List<(int Offset, int Length, bool IsMain)>();

        for (int i = 0; i < _selections.Count; i++)
        {
            allSelections.Add((_selections[i].StartOffset, _selections[i].Length, false));
        }

        if (mainSegment != null)
        {
            allSelections.Add((mainSegment.Offset, mainSegment.Length, true));
        }
        else
        {
            allSelections.Add((_textArea.Caret.Offset, 0, true));
        }

        // For each cursor, compute the text to insert (newline + indentation)
        var insertions = new List<(int Offset, int Length, string Text, bool IsMain)>();
        foreach (var (offset, length, isMain) in allSelections)
        {
            var insertText = "\n";
            if (autoIndent)
            {
                var line = document.GetLineByOffset(offset);
                var lineText = document.GetText(line.Offset, line.Length);
                var currentIndent = GetLineIndentation(lineText);
                var trimmedLine = lineText.TrimEnd();

                var newIndent = currentIndent;
                if (trimmedLine.EndsWith("{"))
                {
                    newIndent += "    ";
                }

                var afterCursor = document.GetText(offset, line.EndOffset - offset).Trim();
                if (trimmedLine.EndsWith("{") && afterCursor.StartsWith("}"))
                {
                    insertText = "\n" + newIndent + "\n" + currentIndent;
                }
                else
                {
                    insertText = "\n" + newIndent;
                }
            }
            insertions.Add((offset, length, insertText, isMain));
        }

        // Sort descending by offset for safe replacement
        var sortedDesc = insertions.OrderByDescending(s => s.Offset).ToList();

        document.BeginUpdate();
        try
        {
            foreach (var (offset, length, text, _) in sortedDesc)
            {
                document.Replace(offset, length, text);
            }
        }
        finally
        {
            document.EndUpdate();
        }

        // Calculate new positions (ascending order)
        var sortedAsc = insertions.OrderBy(s => s.Offset).ToList();
        int adjustment = 0;

        _selections.Clear();
        _anchors.Clear();
        _carets.Clear();

        int mainNewOffset = 0;

        foreach (var (offset, length, text, isMain) in sortedAsc)
        {
            // Position cursor after first newline + indent (not after the closing brace line)
            int cursorAdvance;
            var firstNewline = text.IndexOf('\n', 1);
            if (firstNewline > 0)
            {
                // Between braces case: place cursor at end of middle line
                cursorAdvance = firstNewline;
            }
            else
            {
                cursorAdvance = text.Length;
            }

            var newOffset = offset + adjustment + cursorAdvance;
            adjustment += text.Length - length;

            if (isMain)
            {
                mainNewOffset = newOffset;
            }
            else
            {
                _selections.Add(new TextSegment { StartOffset = newOffset, Length = 0 });
                _anchors.Add(newOffset);
                _carets.Add(newOffset);
            }
        }

        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    private static string GetLineIndentation(string line)
    {
        var indent = new System.Text.StringBuilder();
        foreach (var c in line)
        {
            if (c == ' ' || c == '\t')
                indent.Append(c);
            else
                break;
        }
        return indent.ToString();
    }

    /// <summary>
    /// Handles backspace at all cursor positions.
    /// </summary>
    public void BackspaceAtAllCursors()
    {
        if (_selections.Count == 0) return;

        var document = _textView.Document;
        var mainSelection = _textArea.Selection;
        var mainSegment = mainSelection.SurroundingSegment;

        // Collect all selections including main with their indices
        var allSelections = new List<(int Offset, int Length, bool IsMain)>();

        for (int i = 0; i < _selections.Count; i++)
        {
            allSelections.Add((_selections[i].StartOffset, _selections[i].Length, false));
        }

        // Add main selection/caret position
        if (mainSegment != null)
        {
            allSelections.Add((mainSegment.Offset, mainSegment.Length, true));
        }
        else
        {
            allSelections.Add((_textArea.Caret.Offset, 0, true));
        }

        // Sort by offset descending for deletion
        var sortedDesc = allSelections.OrderByDescending(s => s.Offset).ToList();

        document.BeginUpdate();
        try
        {
            foreach (var (offset, length, _) in sortedDesc)
            {
                if (length > 0)
                {
                    // Delete selection
                    document.Remove(offset, length);
                }
                else if (offset > 0)
                {
                    // Delete character before cursor
                    document.Remove(offset - 1, 1);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }

        // Calculate new positions (process in ascending order)
        var sortedAsc = allSelections.OrderBy(s => s.Offset).ToList();
        int adjustment = 0;

        // Clear and rebuild selections with updated positions
        _selections.Clear();
        _anchors.Clear();
        _carets.Clear();

        int mainNewOffset = 0;

        foreach (var (offset, length, isMain) in sortedAsc)
        {
            int deleteAmount = length > 0 ? length : (offset > 0 ? 1 : 0);
            var newOffset = Math.Max(0, offset + adjustment - (length > 0 ? 0 : 1));
            adjustment -= deleteAmount;

            if (isMain)
            {
                mainNewOffset = newOffset;
            }
            else
            {
                _selections.Add(new TextSegment { StartOffset = newOffset, Length = 0 });
                _anchors.Add(newOffset);
                _carets.Add(newOffset);
            }
        }

        // Update main caret position
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Handles delete key at all cursor positions.
    /// </summary>
    public void DeleteAtAllCursors()
    {
        if (_selections.Count == 0) return;

        var document = _textView.Document;
        var mainSelection = _textArea.Selection;
        var mainSegment = mainSelection.SurroundingSegment;

        // Collect all selections including main with their indices
        var allSelections = new List<(int Offset, int Length, bool IsMain)>();

        for (int i = 0; i < _selections.Count; i++)
        {
            allSelections.Add((_selections[i].StartOffset, _selections[i].Length, false));
        }

        // Add main selection/caret position
        if (mainSegment != null)
        {
            allSelections.Add((mainSegment.Offset, mainSegment.Length, true));
        }
        else
        {
            allSelections.Add((_textArea.Caret.Offset, 0, true));
        }

        // Sort by offset descending for deletion
        var sortedDesc = allSelections.OrderByDescending(s => s.Offset).ToList();
        int originalTextLength = document.TextLength;

        document.BeginUpdate();
        try
        {
            foreach (var (offset, length, _) in sortedDesc)
            {
                if (length > 0)
                {
                    document.Remove(offset, length);
                }
                else if (offset < document.TextLength)
                {
                    document.Remove(offset, 1);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }

        // Calculate new positions (process in ascending order)
        var sortedAsc = allSelections.OrderBy(s => s.Offset).ToList();
        int adjustment = 0;
        int runningTextLength = originalTextLength;

        // Clear and rebuild selections with updated positions
        _selections.Clear();
        _anchors.Clear();
        _carets.Clear();

        int mainNewOffset = 0;

        foreach (var (offset, length, isMain) in sortedAsc)
        {
            int deleteAmount = length > 0 ? length : (offset < runningTextLength ? 1 : 0);
            var newOffset = Math.Max(0, offset + adjustment);
            adjustment -= deleteAmount;
            runningTextLength -= deleteAmount;

            if (isMain)
            {
                mainNewOffset = newOffset;
            }
            else
            {
                _selections.Add(new TextSegment { StartOffset = newOffset, Length = 0 });
                _anchors.Add(newOffset);
                _carets.Add(newOffset);
            }
        }

        // Update main caret position
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors left by one character, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsLeft()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            // If there's a selection, collapse to start; otherwise move left
            int newOffset = sel.Length > 0 ? sel.StartOffset : Math.Max(0, sel.StartOffset - 1);
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset; // Reset anchor to new position
            _carets[i] = newOffset;  // Reset caret to new position
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainNewOffset;
        if (mainSegment != null && mainSegment.Length > 0)
        {
            mainNewOffset = mainSegment.Offset;
        }
        else
        {
            mainNewOffset = Math.Max(0, _textArea.Caret.Offset - 1);
        }
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors right by one character, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsRight()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            // If there's a selection, collapse to end; otherwise move right
            int newOffset = sel.Length > 0 ? sel.EndOffset : Math.Min(document.TextLength, sel.StartOffset + 1);
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset; // Reset anchor to new position
            _carets[i] = newOffset;  // Reset caret to new position
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainNewOffset;
        if (mainSegment != null && mainSegment.Length > 0)
        {
            mainNewOffset = mainSegment.EndOffset;
        }
        else
        {
            mainNewOffset = Math.Min(document.TextLength, _textArea.Caret.Offset + 1);
        }
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections left by one character (Shift+Left behavior).
    /// </summary>
    public void ExtendAllSelectionsLeft()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret left
            int newCaret = Math.Max(0, caret - 1);
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            // Determine anchor vs caret based on caret position
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        int mainNewCaret = Math.Max(0, mainCaret - 1);
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections right by one character (Shift+Right behavior).
    /// </summary>
    public void ExtendAllSelectionsRight()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret right
            int newCaret = Math.Min(document.TextLength, caret + 1);
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            // Determine anchor vs caret based on caret position
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        int mainNewCaret = Math.Min(document.TextLength, mainCaret + 1);
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors up by one line, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsUp()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.StartOffset : sel.EndOffset;
            var loc = document.GetLocation(offset);

            int newOffset;
            if (loc.Line > 1)
            {
                var targetLine = document.GetLineByNumber(loc.Line - 1);
                int targetColumn = Math.Min(loc.Column, targetLine.Length + 1);
                newOffset = targetLine.Offset + targetColumn - 1;
            }
            else
            {
                newOffset = sel.StartOffset;
            }
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset; // Reset anchor
            _carets[i] = newOffset;  // Reset caret
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.Offset : _textArea.Caret.Offset;
        var mainLoc = document.GetLocation(mainOffset);

        if (mainLoc.Line > 1)
        {
            var targetLine = document.GetLineByNumber(mainLoc.Line - 1);
            int targetColumn = Math.Min(mainLoc.Column, targetLine.Length + 1);
            int mainNewOffset = targetLine.Offset + targetColumn - 1;
            _textArea.Caret.Offset = mainNewOffset;
            _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);
        }
        else
        {
            // Just collapse selection
            _textArea.Selection = Selection.Create(_textArea, mainOffset, mainOffset);
        }

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors down by one line, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsDown()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.EndOffset : sel.EndOffset;
            var loc = document.GetLocation(offset);

            int newOffset;
            if (loc.Line < document.LineCount)
            {
                var targetLine = document.GetLineByNumber(loc.Line + 1);
                int targetColumn = Math.Min(loc.Column, targetLine.Length + 1);
                newOffset = targetLine.Offset + targetColumn - 1;
            }
            else
            {
                newOffset = sel.EndOffset;
            }
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset; // Reset anchor
            _carets[i] = newOffset;  // Reset caret
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.EndOffset : _textArea.Caret.Offset;
        var mainLoc = document.GetLocation(mainOffset);

        if (mainLoc.Line < document.LineCount)
        {
            var targetLine = document.GetLineByNumber(mainLoc.Line + 1);
            int targetColumn = Math.Min(mainLoc.Column, targetLine.Length + 1);
            int mainNewOffset = targetLine.Offset + targetColumn - 1;
            _textArea.Caret.Offset = mainNewOffset;
            _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);
        }
        else
        {
            // Just collapse selection
            _textArea.Selection = Selection.Create(_textArea, mainOffset, mainOffset);
        }

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors to the beginning of their respective lines, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsHome()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.StartOffset : sel.StartOffset;
            var line = document.GetLineByOffset(offset);
            int newOffset = line.Offset;
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset;
            _carets[i] = newOffset;
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.Offset : _textArea.Caret.Offset;
        var mainLine = document.GetLineByOffset(mainOffset);
        int mainNewOffset = mainLine.Offset;
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors to the end of their respective lines, collapsing any selections.
    /// </summary>
    public void MoveAllCursorsEnd()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.EndOffset : sel.EndOffset;
            var line = document.GetLineByOffset(offset);
            int newOffset = line.EndOffset;
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset;
            _carets[i] = newOffset;
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.EndOffset : _textArea.Caret.Offset;
        var mainLine = document.GetLineByOffset(mainOffset);
        int mainNewOffset = mainLine.EndOffset;
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections to the beginning of their respective lines (Shift+Home behavior).
    /// </summary>
    public void ExtendAllSelectionsHome()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret to line start
            var line = document.GetLineByOffset(caret);
            int newCaret = line.Offset;
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        var mainLine = document.GetLineByOffset(mainCaret);
        int mainNewCaret = mainLine.Offset;
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections to the end of their respective lines (Shift+End behavior).
    /// </summary>
    public void ExtendAllSelectionsEnd()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret to line end
            var line = document.GetLineByOffset(caret);
            int newCaret = line.EndOffset;
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        var mainLine = document.GetLineByOffset(mainCaret);
        int mainNewCaret = mainLine.EndOffset;
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Number of live cursors: the additional ones plus the main caret.
    /// </summary>
    public int CursorCount => _selections.Count + 1;

    /// <summary>
    /// Pastes clipboard text at all cursor positions.
    /// </summary>
    public void PasteAtAllCursors()
    {
        if (_selections.Count == 0) return;

        string clipboardText;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;
            clipboardText = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardText))
                return;
        }
        catch
        {
            return;
        }

        // A multi-cursor copy joins one fragment per cursor with newlines, so pasting the joined
        // text back at every cursor pasted all four words into all four places. When the clipboard
        // holds exactly one line per cursor, hand each cursor its own line instead.
        var perCursor = SplitForCursors(clipboardText, CursorCount);
        if (perCursor != null)
            InsertTextsAtAllCursors(perCursor);
        else
            InsertTextAtAllCursors(clipboardText);
    }

    /// <summary>
    /// Splits clipboard text into one fragment per cursor, or returns null when it does not divide
    /// evenly (in which case the whole text belongs at every cursor). This is VS Code's
    /// <c>multiCursorPaste: spread</c> rule: the line count has to match the cursor count exactly.
    /// </summary>
    public static IReadOnlyList<string>? SplitForCursors(string clipboardText, int cursorCount)
    {
        if (cursorCount < 2 || string.IsNullOrEmpty(clipboardText)) return null;

        var lines = clipboardText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Copying whole lines leaves a trailing newline, and the empty tail it produces is not a
        // cursor's worth of text.
        if (lines.Length == cursorCount + 1 && lines[lines.Length - 1].Length == 0)
            lines = lines[..^1];

        return lines.Length == cursorCount ? lines : null;
    }

    /// <summary>
    /// Tab at every cursor: each one gets the spaces that carry it to the next tab stop, so the
    /// columns stay aligned. Without this the key fell through to AvalonEdit, which knows only
    /// about the main caret and indented (or, with a selection, outdented) the wrong lines.
    /// </summary>
    public void IndentAtAllCursors(int indentSize = DefaultIndentSize)
    {
        if (_selections.Count == 0) return;

        var document = _textView.Document;
        var texts = new List<string>();

        foreach (var offset in GetOrderedCursorStarts())
        {
            var line = document.GetLineByOffset(offset);
            var column = offset - line.Offset;
            texts.Add(new string(' ', indentSize - (column % indentSize)));
        }

        InsertTextsAtAllCursors(texts);
    }

    /// <summary>
    /// Shift+Tab at every cursor. Outdent is a *line* operation — it strips one indent level from
    /// the start of each line a cursor sits on — so two cursors on the same line strip it once.
    /// </summary>
    public void OutdentAtAllCursors(int indentSize = DefaultIndentSize)
    {
        if (_selections.Count == 0) return;

        var document = _textView.Document;

        // Everything the remap needs is read before the document changes, because the offsets it
        // reads from would move underneath it otherwise.
        var cursors = new List<(int Offset, int LineNumber, int LineStart, bool IsMain)>();
        foreach (var sel in _selections)
        {
            var l = document.GetLineByOffset(sel.EndOffset);
            cursors.Add((sel.EndOffset, l.LineNumber, l.Offset, false));
        }
        var mainLine = document.GetLineByOffset(_textArea.Caret.Offset);
        cursors.Add((_textArea.Caret.Offset, mainLine.LineNumber, mainLine.Offset, true));

        // One removal per line, keyed by line number so two cursors on a line strip it once.
        var removals = new Dictionary<int, (int Offset, int Length)>();
        foreach (var (_, lineNumber, lineStart, _) in cursors)
        {
            if (removals.ContainsKey(lineNumber)) continue;

            var line = document.GetLineByNumber(lineNumber);
            var lineText = document.GetText(line.Offset, line.Length);
            int remove = 0;
            if (lineText.Length > 0 && lineText[0] == '\t')
            {
                remove = 1;
            }
            else
            {
                while (remove < indentSize && remove < lineText.Length && lineText[remove] == ' ')
                    remove++;
            }

            if (remove > 0)
                removals[lineNumber] = (lineStart, remove);
        }

        if (removals.Count == 0) return;

        var ordered = removals.Values.OrderBy(r => r.Offset).ToList();

        document.BeginUpdate();
        try
        {
            for (int i = ordered.Count - 1; i >= 0; i--)
                document.Remove(ordered[i].Offset, ordered[i].Length);
        }
        finally
        {
            document.EndUpdate();
        }

        _selections.Clear();
        _anchors.Clear();
        _carets.Clear();

        int mainNewOffset = _textArea.Caret.Offset;

        foreach (var (offset, lineNumber, lineStart, isMain) in cursors)
        {
            // What was removed above this cursor's line shifts it wholesale; what was removed on
            // its own line shifts it only as far as the new line start.
            int before = ordered.Where(r => r.Offset < lineStart).Sum(r => r.Length);
            int own = removals.TryGetValue(lineNumber, out var r0) ? r0.Length : 0;
            int newOffset = Math.Max(offset - own, lineStart) - before;

            if (isMain)
            {
                mainNewOffset = newOffset;
            }
            else
            {
                _selections.Add(new TextSegment { StartOffset = newOffset, Length = 0 });
                _anchors.Add(newOffset);
                _carets.Add(newOffset);
            }
        }

        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// The offset each cursor's replacement starts at (its selection start, or the bare caret),
    /// in document order — the same order <see cref="InsertTextsAtAllCursors"/> matches texts to.
    /// </summary>
    private List<int> GetOrderedCursorStarts()
    {
        var starts = new List<int>();
        foreach (var sel in _selections)
            starts.Add(sel.StartOffset);

        var mainSegment = _textArea.Selection.SurroundingSegment;
        starts.Add(mainSegment?.Offset ?? _textArea.Caret.Offset);

        starts.Sort();
        return starts;
    }

    /// <summary>
    /// Joins the text of every selection (main + additional) in document order, separated by
    /// newlines. Empty (zero-length) cursors contribute nothing. Returns "" when nothing is
    /// actually selected.
    /// </summary>
    public string GetAllSelectionsText()
    {
        var document = _textView.Document;
        var segments = new List<(int Offset, int Length)>();

        var mainSeg = _textArea.Selection.SurroundingSegment;
        if (mainSeg != null && mainSeg.Length > 0)
            segments.Add((mainSeg.Offset, mainSeg.Length));

        foreach (var sel in _selections)
            if (sel.Length > 0)
                segments.Add((sel.StartOffset, sel.Length));

        if (segments.Count == 0) return string.Empty;

        var parts = segments
            .OrderBy(s => s.Offset)
            .Select(s => document.GetText(s.Offset, s.Length));
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Copies the text of all selections (main + additional), newline-joined in document order,
    /// to the clipboard. Returns false (leaving the clipboard untouched) when nothing is selected
    /// so the caller can fall back to the editor's default copy.
    /// </summary>
    public bool CopyAllSelections()
    {
        var text = GetAllSelectionsText();
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Cut equivalent of <see cref="CopyAllSelections"/>: copies all selections to the clipboard,
    /// then removes the selected text at every cursor. Returns false when nothing was selected.
    /// </summary>
    public bool CutAllSelections()
    {
        if (!CopyAllSelections()) return false;
        // Replacing every selection with "" deletes its content and collapses to a caret,
        // reusing InsertTextAtAllCursors' offset-adjustment bookkeeping.
        InsertTextAtAllCursors(string.Empty);
        return true;
    }

    /// <summary>
    /// Gets the word boundary to the left of the given offset.
    /// </summary>
    private int GetWordStartLeft(int offset)
    {
        var document = _textView.Document;
        if (offset <= 0) return 0;

        var text = document.Text;
        int pos = offset - 1;

        // Skip whitespace
        while (pos > 0 && char.IsWhiteSpace(text[pos]))
            pos--;

        if (pos <= 0) return 0;

        // Determine the character class at current position
        char c = text[pos];
        bool isWordChar = char.IsLetterOrDigit(c) || c == '_';

        // Move back through same character class
        while (pos > 0)
        {
            char prev = text[pos - 1];
            bool prevIsWordChar = char.IsLetterOrDigit(prev) || prev == '_';

            if (isWordChar != prevIsWordChar)
                break;

            pos--;
        }

        return pos;
    }

    /// <summary>
    /// Gets the word boundary to the right of the given offset.
    /// </summary>
    private int GetWordEndRight(int offset)
    {
        var document = _textView.Document;
        var text = document.Text;
        int length = text.Length;

        if (offset >= length) return length;

        int pos = offset;

        // Skip whitespace
        while (pos < length && char.IsWhiteSpace(text[pos]))
            pos++;

        if (pos >= length) return length;

        // Determine the character class at current position
        char c = text[pos];
        bool isWordChar = char.IsLetterOrDigit(c) || c == '_';

        // Move forward through same character class
        while (pos < length)
        {
            char curr = text[pos];
            bool currIsWordChar = char.IsLetterOrDigit(curr) || curr == '_';

            if (isWordChar != currIsWordChar)
                break;

            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Moves all cursors left by one word (Ctrl+Left behavior).
    /// </summary>
    public void MoveAllCursorsWordLeft()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.StartOffset : sel.StartOffset;
            int newOffset = GetWordStartLeft(offset);
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset;
            _carets[i] = newOffset;
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.Offset : _textArea.Caret.Offset;
        int mainNewOffset = GetWordStartLeft(mainOffset);
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Moves all cursors right by one word (Ctrl+Right behavior).
    /// </summary>
    public void MoveAllCursorsWordRight()
    {
        var document = _textView.Document;

        // Move additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            var sel = _selections[i];
            int offset = sel.Length > 0 ? sel.EndOffset : sel.EndOffset;
            int newOffset = GetWordEndRight(offset);
            _selections[i] = new TextSegment { StartOffset = newOffset, Length = 0 };
            _anchors[i] = newOffset;
            _carets[i] = newOffset;
        }

        // Move main caret
        var mainSel = _textArea.Selection;
        var mainSegment = mainSel.SurroundingSegment;
        int mainOffset = mainSegment != null && mainSegment.Length > 0 ? mainSegment.EndOffset : _textArea.Caret.Offset;
        int mainNewOffset = GetWordEndRight(mainOffset);
        _textArea.Caret.Offset = mainNewOffset;
        _textArea.Selection = Selection.Create(_textArea, mainNewOffset, mainNewOffset);

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections left by one word (Ctrl+Shift+Left behavior).
    /// </summary>
    public void ExtendAllSelectionsWordLeft()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret to word start
            int newCaret = GetWordStartLeft(caret);
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        int mainNewCaret = GetWordStartLeft(mainCaret);
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>
    /// Extends all selections right by one word (Ctrl+Shift+Right behavior).
    /// </summary>
    public void ExtendAllSelectionsWordRight()
    {
        var document = _textView.Document;

        // Extend additional selections
        for (int i = 0; i < _selections.Count; i++)
        {
            int anchor = _anchors[i];
            int caret = _carets[i];

            // Move caret to word end
            int newCaret = GetWordEndRight(caret);
            _carets[i] = newCaret;

            // Create selection from anchor to new caret
            int start = Math.Min(anchor, newCaret);
            int end = Math.Max(anchor, newCaret);
            _selections[i] = new TextSegment { StartOffset = start, Length = end - start };
        }

        // Extend main selection
        var mainSel = _textArea.Selection;
        int mainAnchor, mainCaret;
        if (mainSel.IsEmpty)
        {
            mainAnchor = mainCaret = _textArea.Caret.Offset;
        }
        else
        {
            var seg = mainSel.SurroundingSegment;
            if (_textArea.Caret.Offset == seg.EndOffset)
            {
                mainAnchor = seg.Offset;
                mainCaret = seg.EndOffset;
            }
            else
            {
                mainAnchor = seg.EndOffset;
                mainCaret = seg.Offset;
            }
        }

        int mainNewCaret = GetWordEndRight(mainCaret);
        int mainStart = Math.Min(mainAnchor, mainNewCaret);
        int mainEnd = Math.Max(mainAnchor, mainNewCaret);
        _textArea.Selection = Selection.Create(_textArea, mainStart, mainEnd);
        _textArea.Caret.Offset = mainNewCaret;

        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_selections.Count == 0) return;

        for (int i = 0; i < _selections.Count; i++)
        {
            var segment = _selections[i];

            // Skip invalid segments
            if (segment.StartOffset < 0 || segment.EndOffset > textView.Document.TextLength)
                continue;

            if (segment.Length > 0)
            {
                // Draw selection highlight
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.DrawRectangle(SelectionBrush, null,
                        VisualLineRectHelpers.ClampToTextRow(rect, textView));
                }
            }

            // Draw caret line at the tracked caret position
            // Use GetVisualPosition to properly locate the caret (GetRectsForSegment returns empty for zero-length)
            int caretOffset = _carets[i];
            if (caretOffset < 0 || caretOffset > textView.Document.TextLength)
                continue;

            var visualLine = textView.GetVisualLine(textView.Document.GetLineByOffset(caretOffset).LineNumber);
            if (visualLine != null)
            {
                int relativeOffset = caretOffset - visualLine.FirstDocumentLine.Offset;
                var caretPos = visualLine.GetVisualPosition(relativeOffset, VisualYPosition.LineTop);
                var caretBottom = visualLine.GetVisualPosition(relativeOffset, VisualYPosition.LineBottom);

                // Adjust for scroll position
                caretPos = caretPos - textView.ScrollOffset;
                caretBottom = caretBottom - textView.ScrollOffset;

                // Draw a thin vertical line as caret
                var caretRect = new Rect(caretPos.X, caretPos.Y, 2, caretBottom.Y - caretPos.Y);
                drawingContext.DrawRectangle(CaretBrush, null, caretRect);
            }
        }
    }
}
