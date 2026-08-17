using System;
using System.Collections.Generic;
using System.Numerics;
using C2VGeometry;

namespace DoodleSharp.Rendering;

/// <summary>
/// Viewport culling over the whole scene: structure-of-arrays world bounds, a coarse uniform grid,
/// and a visibility bitset whose set bits come out in draw order.
///
/// <para>
/// <b>Why a grid and not a tree.</b> The QuadTree this replaces degenerates in exactly the case CAD
/// drawings are made of — a dense cluster bottoms out at <c>MaxDepth</c> and every leaf becomes a
/// linear scan, while items straddling a boundary get stored in up to four subtrees and
/// <c>Remove</c> walks the whole tree. A uniform grid inverts that: a dense cluster is simply one
/// long cell list, and a straight scan over a contiguous run of indices is the case a modern CPU is
/// best at. The <c>RayCaster</c> BVH is a better structure still for ray queries, but it is
/// snapshot-only, has no box query, and excludes <c>VPoint</c>/<c>VRay</c>/<c>VXLine</c> outright.
/// </para>
///
/// <para>
/// <b>Why z-order comes for free.</b> A painter's-algorithm renderer needs shapes back in draw
/// order, and sorting 100k indices per frame would cost more than the culling saves. Instead the
/// slot index <i>is</i> the draw order (the scene appends in order), the query sets bits in a
/// <see cref="ulong"/> array, and <see cref="Visible"/> walks the set bits with
/// <see cref="BitOperations.TrailingZeroCount"/> — ascending by construction, no sort, no
/// allocation.
/// </para>
///
/// <para>
/// The grid is stored CSR-style (one <c>_cellStart</c> offset array plus one packed
/// <c>_cellItems</c> array) rather than as a dictionary of lists, so a query touches two contiguous
/// arrays. Incremental adds go to a small overflow list that is always scanned, and trigger a
/// rebuild once they stop being small — the standard arrangement, and a good fit here because the
/// dominant paths are bulk: a Run rebuilds everything, and sketch mode rebuilds every frame.
/// </para>
/// </summary>
public sealed class SceneIndex
{
    /// <summary>
    /// Rebuild once incremental additions exceed this share of the indexed set. Low enough that the
    /// always-scanned overflow never dominates a query, high enough that dragging out a few dozen
    /// shapes doesn't rebuild on every mouse-up.
    /// </summary>
    private const double OverflowRebuildFraction = 0.05;
    private const int OverflowRebuildFloor = 512;

    /// <summary>Target average occupancy per cell. Chosen so a cell scan stays in one cache line's
    /// worth of indices while the grid itself stays small enough to clear cheaply.</summary>
    private const int TargetItemsPerCell = 4;

    private const int MaxGridDimension = 2048;

    /// <summary>
    /// A shape covering more cells than this is held out of the grid and tested on every query.
    /// Beyond roughly this many entries, duplicating a shape across cells costs more — in build
    /// time, memory and cache misses — than simply testing it each frame. Almost nothing in a real
    /// drawing reaches it; the ones that do are page borders and title blocks.
    /// </summary>
    private const int MaxCellsPerShape = 32;

    // ── Shapes, in slot order (slot == draw order) ───────────────────────────────────────────
    private IDrawable[] _shapes = Array.Empty<IDrawable>();
    private double[] _minX = Array.Empty<double>();
    private double[] _minY = Array.Empty<double>();
    private double[] _maxX = Array.Empty<double>();
    private double[] _maxY = Array.Empty<double>();
    private bool[] _live = Array.Empty<bool>();
    private int _slotCount;

    // ── The grid, CSR-style ──────────────────────────────────────────────────────────────────
    private int[] _cellStart = Array.Empty<int>();
    private int[] _cellItems = Array.Empty<int>();
    private int _cols, _rows;
    private double _originX, _originY, _cellSize;

    // Shapes that span too many cells to be worth binning, plus ones with non-finite bounds
    // (VRay, VXLine — semi-infinite by construction). Both are always considered.
    private readonly List<int> _oversize = new();
    private readonly List<int> _unbounded = new();

    // Added since the last rebuild; always scanned.
    private readonly List<int> _overflow = new();

