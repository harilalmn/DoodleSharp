using System;
using System.Collections.Generic;

namespace C2VGeometry;

/// <summary>
/// One region of the drawing surface, and a node in the viewport tree.
///
/// <para>
/// A viewport whose <see cref="Rows"/> and <see cref="Columns"/> are both 1 is a <b>leaf</b>: it
/// owns a canvas, and shapes can be placed on it. Setting either above 1 turns it into a
/// <b>branch</b> — it is subdivided, its children become the leaves, and it no longer draws
/// anything itself. Because the rules are the same at every depth, an uneven layout — one large
/// view beside a column of small ones — is just a subdivided cell:
/// </para>
///
/// <code>
/// Viewports.Columns = 2;            // 1 x 2
/// Viewport right = Viewports[0][1];
/// right.Rows = 3;                   // subdivide only that cell
///
/// new VCircle(origin, 5).Place(Viewports[0][0]);
/// new VLine(a, b).Place(right[1][0]);
/// </code>
///
/// <para>
/// Indices are <b>0-based</b>, row first. A leaf's single cell is <i>itself</i>, so on the default
/// 1x1 layout <c>Viewports[0][0]</c> is the root — which is what makes a bare <c>Place()</c>, an
/// auto-registered shape, and <c>Place(Viewports[0][0])</c> all mean the same thing with no special
/// case anywhere.
/// </para>
/// </summary>
public sealed class Viewport
{
    /// <summary>
    /// The most rows or columns one viewport may be split into.
    ///
    /// <para>
    /// Not an arbitrary round number: every leaf owns a canvas, and a canvas carries a spatial
    /// index, its own render layers and a share of the process-wide pen cache, which is keyed
    /// partly by zoom level. The cap is what keeps a typo — or a <c>Rows</c> assigned from a
    /// computed value — from asking for thousands of them.
    /// </para>
    /// </summary>
    public const int MaxDimension = 8;

    /// <summary>
    /// Guards every mutation of the tree. Shared by all nodes, because a change to one node's
    /// children is a change to the shape of the whole tree, and the tree is read by the UI thread
    /// while user code mutates it from a thread-pool thread.
    /// </summary>
    internal static readonly object SyncRoot = new();

    private static Viewport _root = new(null, "Viewports");

    private int _rows = 1;
    private int _columns = 1;
    private Viewport[]? _children;

    // Sizes belong to the parent, not the cell: one row height is shared by every cell in that row,
    // so storing it per cell would give the same row several disagreeing answers.
    private ViewportLength[] _rowHeights = { ViewportLength.Star };
    private ViewportLength[] _columnWidths = { ViewportLength.Star };

    private Viewport(Viewport? parent, string path, int rowIndex = -1, int columnIndex = -1)
    {
        Parent = parent;
        Path = path;
        Depth = parent == null ? 0 : parent.Depth + 1;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
    }

    #region The tree

    /// <summary>
    /// The whole drawing surface, undivided until code divides it. This is what the unqualified
    /// name <c>Viewports</c> refers to — see <see cref="ViewportRoot"/> for why that spelling needs
    /// a holder class.
    /// </summary>
    public static Viewport Root => _root;

    /// <summary>
    /// Raised whenever the layout changes — a <see cref="Rows"/>, <see cref="Columns"/>,
    /// <see cref="Height"/> or <see cref="Width"/> assignment that actually changed something, or
    /// <see cref="Reset"/>.
    ///
    /// <para>
    /// <b>May arrive on a thread-pool thread</b>, because user code runs <c>Main()</c> off the UI
    /// thread. A host that rebuilds visuals in response has to marshal, and should coalesce: a
    /// script setting <c>Rows</c> and then <c>Columns</c> raises this twice for one intended layout.
    /// </para>
    /// </summary>
    public static event Action? LayoutChanged;