    // ── Visibility ───────────────────────────────────────────────────────────────────────────
    private ulong[] _visible = Array.Empty<ulong>();

    /// <summary>
    /// Per-slot "last query that touched this" marker. A shape now appears in every cell its box
    /// overlaps, so a query meets the same slot several times; stamping keeps
    /// <see cref="ConsideredCount"/> honest and lets <see cref="QueryInto"/> emit each shape once
    /// without a HashSet. Comparing against a rising id avoids clearing the array per query.
    /// </summary>
    private int[] _stamp = Array.Empty<int>();
    private int _queryId;

    private int _visibleMinSlot;
    private int _visibleMaxSlot;
    private int _visibleCount;
    private int _consideredCount;

    /// <summary>Shapes currently indexed, including tombstoned slots.</summary>
    public int SlotCount => _slotCount;

    /// <summary>How many shapes the last query marked visible.</summary>
    public int VisibleCount => _visibleCount;

    /// <summary>
    /// How many shapes the last query had to test. The ratio against <see cref="VisibleCount"/> is
    /// the whole point of this class — when it equals the document size, culling is doing nothing.
    /// </summary>
    public int ConsideredCount => _consideredCount;

    /// <summary>The shape occupying a slot, or null if the slot is empty or tombstoned.</summary>
    public IDrawable? ShapeAt(int slot) =>
        (uint)slot < (uint)_slotCount && _live[slot] ? _shapes[slot] : null;

    /// <summary>
    /// The larger of a shape's cached world width and height, or <see cref="double.PositiveInfinity"/>
    /// for a shape with no meaningful bounds.
    ///
    /// <para>
    /// This exists so level-of-detail can size a shape without calling <c>GetBounds()</c>, which is
    /// uncached everywhere in the geometry library and allocates a <c>BoundingBox</c> plus two
    /// <c>VXYZ</c> objects on every call. The index already read those bounds once at build time;
    /// reading them back is an array lookup.
    /// </para>
    /// </summary>
    public double MaxExtentAt(int slot)
    {
        if ((uint)slot >= (uint)_slotCount || !_live[slot]) return 0;
        var w = _maxX[slot] - _minX[slot];
        var h = _maxY[slot] - _minY[slot];
        if (double.IsNaN(w) || double.IsNaN(h)) return double.PositiveInfinity;
        return w > h ? w : h;
    }

    /// <summary>The centre of a shape's cached bounds, for drawing it as a single mark.</summary>
    public void CentreAt(int slot, out double x, out double y)
    {
        x = (_minX[slot] + _maxX[slot]) * 0.5;
        y = (_minY[slot] + _maxY[slot]) * 0.5;
    }

    // ── Build ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Indexes a whole scene. O(n) plus one <c>GetBounds()</c> per shape — the only place bounds are
    /// read, which is what keeps <c>GetBounds()</c>'s allocation cost off the per-frame path.
    /// </summary>
    public void Rebuild(IReadOnlyList<IDrawable> shapes)
    {
        var n = shapes.Count;
        EnsureSlotCapacity(n);
        _slotCount = n;

        _oversize.Clear();
        _unbounded.Clear();
        _overflow.Clear();

        double worldMinX = double.MaxValue, worldMinY = double.MaxValue;
        double worldMaxX = double.MinValue, worldMaxY = double.MinValue;
        int bounded = 0;

        for (int i = 0; i < n; i++)
        {
            var drawable = shapes[i];
            _shapes[i] = drawable;
            _live[i] = true;

            if (!TryGetBounds(drawable, out var lo, out var hi))
            {
                _minX[i] = _minY[i] = double.NaN;
                _maxX[i] = _maxY[i] = double.NaN;
                _unbounded.Add(i);
                continue;
            }

            _minX[i] = lo.X; _minY[i] = lo.Y;
            _maxX[i] = hi.X; _maxY[i] = hi.Y;

            if (lo.X < worldMinX) worldMinX = lo.X;
            if (lo.Y < worldMinY) worldMinY = lo.Y;
            if (hi.X > worldMaxX) worldMaxX = hi.X;
            if (hi.Y > worldMaxY) worldMaxY = hi.Y;
            bounded++;
        }

        EnsureVisibleCapacity(n);
        _queryId = 0;
        Array.Clear(_stamp);

        if (bounded == 0)
        {
            _cols = _rows = 0;
            _cellStart = Array.Empty<int>();
            _cellItems = Array.Empty<int>();
            return;
        }

        ChooseGrid(worldMinX, worldMinY, worldMaxX, worldMaxY, bounded);
        BuildCells(n);
    }

    private void ChooseGrid(double minX, double minY, double maxX, double maxY, int bounded)
    {
        var width = Math.Max(maxX - minX, 1e-9);
        var height = Math.Max(maxY - minY, 1e-9);

        // Aim for TargetItemsPerCell on average, then clamp the grid so a pathological aspect ratio
        // or a huge shape count can't produce a cell array bigger than the scene itself.
        var targetCells = Math.Max(1.0, bounded / (double)TargetItemsPerCell);
        var cell = Math.Sqrt(width * height / targetCells);
        if (!double.IsFinite(cell) || cell <= 0) cell = Math.Max(width, height);

        _cellSize = cell;
        _cols = Math.Clamp((int)Math.Ceiling(width / cell) + 1, 1, MaxGridDimension);
        _rows = Math.Clamp((int)Math.Ceiling(height / cell) + 1, 1, MaxGridDimension);

        // Re-derive the cell size from the clamped dimensions so the grid still spans the world.
        _cellSize = Math.Max(width / _cols, height / _rows);
        if (!double.IsFinite(_cellSize) || _cellSize <= 0) _cellSize = 1;

        _originX = minX;
        _originY = minY;
    }

    /// <summary>
    /// Counting sort of slots into cells. Two passes over the shapes and no per-cell list objects —
    /// the whole grid is two int arrays.
    ///
    /// <para>
    /// <b>A shape is entered into every cell its box overlaps, not just the one holding a corner.</b>
    /// Binning by corner and then widening the query by one cell looks cheaper, but it silently
    /// breaks down the moment a shape is larger than a cell: the first benchmark run on a city grid
    /// examined 9,844 shapes to draw 45, because the street lines were exactly one block long — a
    /// hair over the cell size — so half the document ended up in the always-scanned oversize list.
    /// Duplication across cells costs a little memory and buys a cull that degrades gracefully
    /// instead of falling off a cliff at one particular shape size.
    /// </para>
    ///
    /// <para>
    /// Only genuinely huge shapes — ones spanning more cells than <see cref="MaxCellsPerShape"/> —
    /// still go to <c>_oversize</c>, where the cost of duplicating them would exceed the cost of
    /// testing them every frame.
    /// </para>
    /// </summary>
    private void BuildCells(int n)
    {
        var cellCount = _cols * _rows;
        if (_cellStart.Length < cellCount + 1)
            _cellStart = new int[cellCount + 1];
        else
            Array.Clear(_cellStart, 0, cellCount + 1);

        // Pass 1: count entries per cell.
        var entries = 0;
        for (int i = 0; i < n; i++)
        {
            if (!_live[i] || double.IsNaN(_minX[i])) continue;

            CellSpan(i, out var c0, out var r0, out var c1, out var r1);
            var span = (long)(c1 - c0 + 1) * (r1 - r0 + 1);

            if (span > MaxCellsPerShape)
            {
                _oversize.Add(i);
                continue;
            }

            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    _cellStart[r * _cols + c + 1]++;

            entries += (int)span;
        }

        for (int c = 0; c < cellCount; c++)
            _cellStart[c + 1] += _cellStart[c];

        if (_cellItems.Length < entries)
            _cellItems = new int[Math.Max(entries, 16)];

        // Pass 2: place, using a copy of the offsets as moving cursors.
        var cursor = cellCount <= 4096 ? stackalloc int[cellCount] : new int[cellCount];
        for (int c = 0; c < cellCount; c++) cursor[c] = _cellStart[c];

        for (int i = 0; i < n; i++)
        {
            if (!_live[i] || double.IsNaN(_minX[i])) continue;

            CellSpan(i, out var c0, out var r0, out var c1, out var r1);
            if ((long)(c1 - c0 + 1) * (r1 - r0 + 1) > MaxCellsPerShape) continue;

            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    _cellItems[cursor[r * _cols + c]++] = i;
        }
    }