    /// <summary>
    /// Puts the layout back to a single undivided viewport.
    ///
    /// <para>
    /// Part of the host's between-runs reset, alongside rewinding shape ids — so the layout on
    /// screen is always the one the current source asks for, and deleting a
    /// <c>Viewports.Rows = 3</c> line takes effect on the next run rather than lingering until
    /// restart.
    /// </para>
    /// </summary>
    public static void Reset()
    {
        lock (SyncRoot)
        {
            _root.Detach();
            _root = new Viewport(null, "Viewports");
        }
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Every leaf, depth-first and left to right — the order the cells appear on screen.
    /// </summary>
    public static IReadOnlyList<Viewport> Leaves()
    {
        var found = new List<Viewport>();
        lock (SyncRoot) { _root.CollectLeaves(found); }
        return found;
    }

    private void CollectLeaves(List<Viewport> into)
    {
        if (_children == null) { into.Add(this); return; }
        foreach (var child in _children) child.CollectLeaves(into);
    }

    #endregion

    #region Shape of this node

    /// <summary>
    /// How many rows this viewport is split into. 1, the default, means it is a leaf and draws.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Below 1, or above <see cref="MaxDimension"/>.</exception>
    public int Rows
    {
        get => _rows;
        set => Resize(value, _columns, nameof(Rows));
    }

    /// <summary>
    /// How many columns this viewport is split into. 1, the default, means it is a leaf and draws.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Below 1, or above <see cref="MaxDimension"/>.</exception>
    public int Columns
    {
        get => _columns;
        set => Resize(_rows, value, nameof(Columns));
    }

    /// <summary>True when this viewport is undivided, and therefore owns a canvas of its own.</summary>
    public bool IsLeaf
    {
        get { lock (SyncRoot) { return _children == null; } }
    }

    /// <summary>The viewport this one subdivides, or null for the root.</summary>
    public Viewport? Parent { get; }

    /// <summary>0 for the root, 1 for its cells, and so on.</summary>
    public int Depth { get; }

    /// <summary>Which row of its parent this viewport occupies; -1 for the root.</summary>
    public int RowIndex { get; }

    /// <summary>Which column of its parent this viewport occupies; -1 for the root.</summary>
    public int ColumnIndex { get; }

    /// <summary>
    /// How tall this viewport's <b>row</b> is, in the same spelling XAML uses: <c>"*"</c> for a
    /// share of the space, <c>"3*"</c> for three shares, or a number for a fixed pixel height.
    /// Defaults to <c>"*"</c>, so an undivided grid shares the room equally.
    ///
    /// <para>
    /// It addresses the row, not the cell — every viewport in the same row reports and sets the same
    /// value, exactly as a XAML <c>RowDefinition</c> is shared by the cells sitting in it.
    /// <c>Viewports[1].Height</c> says the same thing more directly.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Read or set on the root, which has no parent to be sized within — it always fills the pane.
    /// </exception>
    /// <example>
    /// <code>
    /// Viewports.Rows = 2;
    /// Viewports[0][0].Height = "3*";     // the top row gets three quarters of the height
    /// </code>
    /// </example>
    public string Height
    {
        get => RequireParent("Height")._rowHeights[RowIndex].ToString();
        set => RequireParent("Height").SetRowHeight(RowIndex, ViewportLength.Parse(value));
    }

    /// <summary>
    /// How wide this viewport's <b>column</b> is, in the same spelling XAML uses. See
    /// <see cref="Height"/> — this is the same rule turned ninety degrees, and likewise addresses
    /// the column rather than the single cell.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Read or set on the root, which has no parent to be sized within.
    /// </exception>
    /// <example>
    /// <code>
    /// Viewports.Columns = 3;
    /// Viewports[0][2].Width = "4*";      // the last column gets four shares, the others one each
    /// </code>
    /// </example>
    public string Width
    {
        get => RequireParent("Width")._columnWidths[ColumnIndex].ToString();
        set => RequireParent("Width").SetColumnWidth(ColumnIndex, ViewportLength.Parse(value));
    }

    private Viewport RequireParent(string member) =>
        Parent ?? throw new InvalidOperationException(
            $"{this} has no parent, so it has no {member} of its own — the root viewport always " +
            $"fills the pane. Divide it first, then size its cells: Viewports.Rows = 2; " +
            $"Viewports[0][0].{member} = \"3*\";");

    /// <summary>
    /// The parsed height of one of the rows <b>this viewport contains</b> — the number rather than
    /// the string. <see cref="Height"/> is the spelling user code sets; this is what a host reads to
    /// lay the row out.
    ///
    /// <para>
    /// Note the subjects are opposites, which the similar names invite you to confuse:
    /// <see cref="Height"/> is the row this viewport <i>sits in</i>, over in its parent, while this
    /// is a row it <i>contains</i>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <c>0..Rows-1</c>, with the same message the indexer gives.
    /// </exception>
    public ViewportLength RowHeightAt(int row)
    {
        lock (SyncRoot)
        {
            if (row < 0 || row >= _rows) throw OutOfRange(isRow: true, row);
            return _rowHeights[row];
        }
    }

    /// <summary>
    /// The parsed width of one of the columns <b>this viewport contains</b>. The counterpart to
    /// <see cref="RowHeightAt"/>, with the same opposition of subject against <see cref="Width"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <c>0..Columns-1</c>, with the same message the indexer gives.
    /// </exception>
    public ViewportLength ColumnWidthAt(int column)
    {
        lock (SyncRoot)
        {
            if (column < 0 || column >= _columns) throw OutOfRange(isRow: false, column);
            return _columnWidths[column];
        }
    }

    internal void SetRowHeight(int row, ViewportLength length)
    {
        lock (SyncRoot)
        {
            if (row < 0 || row >= _rows || _rowHeights[row] == length) return;
            _rowHeights[row] = length;
        }
        LayoutChanged?.Invoke();
    }

    internal void SetColumnWidth(int column, ViewportLength length)
    {
        lock (SyncRoot)
        {
            if (column < 0 || column >= _columns || _columnWidths[column] == length) return;
            _columnWidths[column] = length;
        }
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// How this viewport is written in code — <c>"Viewports[0][1]"</c>.
    ///
    /// <para>
    /// Stable for a given position in a given layout, which is what lets a host match a rebuilt
    /// tree against the canvases it already holds and keep each cell's pan and zoom across a
    /// re-run. Node <i>identity</i> is what shapes are keyed on; this is for matching and display.
    /// </para>
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// False once a resize or a <see cref="Reset"/> removed this viewport from the tree. A detached
    /// viewport still answers questions about itself but is nowhere on screen, so a host holding
    /// shapes on one has to re-home them.
    /// </summary>
    public bool IsAttached { get; private set; } = true;

    private void Resize(int rows, int columns, string changed)
    {
        if (rows < 1 || rows > MaxDimension)
            throw new ArgumentOutOfRangeException(changed,
                $"{this}.Rows must be between 1 and {MaxDimension}; got {rows}.");
        if (columns < 1 || columns > MaxDimension)
            throw new ArgumentOutOfRangeException(changed,
                $"{this}.Columns must be between 1 and {MaxDimension}; got {columns}.");

        lock (SyncRoot)
        {
            // Assigning the value it already has must change nothing and raise nothing. Main() runs
            // again on every F5 and re-declares the layout, so treating a re-declaration as a change
            // would rebuild the grid — and throw away every cell's pan and zoom — on every run.
            if (rows == _rows && columns == _columns) return;

            var previous = _children;
            _rows = rows;
            _columns = columns;
            _rowHeights = Resized(_rowHeights, rows);
            _columnWidths = Resized(_columnWidths, columns);
            _children = rows == 1 && columns == 1 ? null : BuildChildren(rows, columns, previous);

            if (previous != null)
            {
                foreach (var old in previous)
                {
                    if (_children == null || Array.IndexOf(_children, old) < 0) old.Detach();
                }
            }
        }

        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// The new child grid, reusing the node already at each position.
    ///
    /// <para>
    /// Reuse matters because a resize is rarely a fresh start: subdividing a cell and then widening
    /// its parent must not discard the cell's own subdivision, and a host keying canvases by node
    /// identity would otherwise throw away pan and zoom for cells that did not move.
    /// </para>
    /// </summary>
    private Viewport[] BuildChildren(int rows, int columns, Viewport[]? previous)
    {
        var rebuilt = new Viewport[rows * columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var path = $"{Path}[{r}][{c}]";
                rebuilt[r * columns + c] = Find(previous, path) ?? new Viewport(this, path, r, c);
            }
        }
        return rebuilt;
    }

    /// <summary>
    /// Grows or shrinks a size array, keeping what the rows or columns that survive were set to and
    /// defaulting anything new to an equal share.
    /// </summary>
    private static ViewportLength[] Resized(ViewportLength[] previous, int count)
    {
        var next = new ViewportLength[count];
        for (var i = 0; i < count; i++)
        {
            next[i] = i < previous.Length ? previous[i] : ViewportLength.Star;
        }
        return next;
    }

    private static Viewport? Find(Viewport[]? previous, string path)
    {
        if (previous == null) return null;
        foreach (var old in previous)
        {
            if (string.Equals(old.Path, path, StringComparison.Ordinal)) return old;
        }
        return null;
    }

    /// <summary>Marks this node and everything under it as no longer part of the tree.</summary>
    private void Detach()
    {
        IsAttached = false;
        if (_children == null) return;
        foreach (var child in _children) child.Detach();
        _children = null;
    }

    #endregion

    #region Indexing

    /// <summary>
    /// The cells of one row of this viewport, so that <c>vp[row][column]</c> reads two-dimensionally.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The row does not exist. The message names the current size, because the usual cause is
    /// indexing before setting <see cref="Rows"/>.
    /// </exception>
    public ViewportRow this[int row]
    {
        get
        {
            if (row < 0 || row >= _rows) throw OutOfRange(isRow: true, row);
            return new ViewportRow(this, row);
        }
    }

    /// <summary>
    /// The cell at (row, column). A leaf's only cell is itself — that is what makes
    /// <c>Viewports[0][0]</c> the root on the default layout.
    /// </summary>
    internal Viewport Cell(int row, int column)
    {
        if (row < 0 || row >= _rows) throw OutOfRange(isRow: true, row);
        if (column < 0 || column >= _columns) throw OutOfRange(isRow: false, column);

        lock (SyncRoot)
        {
            return _children == null ? this : _children[row * _columns + column];
        }
    }

    private ArgumentOutOfRangeException OutOfRange(bool isRow, int index) =>
        new(isRow ? "row" : "column",
            $"{this}[{index}] is out of range. {Subject()} is {_rows} row{S(_rows)} x {_columns} " +
            $"column{S(_columns)} (valid rows 0..{_rows - 1}, columns 0..{_columns - 1}). " +
            $"Set {this}.{(isRow ? "Rows" : "Columns")} before placing.");

    private string Subject() => Parent == null ? "The layout" : "That viewport";

    private static string S(int n) => n == 1 ? "" : "s";

    /// <summary>
    /// The leaf this viewport's shapes are visible in <i>now</i>, following both directions a
    /// viewport can stop being drawable: subdivided (go down to the first cell) and removed by a
    /// resize or a reset (go up to the nearest surviving ancestor first).
    ///
    /// <para>
    /// Shrinking a layout is a legitimate thing to do to your own drawing, so shapes on a cell that
    /// no longer exists are re-homed rather than lost or thrown over — and the whole subtree being
    /// gone, which is what a reset leaves behind, falls back to the current root.
    /// </para>
    /// </summary>
    public Viewport ResolveVisible()
    {
        var node = this;
        while (!node.IsAttached && node.Parent != null) node = node.Parent;
        if (!node.IsAttached) node = Root;
        return node.FirstLeaf();
    }

    /// <summary>
    /// The leaf this viewport draws through when it has been subdivided: itself when it is a leaf,
    /// otherwise its first descendant leaf.
    ///
    /// <para>
    /// Resolved on demand rather than fixed up when a leaf is subdivided, so "the cell stayed where
    /// it was, it just got split" keeps holding however many times it is split again. See
    /// <see cref="ResolveVisible"/> for the version that also handles a viewport the layout removed.
    /// </para>
    /// </summary>
    public Viewport FirstLeaf()
    {
        var node = this;
        while (true)
        {
            Viewport[]? children;
            lock (SyncRoot) { children = node._children; }
            if (children == null) return node;
            node = children[0];
        }
    }

    #endregion

    /// <summary>How this viewport is written in code, e.g. <c>Viewports[0][1]</c>.</summary>
    public override string ToString() => Path;
}