    /// <summary>The inclusive cell rectangle a shape's bounding box covers.</summary>
    private void CellSpan(int slot, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Math.Clamp((int)((_minX[slot] - _originX) / _cellSize), 0, _cols - 1);
        c1 = Math.Clamp((int)((_maxX[slot] - _originX) / _cellSize), 0, _cols - 1);
        r0 = Math.Clamp((int)((_minY[slot] - _originY) / _cellSize), 0, _rows - 1);
        r1 = Math.Clamp((int)((_maxY[slot] - _originY) / _cellSize), 0, _rows - 1);
    }

    // ── Incremental change ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a shape. It goes on the always-scanned overflow list rather than into the grid,
    /// which keeps the operation O(1); once the overflow stops being negligible relative to the
    /// indexed set, <see cref="NeedsRebuild"/> goes true and the caller rebuilds.
    /// </summary>
    public void Add(IDrawable shape)
    {
        EnsureSlotCapacity(_slotCount + 1);
        EnsureVisibleCapacity(_slotCount + 1);

        var slot = _slotCount++;
        _shapes[slot] = shape;
        _live[slot] = true;

        if (TryGetBounds(shape, out var lo, out var hi))
        {
            _minX[slot] = lo.X; _minY[slot] = lo.Y;
            _maxX[slot] = hi.X; _maxY[slot] = hi.Y;
            _overflow.Add(slot);
        }
        else
        {
            _minX[slot] = _minY[slot] = double.NaN;
            _maxX[slot] = _maxY[slot] = double.NaN;
            _unbounded.Add(slot);
        }
    }

    /// <summary>
    /// Tombstones a shape. O(n) to find it — acceptable because removal is user-initiated and rare,
    /// and because the scene owns the id→slot mapping when one is needed.
    /// </summary>
    public bool Remove(IDrawable shape)
    {
        for (int i = 0; i < _slotCount; i++)
        {
            if (_live[i] && ReferenceEquals(_shapes[i], shape))
            {
                _live[i] = false;
                _shapes[i] = null!;
                return true;
            }
        }
        return false;
    }

    /// <summary>True when incremental changes have grown enough to be worth a rebuild.</summary>
    public bool NeedsRebuild =>
        _overflow.Count > Math.Max(OverflowRebuildFloor, _slotCount * OverflowRebuildFraction);

    public void Clear()
    {
        _slotCount = 0;
        _cols = _rows = 0;
        _oversize.Clear();
        _unbounded.Clear();
        _overflow.Clear();
        _visibleCount = 0;
        _consideredCount = 0;
    }

    // ── Query ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks every shape overlapping the world rectangle. Allocation-free; the results are read
    /// back through <see cref="Visible"/> in draw order.
    /// </summary>
    public void Query(double minX, double minY, double maxX, double maxY)
    {
        NextQuery();
        ClearVisible();
        _visibleCount = 0;
        _consideredCount = 0;

        // Always-visible: semi-infinite shapes have no meaningful bounds to test.
        foreach (var slot in _unbounded)
        {
            if (!_live[slot]) continue;
            if (_stamp[slot] == _queryId) continue;
            _stamp[slot] = _queryId;
            Mark(slot);
            _consideredCount++;
        }

        foreach (var slot in _oversize) TestAndMark(slot, minX, minY, maxX, maxY);
        foreach (var slot in _overflow) TestAndMark(slot, minX, minY, maxX, maxY);

        if (_cols == 0 || _rows == 0) return;

        GetCellRange(minX, minY, maxX, maxY, out var c0, out var r0, out var c1, out var r1);

        for (int r = r0; r <= r1; r++)
        {
            var rowBase = r * _cols;
            for (int c = c0; c <= c1; c++)
            {
                var cell = rowBase + c;
                var end = _cellStart[cell + 1];
                for (int k = _cellStart[cell]; k < end; k++)
                    TestAndMark(_cellItems[k], minX, minY, maxX, maxY);
            }
        }
    }

    /// <summary>
    /// Collects shapes overlapping a world rectangle into <paramref name="results"/>, in draw
    /// order, <b>without</b> touching the visibility bitset.
    ///
    /// <para>
    /// Snapping and hit-testing run from mouse-move, interleaved with rendering. Sharing the bitset
    /// would make the render's cull result depend on whether a tool happened to query first — a
    /// coupling that would work by accident today (the canvas re-queries at the top of every
    /// repaint) and break silently the moment that ordering changed.
    /// </para>
    /// </summary>
    public void QueryInto(double minX, double minY, double maxX, double maxY, List<IDrawable> results)
    {
        NextQuery();
        results.Clear();

        foreach (var slot in _unbounded)
        {
            if (!_live[slot] || _stamp[slot] == _queryId) continue;
            _stamp[slot] = _queryId;
            results.Add(_shapes[slot]);
        }

        foreach (var slot in _oversize) TestAndCollect(slot, minX, minY, maxX, maxY, results);
        foreach (var slot in _overflow) TestAndCollect(slot, minX, minY, maxX, maxY, results);

        if (_cols == 0 || _rows == 0) return;

        GetCellRange(minX, minY, maxX, maxY, out var c0, out var r0, out var c1, out var r1);

        for (int r = r0; r <= r1; r++)
        {
            var rowBase = r * _cols;
            for (int c = c0; c <= c1; c++)
            {
                var cell = rowBase + c;
                var end = _cellStart[cell + 1];
                for (int k = _cellStart[cell]; k < end; k++)
                    TestAndCollect(_cellItems[k], minX, minY, maxX, maxY, results);
            }
        }
    }

    private void TestAndCollect(int slot, double minX, double minY, double maxX, double maxY,
                                List<IDrawable> results)
    {
        if (!_live[slot]) return;
        if (_stamp[slot] == _queryId) return;   // already emitted for this query
        _stamp[slot] = _queryId;
        if (_minX[slot] > maxX || _maxX[slot] < minX) return;
        if (_minY[slot] > maxY || _maxY[slot] < minY) return;
        results.Add(_shapes[slot]);
    }

    /// <summary>
    /// The cell rectangle a world rectangle can draw candidates from. Widened one cell down and
    /// left because a shape is binned by its lower-left corner and can therefore reach forward into
    /// the view from the cell before it.
    /// </summary>
    private void GetCellRange(double minX, double minY, double maxX, double maxY,
                              out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Math.Clamp((int)Math.Floor((minX - _originX) / _cellSize), 0, _cols - 1);
        c1 = Math.Clamp((int)Math.Floor((maxX - _originX) / _cellSize), 0, _cols - 1);
        r0 = Math.Clamp((int)Math.Floor((minY - _originY) / _cellSize), 0, _rows - 1);
        r1 = Math.Clamp((int)Math.Floor((maxY - _originY) / _cellSize), 0, _rows - 1);
    }

    /// <summary>
    /// Advances the query stamp. Wrapping is handled by clearing rather than by letting the
    /// comparison alias — at one query per frame that is once every 2.6 million frames, but a stale
    /// stamp would drop shapes from a frame, and that is not a bug worth leaving to chance.
    /// </summary>
    private void NextQuery()
    {
        if (++_queryId == int.MaxValue)
        {
            Array.Clear(_stamp);
            _queryId = 1;
        }
    }

    private void TestAndMark(int slot, double minX, double minY, double maxX, double maxY)
    {
        if (!_live[slot]) return;
        if (_stamp[slot] == _queryId) return;   // already met this query, via another cell
        _stamp[slot] = _queryId;
        _consideredCount++;

        if (_minX[slot] > maxX || _maxX[slot] < minX) return;
        if (_minY[slot] > maxY || _maxY[slot] < minY) return;

        Mark(slot);
    }

    private void Mark(int slot)
    {
        var word = slot >> 6;
        var bit = 1UL << (slot & 63);
        if ((_visible[word] & bit) != 0) return;

        _visible[word] |= bit;
        _visibleCount++;
        if (slot < _visibleMinSlot) _visibleMinSlot = slot;
        if (slot > _visibleMaxSlot) _visibleMaxSlot = slot;
    }

    private void ClearVisible()
    {
        if (_visibleMaxSlot >= _visibleMinSlot && _visible.Length > 0)
        {
            var from = _visibleMinSlot >> 6;
            var to = Math.Min(_visibleMaxSlot >> 6, _visible.Length - 1);
            Array.Clear(_visible, from, to - from + 1);
        }
        _visibleMinSlot = int.MaxValue;
        _visibleMaxSlot = int.MinValue;
    }

    /// <summary>
    /// The visible slots, ascending — which is draw order, because the scene appends in draw order.
    /// A struct enumerator so <c>foreach</c> over it allocates nothing.
    /// </summary>
    public VisibleEnumerator Visible => new(this, ascending: true);

    /// <summary>
    /// The visible slots, descending — topmost first, which is what hit-testing wants.
    /// </summary>
    public VisibleEnumerator VisibleTopDown => new(this, ascending: false);

    public struct VisibleEnumerator
    {
        private readonly SceneIndex _owner;
        private readonly bool _ascending;
        private int _slot;
        private bool _started;

        internal VisibleEnumerator(SceneIndex owner, bool ascending)
        {
            _owner = owner;
            _ascending = ascending;
            _slot = -1;
            _started = false;
        }

        public readonly VisibleEnumerator GetEnumerator() => this;

        public readonly int Current => _slot;

        public bool MoveNext()
        {
            var o = _owner;
            if (o._visibleCount == 0 || o._visibleMaxSlot < o._visibleMinSlot) return false;

            if (!_started)
            {
                _started = true;
                _slot = _ascending ? o._visibleMinSlot - 1 : o._visibleMaxSlot + 1;
            }

            if (_ascending)
            {
                for (int s = _slot + 1; s <= o._visibleMaxSlot; s++)
                {
                    if ((o._visible[s >> 6] & (1UL << (s & 63))) != 0) { _slot = s; return true; }
                }
            }
            else
            {
                for (int s = _slot - 1; s >= o._visibleMinSlot; s--)
                {
                    if ((o._visible[s >> 6] & (1UL << (s & 63))) != 0) { _slot = s; return true; }
                }
            }
            return false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a shape's world bounds with its animation offset folded in, or reports the shape as
    /// unbounded — meaning "always considered visible".
    ///
    /// <para>
    /// <b><c>VRay</c> and <c>VXLine</c> are unbounded by type, not by measurement.</b> Their
    /// <c>GetBounds()</c> returns a finite box derived from <c>RenderExtent</c> (10,000 units), but
    /// <c>RenderCanvas.DrawXLine</c>/<c>DrawRay</c> extend them across the *visible viewport*
    /// instead. Culling on that box would make an infinite construction line disappear as soon as
    /// you panned past its extent — visible as a shape that vanishes for no reason, which is the
    /// hardest kind of rendering bug to attribute. A shape whose <c>GetBounds()</c> throws or
    /// yields non-finite numbers is treated the same way, so a bad shape degrades to "always drawn"
    /// rather than taking the frame down.
    /// </para>
    /// </summary>
    private static bool TryGetBounds(IDrawable drawable, out VXYZ lo, out VXYZ hi)
    {
        lo = hi = VXYZ.Zero;
        if (drawable is not Shape shape) return false;
        if (drawable is VRay or VXLine) return false;

        try
        {
            var b = shape.GetBounds();
            if (b?.Min == null || b.Max == null) return false;

            var ox = shape.OffsetX;
            var oy = shape.OffsetY;
            lo = new VXYZ(b.Min.X + ox, b.Min.Y + oy);
            hi = new VXYZ(b.Max.X + ox, b.Max.Y + oy);

            return double.IsFinite(lo.X) && double.IsFinite(lo.Y)
                && double.IsFinite(hi.X) && double.IsFinite(hi.Y);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureSlotCapacity(int needed)
    {
        if (_shapes.Length >= needed) return;

        var size = Math.Max(needed, Math.Max(16, _shapes.Length * 2));
        Array.Resize(ref _shapes, size);
        Array.Resize(ref _minX, size);
        Array.Resize(ref _minY, size);
        Array.Resize(ref _maxX, size);
        Array.Resize(ref _maxY, size);
        Array.Resize(ref _live, size);
    }

    private void EnsureVisibleCapacity(int slots)
    {
        var words = (slots + 63) >> 6;
        if (_visible.Length < words) Array.Resize(ref _visible, Math.Max(words, 4));
        if (_stamp.Length < slots) Array.Resize(ref _stamp, Math.Max(slots, 16));
    }
}
