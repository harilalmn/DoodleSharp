# TODO - DoodleSharp Future Development

## High Priority (P0) - Interactive Editing

### Shape Selection System
- [x] **Click to select** - Single shape selection on canvas click
- [x] **Multi-select with Shift** - Add to selection with Shift+Click
- [x] **Multi-select with Ctrl** - Toggle selection with Ctrl+Click
- [x] **Selection box** - Drag rectangle to select multiple shapes
- [x] **Crossing/Window selection** - Drag right = Window (fully inside), Drag left = Crossing (intersecting)
- [x] **Select All** - Ctrl+A to select all shapes
- [x] **Deselect** - Escape or click on empty canvas
- [x] **Visual feedback** - Highlight selected shapes with handles

### Shape Editing
- [x] **Control point handles** - Shape-specific control points for all 13 shape types
- [x] **Drag to modify** - Move control points to edit shape geometry (vertex, radius, curve handles)
- [x] **Move selected shapes** - Drag move handle to reposition
- [x] **Resize handles** - Corner/edge/radius handles for resizing
- [ ] **Rotation handle** - Rotate selected shapes
- [x] **Sync to code** - Update source code when shapes are edited

### Properties Panel
- [x] **Panel UI** - Floating/dockable panel showing shape properties
- [x] **Coordinate editing** - Edit X, Y, Width, Height, Radius, etc.
- [x] **Color picker** - Visual color selection for Stroke/Fill via ColorPickerDialog
- [x] **Thickness slider** - Adjust stroke thickness (0.5-20)
- [x] **Opacity slider** - Adjust shape opacity (0-100%)
- [x] **Visibility toggle** - Show/hide shapes with code sync (`IsVisible = false`)
- [x] **Name/ID display** - Show shape identifier and editable name with variable rename in code
- [x] **Style code sync** - All style property changes (Color, Fill, Weight, Opacity, Visible) persist as code lines
- [x] **Multi-selection** - Edit common style properties of multiple shapes
- [x] **Dock/Float toggle** - Switch between docked column and floating window
- [x] **Auto-deselect** - Selection cleared on Run and when clicking code editor

### Delete Shape
- [x] **Delete key** - Remove selected shapes
- [x] **Right-click context menu** - Delete option
- [x] **Code sync** - Remove corresponding code when shape deleted
- [x] **Undo support** - Restore deleted shapes (shape *and* its code, as one step; survives the auto-run the delete itself triggers)

---

## High Priority (P0) - Animation UI Enhancements

Core timeline playback is implemented; items below are advanced timeline UX polish.

### Timeline Panel
- [ ] **Timeline UI** - Visual timeline at bottom of window
- [ ] **Time ruler** - Displays time in seconds
- [ ] **Playhead** - Draggable position indicator
- [ ] **Shape tracks** - Row per animated shape
- [ ] **Keyframe markers** - Visual keyframe indicators
- [ ] **Duration handles** - Resize animation duration
- [ ] **Zoom timeline** - Zoom in/out on timeline

### Animation Preview
- [ ] **Play button** - Start animation playback
- [ ] **Pause button** - Pause at current frame
- [ ] **Stop button** - Reset to beginning
- [ ] **Loop toggle** - Enable/disable repeat
- [ ] **Speed control** - Playback speed slider (0.25x - 4x)
- [ ] **Frame stepping** - Step forward/backward one frame
- [ ] **Current time display** - Show current time position

---

## High Priority (P0) - Export Enhancements

### DXF Export
- [x] **DXF file format** - AutoCAD DXF R12/R14 format
- [ ] **Layer mapping** - Map shape types to DXF layers
- [ ] **Color mapping** - Map colors to DXF color indices
- [ ] **Line type support** - Solid, dashed, dotted
- [ ] **All shape types** - Export all supported shapes
- [ ] **Scale/units** - Configurable export units

### PDF Export
- [x] **Vector PDF** - PDF/A format for archiving
- [x] **Page size options** - A4, Letter, Custom
- [x] **Margins** - Configurable page margins
- [x] **Fit to page** - Auto-scale to fit
- [ ] **Multi-page** - Split large drawings across pages
- [ ] **Metadata** - Title, author, date

---

## Medium Priority (P1) - Geometry Operations

### Boolean Operations
- [x] **Union** - Combine two or more polygons
- [x] **Intersection** - Get overlapping area of polygons
- [x] **Difference** - Subtract one polygon from another
- [x] **XOR** - Symmetric difference
- [x] **Clipper library** - Use Clipper2 for robust operations
- [x] **API exposure** - VPolygon.Union(other), etc.
- [x] **Slice via Clipper too** - `VPolygon.Slice` now intersects two half-planes instead of walking the perimeter; area-preserving, and a concave cut crossed >2 times correctly returns 3+ pieces

### Array/Pattern Operations
- [ ] **Linear array** - Repeat shape along vector
  ```csharp
  shape.LinearArray(direction, count, spacing);
  ```
- [ ] **Rectangular array** - Grid of copies
  ```csharp
  shape.RectangularArray(rows, cols, rowSpacing, colSpacing);
  ```
- [ ] **Circular array** - Copies around center point
  ```csharp
  shape.CircularArray(center, count, angleSpan);
  ```
- [ ] **Path array** - Distribute along curve
  ```csharp
  shape.PathArray(curve, count, alignToPath);
  ```

---

## Medium Priority (P1) - Bug Fixes & Performance

### Bug Fixes
- [x] Fix console resize and scroll with multiline content (Auto row span layout issue)
- [ ] Test arc rendering for edge cases (360 arc, negative angles)
- [ ] Verify polygon rendering with self-intersecting polygons
- [ ] Test zoom limits at extreme scales

### Performance
- [ ] Optimize redraw for large shape counts (> 1000)
- [ ] Cache brushes instead of creating new ones per shape
- [ ] Implement shape culling for off-screen shapes
- [x] Spatial acceleration for ray queries (`RayCaster`: flat BVH + SAH split, allocation-free queries, parallel batch, in-place refit)

---

## Low Priority (P2) - Styling Enhancements

### Shape Styling
- [x] **Dash patterns** - Dashed/dotted lines via LineType property
  ```csharp
  line.LineType = LineType.Dashed; // Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden
  ```
- [ ] **Line caps** - Round, Square, Flat
- [ ] **Line joins** - Miter, Bevel, Round
- [ ] **Gradient fills** - Linear and radial gradients
- [ ] **Pattern fills** - Hatch patterns (diagonal, cross, dots)

### Canvas Features
- [x] **Snap to grid** - Snap coordinates to grid intersections (F9 toggle, adaptive spacing)
- [ ] **Ruler display** - Show rulers along canvas edges
- [ ] **Zoom slider** - Visual zoom control in UI
- [x] **Mini-map** - Overview of entire canvas (Ctrl+Shift+M toggle, syntax coloring, viewport indicator)

---

## Low Priority (P2) - Additional Features

### Export Features
- [ ] **Copy to clipboard** - As image or SVG

### Layer System
- [ ] **Named layers** - Create/rename layers
- [ ] **Visibility toggle** - Show/hide layers
- [ ] **Lock layers** - Prevent editing
- [ ] **Z-order** - Bring to front, send to back

### UI Enhancements
- [x] **Drag-and-drop in Project Explorer** - Move files and folders between directories via drag-and-drop
- [x] **Go to Location** - Context menu option to open file/folder location in Windows File Explorer
- [ ] **Customizable theme** - Light/Dark mode toggle
- [ ] **Full screen mode** - Maximize canvas
- [ ] **Undo/Redo for drawing** - Undo interactive drawing operations

---

## Technical Debt

### Code Quality
- [ ] Add XML documentation comments to all public APIs
- [x] Add unit tests for geometry calculations
- [ ] Add integration tests for script execution
- [ ] Implement proper MVVM pattern

### Architecture
- [x] Separate geometry library for reuse (C2VGeometry)
- [ ] Add dependency injection for testability
- [ ] Implement plugin system for custom shapes

---

## Completed Features

### Shapes (15 total)
- [x] VPoint, VLine, VCircle, VRectangle, VEllipse, VArc
- [x] VPolygon, VPolyline, VBezier, VSpline
- [x] VArrow, VText, VDimension, VGroup
- [x] Region (curve-bounded areas with holes, boolean ops)

### Drawing Tools (12 total)
- [x] All shape types with click-based creation
- [x] Code generation for drawn shapes

### Snap System (9 types)
- [x] Endpoint, Midpoint, Center, Intersection, Perpendicular, Nearest, Extension, Tangent, Grid

### Animation System
- [x] Draw, Move, Rotate, Flip, FadeIn, FadeOut animations
- [x] Timeline class with easing functions
- [x] ObjectPropertyAnimation<T> for animating numeric properties on any object
- [x] CompositionTarget.Rendering-based animation loop (vsync-aligned)

### Boolean Operations
- [x] Union, Intersection, Difference, XOR (Clipper2)
- [x] VPolygon.Union/Intersect/Difference/Xor methods
- [x] Region boolean ops (RegionBooleanOps)

### Export
- [x] PNG export
- [x] SVG export
- [x] GIF animation export
- [x] MP4 video export
- [x] DXF export (AutoCAD R12 ASCII)
- [x] PDF export (vector graphics)

### Editor
- [x] Syntax highlighting (C#)
- [x] Code completion and IntelliSense
- [x] Code folding and bracket matching
- [x] Code snippets
- [x] Visible realtime syntax-error squiggles — TextMarkerService draws a 2px-amplitude red zigzag tucked under the text baseline (was 1px in the inter-line gap and effectively invisible).

### Global Parameters
- [x] `GlobalParameters.Set/Get` registry shared by every module, surviving across runs (`C2VGeometry/Parameters/`)
- [x] Self-converting `ParamValue` reads (implicit double/bool/string/DateTime)
- [x] Idempotent declaration with override tracking + stale-declaration pruning
- [x] Global Parameters sidebar (`F6`) with slider/checkbox/text editors and min/max range boxes
- [x] Real-time canvas updates while dragging (resident-assembly re-execution)
- [x] Write-back of edited values into the `Set(...)` literal in source

### Canvas
- [x] Zoom and pan
- [x] Grid and axes
- [x] Coordinate display
- [x] Measuring tool
- [x] Snap to grid (F9 toggle)
- [x] Crossing/Window selection (drag direction determines mode)
- [x] Shape ID counter reset on each execution
- [x] Minimap with syntax coloring and viewport indicator

### Shape Editing
- [x] Shape-specific control points (13 shape types)
- [x] Drag control points to edit geometry
- [x] Code sync on drag end
- [x] Properties panel (floating/dockable)
- [x] Style property code sync (Color, FillColor, LineWeight, Opacity, IsVisible)
- [x] Variable rename from Properties panel
- [x] Auto-deselect on Run and editor click

### Curve Operations
- [x] `ICurve.SetBounds(start, end)` — in-place parameter-range trim for VLine/VArc/VEllipse/VPolyline/VBezier/VSpline (VBezier uses De Casteljau, VSpline dense-resamples); throws on VCircle/VPolygon/VRay/VXLine. Mirrored in C2VGeometry. 17 xUnit tests.

### Recently Completed (2026-08-19) — Viewports, and the End of Auto-Run
- [x] **`Viewports` — the canvas is a recursive grid of independent canvases** — a drawing that wanted two views of itself had none: everything landed in one coordinate space on one surface, and showing three variants side by side meant offsetting each into a different region of that space by hand (the `ConvexHull` sample still plants three algorithms at x = −300, 0 and +300). `Viewports.Rows`/`Columns` (both default 1) divide the pane; `shape.Place(Viewports[1][2])` chooses a cell; any leaf can be subdivided again, so an uneven layout is just a divided cell. Indices are 0-based row-first, and **a leaf's only cell is itself** — on the default 1×1 layout `Viewports[0][0]` *is* the root, which is what makes a bare `Place()`, an auto-registered shape and `Place(Viewports[0][0])` the same thing with no special case anywhere. Each cell pans and zooms on its own and keeps its view across an F5. The layout resets on every run, like shape ids, so the source always says what is on screen. CLAUDE.md notes 117–119, `Tests/Viewport*Tests.cs`.
- [x] **Row heights and column widths in XAML's grid-length spelling** — `Viewports[0].Height = "3*"`, `Viewports[0][2].Width = "4*"`, or a plain number for fixed device pixels. `Height` addresses the **row** and `Width` the **column**, exactly as a XAML `RowDefinition` is shared by the cells sitting in it. `"Auto"` is rejected *by name*, because a canvas has no natural size and an auto-sized cell would collapse to nothing and look like the drawing had vanished. Sizes survive a resize that keeps the row, and a size set before the grid grows is remembered rather than discarded.
- [x] **`Viewports` could not have been a static class** — C# has no static indexers (CS0720) and no namespace-level members, so a bare type name can never be indexed and `Viewports[0][0]` simply does not compile. It is a static *property* on `ViewportRoot`, reachable unqualified because the compiler injects `global using static C2VGeometry.ViewportRoot;` as its own syntax tree into every compilation — in the editor workspace too, or IntelliSense red-squiggles a name that compiles perfectly well. Not a line in the templates: a template only covers the files it generates, and a bare `Viewports` in a hand-written second file would fail. The tree carries an explicit `Encoding`, because the execute path emits a PDB and Roslyn refuses debug info for a `SourceText` without one — `CS8055`, which would have broken every run.
- [x] **Export covers the whole container, tiled as it appears** — PNG/GIF/MP4 capture the container rather than one canvas, so the tiling is free; SVG and PDF wrap each cell in a clipped group carrying that cell's own scale and pan, taken from the cell's `ViewportTransform` rather than recomputed. Tile rectangles come from **WPF layout**, never from rows-and-columns arithmetic, which is what makes nesting depth and star sizing irrelevant to every exporter. DXF has no viewport concept, so a divided drawing is flattened into model space and the console says plainly that coordinates become screen distances. **An undivided drawing exports through the path it always took** — for SVG that is load-bearing, since the historical export frames the *shapes* with padding while a tiled one reproduces the *view*.
- [x] **Auto-update and Auto-Draw removed; code runs on F5 / Run only** — both settings, the debounce timer, the second `TextChanged` handler and `_suppressAutoUpdate` are gone, from `SharedEditorController` too, because leaving its inert copy is how a deleted feature regrows (note 43). The three `Shape.AutoRegister = ApplicationSettings.Instance.AutoDraw` assignments were **deleted, not replaced with `= true`** — that would add a second writer to a flag owned by `AutoRegisterScope`, and an assignment landing inside a nested scope would defeat it. `AutoRunCodeAsync` survives as `RunSilentlyAsync` for its two Global Parameters callers. CLAUDE.md note 120, `Tests/AutoUpdateRemovalTests.cs`.
- [x] **`MouseInfo.Viewport`** names the cell an event came from. Handlers stay registered once for the whole drawing — a pointer has one `onmousemove`, and note 95's re-arming reasoning is unchanged — so this is how a handler tells cells apart.
- Worth keeping: **nine defects surfaced during the work, and none of them by a bug report.** Five were caught by tests as they were written — re-parenting a reused canvas would have thrown on the *second* layout change; `CS8055` would have broken every run; dividing the canvas threw away the view you were looking at; the first cell was registered twice; and making `SuspendAutoRegistration` public would have reserved "AutoRegisterScope" as a forbidden project name, caught by the guard written minutes earlier. Four more came from the two `docs-author` agents *reading* the code to document it: an inverted `Scale` doc repeated three times and already propagated into F1 Help, an orphaned `<summary>` that left one method undocumented and another with two, an unlocked read that could throw mid-layout, and two identical records disagreeing on a member type. Note 92's lesson again — divergence is invisible unless you read the paths side by side.
- Worth keeping: two of the new test classes passed alone and failed in a full run, because they mutate the process-wide viewport tree and were not in the `"CanvasState"` collection. Note 9's rule applies to `Viewport.Root` as much as to `Shape.DefaultRegistry`.

### Recently Completed (2026-08-19) — Masks, Draw Order, and an Alphabetical List
- [x] **`VText.Mask` — a background plate behind a label, on by default** — a label crossing the geometry it describes was hard to read. Every `VText` now fills a rectangle behind its glyphs in the **canvas background colour**, so it is invisible over empty canvas and reads as a clean interruption over anything it crosses. `MaskColor` is `null` by default, meaning *follow the canvas background*, resolved when the text is **drawn** rather than captured at construction — change the canvas colour and every default label follows, with nothing to re-run; the SVG and PDF exporters resolve it against the new `VText.CanvasBackgroundColor` seam. `MaskOffset` is padding as a *fraction of the text height* (clamped [0, 1], default 0.15), so a 2-unit and a 200-unit label keep the same breathing room. The mask never appears in the shape list and does not change `GetBounds()`. Known cost, documented rather than hidden: over a *filled* shape a masked label punches a canvas-coloured hole. Dimension labels opt out explicitly, so the three render backends stay identical. CLAUDE.md note 114, `Tests/TextMaskTests.cs`.
- [x] **`Shape.ZIndex` — global draw order; `BringAbove`/`SendBehind` removed** — the pair reordered the shape list *pairwise*, so the answer to "what is on top" depended on the order the calls were made in and was undone by the very next shape constructed; "this label is always on top" was not expressible. Order is now a property (ascending, ties keep creation order, negatives push a backdrop behind everything), derived in `CanvasRenderer.GetShapes()` — the one funnel the renderer, the cull index, hit-testing and the exporters already share — with the sort skipped entirely while every shape is at 0. `IShapeRegistry.MoveAbove`/`MoveBehind` became `NotifyOrderChanged`, which invalidates the cached order and bumps `RegistryVersion` so a `ZIndex` set inside a `Mouse` or `Frame` callback reaches the screen. CLAUDE.md note 113, `Tests/ZIndexTests.cs`.
- [x] **The IntelliSense list is alphabetical** — it was ranked by expected type, then match-score band, then type-vs-member, then scope, then *name length*, which is an order with no rule a reader can see: the members of a `VLine` opened End, Flip, Move, Clone, Scale, Start, Divide, Offset. Fuzzy matching still filters the list and still drives the bold match highlighting; nothing orders by it. Snippets still sort first and are still what `Tab` expands — and `SharedEditorController` was still *appending* them, note 101's bug never fixed in the parallel implementation, now corrected. The recently-used boost, inert once scores stopped ordering, was removed. CLAUDE.md note 115, `Tests/CompletionOrderingTests.cs`.
- [x] **`VXYZ.AngleTo` returns radians in a degrees library — deprecated** — reported as a text mask sitting "slightly off axis" on the half of a drawing whose lines pointed towards −X. `label.Rotate(label.Location, dir.AngleTo(VXYZ.BasisX))` on a reversed direction assigns **π as 3.14 degrees**: a hair crooked instead of a half turn, and invisible for +X where the answer is 0 in either unit. `AngleToRadians` is the implementation, `AngleToDegrees` the library-convention wrapper, and the unit-less name is `[Obsolete]` — deprecated rather than redefined, note 70's precedent, since changing its meaning would silently break every existing `Math.Cos(a.AngleTo(b))`. Note 61 recorded this class of trap and was incomplete. CLAUDE.md note 116.
- Worth keeping: the mask is what *found* the angle bug. Bare glyphs at 3° read as a rendering imperfection; a filled rectangle behind them makes the same 3° obvious.

### Recently Completed (2026-08-18) — Reserved Names
- [x] **A project named after part of the API could not use it** — the project name becomes the namespace of the generated code, and a namespace declaration is searched before any `using`, so in a project called *Mouse* the identifier `Mouse` bound to the user's own namespace and `Mouse.OnMove(...)` failed with CS0234. Applied equally to `Frame`, `Canvas`, `VCircle`, `Shape`, `Console`, `Math`, `List` and to C# keywords. New projects now get a non-shadowing namespace (*Mouse* → `MouseProject`), from a reserved set reflected over the imported namespaces so it cannot go stale. CLAUDE.md note 111.
- [x] **Renaming a namespace or a project directory used to break Run** — the entry point was resolved solely as `{sanitized project name}.Viz`. It now falls back to scanning for a class named `Viz` with a public static `Main()`, which is also what keeps every project already on disk working after the rename above.
- [x] **Ctrl+N created a file that could not compile** — the default name `Untitled-1` was written straight into the template as `public class Untitled-1`; a project name with a space produced an equally invalid namespace. Both are now sanitized.
- [x] **The shadowing error pointed at the one token that was correct** — the compiler blames the token it failed to look up, so shadowing `Mouse` underlined `OnMove` and never mentioned the declaration. The error now lands on the declaration reading **"Mouse is a keyword. try another name"**, once however many uses it broke, across the console, the editor squiggles and the error count alike. Gated so ordinary mistakes are untouched: a typo against the real API still reports as a typo. CLAUDE.md note 112.
- Not a follow-up: the rename happens at project-creation time only, and projects already on disk are deliberately left alone. Projects are disposable sketch files, not a compatibility surface — see the standing rule under Conventions in CLAUDE.md.

### Recently Completed (2026-08-18) — Two Silent Losses
- [x] **Lines, polylines and unfilled rectangles/polygons vanished after the first redraw** — `StrokeBatcher.Add` enrolled a pen in the draw list only when it created that pen's bucket, but `Flush` clears the list while keeping the buckets (their segment lists are reused so a frame doesn't allocate one per pen). From the second flush onward the pen was never re-enrolled and `Flush` iterated an empty list: it drew nothing, and since the drawing loop also clears the segments, they grew without bound. Anything triggering a full redraw — pan, zoom, select, run, an animation frame — made the shapes disappear, while they stayed selectable and showed correct values in the Properties panel. `Tests/StrokeBatcherTests.cs` is new; the class had none. CLAUDE.md note 109.
- [x] **`DoesIntersect` was false for almost every pair of shapes** — only line/line, line/rectangle, rectangle/rectangle, point and group were ever answered, so a ray-casting loop's `if (ray.DoesIntersect(obstacle))` guard never fired even though `ray.Intersect(obstacle)` on the next line returned real points. Overload resolution hid it: the derived-type `VRay.Intersect(ICurve)` won at the call site while `DoesIntersect` fell to the near-empty `Shape.Intersect(Shape)`. Both now defer to `CurveIntersection` for curve pairs. CLAUDE.md note 110.
- [x] **`VRay`/`VXLine` intersections were ~185,000× slower than necessary, and approximate** — they fell through to segment sampling, and because `VRay.GetLength()` is infinity (saturating to `int.MaxValue` on the cast) the ray took the full 1000-segment cap, giving a million segment-pair tests per query at 65 ms. Now converted to their finite `RenderExtent` span and solved in closed form: 0.35 µs, and the reported 359-ray loop went from 154 s to 0.9 ms. A ray still reaches only as far as `RenderExtent` (10000 by default) — now documented on its F1 page.
- [x] **`RayCaster` returned bogus hits for construction lines** — `VRay`/`VXLine` were documented as excluded but never were: their bounds are finite (from `RenderExtent`), so the non-finite-bounds filter never caught them, and with no exact ray math a hit on one landed on its bounding box. A diagonal guide could answer with a point nowhere near itself and still win the nearest-hit race against the real geometry. Now excluded by type, as the docs always claimed. **Open follow-up:** add exact ray-vs-ray/xline math if construction guides should ever be real hit targets.

### Recently Completed (2026-08-15) — Slice, and the Web App
- [x] **`VPolygon.Slice` was losing 94% of the polygon on a concave cut** — it paired boundary intersections in *perimeter* order and closed each piece with a single chord, which assumes every output piece is one arc plus one chord. True only for a convex cut with exactly two crossings. A notch straddling the line means four crossings: it emitted the arcs between intersections 0-1 and 2-3, dropped 1-2 and 3-0, and **could not represent the remaining piece at all** — that one is bounded by two arcs. The reported parcel returned two slivers totalling 12,945 against an area of 225,561, and since each sliver was a valid polygon nothing failed loudly. Replaced with two half-plane intersections through `PolygonClipper`: area-preserving by construction, and the four-crossing cut now correctly returns three pieces. CLAUDE.md note 73, `Tests/PolygonSliceTests.cs` (12 cases, the reported parcel verbatim).

### Recently Completed (2026-08-13) — Documentation Ownership
- [x] **`docs-author` subagent owns the user-facing docs** (`.claude/agents/docs-author.md`), and running it is a required step of `/release`. First pass documented every public type and member of C2VGeometry, `DoodleSharp.Animation` and `DoodleSharp.Console` with working examples across README, F1 Help, SKILL.md and ApiReferenceResource.
- [x] **Fixed the code defects that pass uncovered** — early-starting animations, phantom circles from arc intersections, VHatch/VRadialDimension/VSpatialGrid vanishing after a run, `GetClosestCell` requiring a drawable. See TASKS Phase 37 for the list left open for a decision.

### Recently Completed (2026-08-13) — Error Squiggles
- [x] **Zero-width diagnostics are now underlined** — every marker loop guarded on `length > 0`, but Roslyn reports missing-token errors (`;`, `)`, `(`, incomplete statements) as empty spans, so a bare `for` produced seven diagnostics, no squiggle and an error count of zero. `Editor/DiagnosticRange.cs` widens empty spans; errors count regardless of whether a marker can be drawn; diagnostics on the same range merge into one marker. CLAUDE.md note 51.

### Recently Completed (2026-08-13) — Geometry API Correctness
- [x] **All seven open API warts closed** — `Contains`/`DistanceTo` implemented on every shape (with a reflection guard), `VRectangle.RotationAngle` unshadowed so rectangle rotation animations work, `VGrid`'s unreachable constructor, `VEllipse` arc-length parameterisation, `GeometryDiagnostics.Sink`, `ChartOptions.ShowLegend`, and `BuiltInHatches.Get` cloning. Six further defects surfaced while checking the work. TASKS Phase 38, CLAUDE.md notes 53–58.

### Open
- [ ] **`RotationAngle` is render-only; hit-testing is not** — `Contains`, `DistanceTo` and click-to-select all work on a shape's unrotated geometry, so a shape turned by `RotateAnimation` is drawn rotated but picked unrotated. Longstanding and unchanged, but it now reaches every shape rather than the four that used to honour rotation (CLAUDE.md note 68), so it is more likely to be noticed. **`VRectangle` is the exception in both directions**: because its setter rebuilds the corner geometry rather than relying on a render transform, its `Contains`/`DistanceTo` *do* follow its rotation. Fixing the general case means transforming the test point into the shape's local frame in `SelectionTool.HitTestShape` and in each `Contains` — at which point the rectangle's special case could go away too.
- [ ] **`VEllipse.Contains` assumes an axis-aligned ellipse** — the implicit-equation test `(dx/rx)² + (dy/ry)² ≤ 1` only holds when the axes line up with X and Y, which today is guaranteed because `VEllipse` has no rotation property. Nothing to fix yet, and writing the general form now would mean untestable dead code guarding a property that does not exist. **The hazard is that the assumption is invisible**: adding rotation later (a reasonable thing to want — `VRectangle` already has it) leaves `Contains` compiling and returning wrong answers near the ellipse's ends, exactly where a hit test matters. The general form needs the test point rotated into the ellipse's local frame first. `DistanceTo` samples the curve and is unaffected. Also CLAUDE.md note 54.

### Recently Completed (2026-08-14) — Rotation, Release Tooling, API Decisions
- [x] **Rotation animations work on every shape** — `RotateAnimation` wrote `RotationAngle`/`RotationPivot` on any shape, but only `DrawLine`/`DrawCircle`/`DrawArrow` read them back, so ellipses, arcs, polygons, polylines, béziers, splines, text, groups, hatches and regions never turned. Applied once in `DispatchShapeDraw` instead of per shape. **Verified visually** via an offscreen render probe at 0°/45°/90°. CLAUDE.md note 68, `Tests/ShapeRotationTests.cs`.
- [x] **Renderer changes are now verifiable** — `RenderCanvas.Render` is public, so an out-of-repo WPF console app can render a scene to a PNG and it can simply be looked at. Recipe and its non-obvious requirements in CLAUDE.md note 69. Closes the standing "cannot check GUI behaviour" gap for canvas work.
- [x] **`release.ps1` stamps `CHANGELOG.md`** — it bumped `Directory.Build.props` and `installer.iss` but left the changelog, so every release's entries stayed under `[Unreleased]` and the curated history fell a release behind (caught when 2026.8.5's notes were still there after it shipped). It now converts `[Unreleased]` into the new version section, warns rather than fails when there is nothing to stamp, and does its file I/O as explicit BOM-less UTF-8 — `Get-Content`/`Set-Content` in PowerShell 5.1 use the ANSI codepage in **both** directions and would have written mojibake over every em-dash.
- [x] **`VTransform.CreateRotation` deprecated, not redefined** — `CreateRotationRadians` added as the explicit name, `CreateRotation` marked `[Obsolete]` forwarding to it. Redefining the name to mean degrees would have left existing calls compiling and silently rotating by 1/57th. CLAUDE.md note 70.
- [x] **`BooleanOps.UnionAll` added** — the "give me the pieces" entry point. `Union` returns null for inputs that do not all overlap, and the diagnostic pointed at `UnionWithHoles`, which only takes two polygons. CLAUDE.md note 71, `Tests/UnionAllTests.cs`.

### Recently Completed (2026-08-13) — API Naming and the Missing Conveniences
- [x] **`Shape.Place()` replaces the overloaded `Draw()`** — `Draw()` meant "mark as explicitly drawn" on a registered shape and "actually place it" on an unregistered one. Both are the same two lines; only the starting state differed, so the fix was a name. `Draw()` is now `=> Place()` and stays (not `[Obsolete]` — a warning on every existing call is worse than an extra name). `CodeGenerator` and `CodeSnippets` emit `Place()`, since the app was the largest producer of the old spelling. CLAUDE.md note 66, `Tests/CodeGeneratorTests.cs` + `NewConvenienceApiTests.DrawIsExactlyPlace`.
- [x] **The four documented-but-missing conveniences added** — `DoubleExtensions.ToRadians()`/`ToDegrees()` (new class), `VCircle.Diameter`, `VPolyline.PointCount`, and `Shape.CopyStyleTo` — which turned out not to be missing at all but `protected`, which is why the reflection audit reported it as absent from public API. Promoted to public, null-safe, returns `target` for chaining. CLAUDE.md note 67, `Tests/NewConvenienceApiTests.cs`.

### Recently Completed (2026-08-13) — Undoable Canvas Delete
- [x] **Deleting a shape from the canvas is undoable** — the command stack, `DeleteShapesCommand` and the Ctrl+Z routing all already existed, but the canvas delete never pushed a command, so the one operation that edits the user's source had no undo. `DeleteShapesWithCodeCommand` restores the shape *and* the source text (captured verbatim before/after), removes from both the registry and the display list, and invalidates the resident assembly so a parameter change can't re-run stale IL. `IsCanvasUndoContext()` now mirrors the Delete key's gate, fixing the case where a delete cleared the selection and left no way to undo. Multi-file search lifted into `CodeSyncManager.PlanDeletion<T>` for testability. CLAUDE.md note 65, `Tests/CodeSyncDeleteTests.cs`.

### Recently Completed (2026-08-13) — External Edits and Canvas Pollution
- [x] **External edits to open files are read back in** — `RefreshFilesFromDisk` never refreshed the content of a file it already had, so another editor, a git checkout or a tool was silently ignored while the status bar claimed a refresh. It now returns a `DiskRefreshResult` and the host reports what actually happened. A file with unsaved changes is never overwritten (reported as a conflict instead), identical content stays a no-op so our own saves don't reset the editor, the caret and scroll survive a reload, and reads retry on a sharing violation. CLAUDE.md note 63, `Tests/DiskRefreshTests.cs`. Supersedes note 52.
- [x] **Query methods no longer draw their answer** — `GeometryHelper.IntersectLineLine`/`IntersectRectRect`/`IntersectLineRect` and `VRay.ToFiniteLine`/`ToXLine`/`VXLine.ToFiniteLine` all auto-registered their result, so asking a maths question littered the canvas. They now build under `Shape.SuspendAutoRegistration()` and the docs say to `Draw()` the result if you want it. Also fixed `Region.CloneCurve`/`ReverseCurve`, where cloning a region leaked one shape per edge because `Clone()` registers. CLAUDE.md note 64, guarded in `Tests/GeometryRegistryPollutionTests.cs`.

### Recently Completed (2026-08-13) — Live-Testing Round 2
- [x] **No member list after `circle.`** — a dot at the end of a line makes the parser swallow the next statement into a qualified *name*, where `GetSymbolInfo` yields no symbol and `GetTypeInfo` yields a non-null **error** type, so the "did the receiver bind?" check said yes and looked up members on a type that has none. Now takes a lone candidate symbol, rejects error types, and falls back to speculative binding of the receiver text. CLAUDE.md note 44.
- [x] **Canvas delete left the code behind** — the declaration regex could not span a nested constructor call, so `new VRay(p1, new VXYZ(1, 2))` was never matched while the status bar reported a successful delete. Balanced statement scan (string/char/comment aware), all files searched, editor flushed first, and an honest status message when the code is not found. CLAUDE.md note 60, `Tests/CodeSyncDeleteTests.cs`.
- [x] **Signature tooltip stayed up after `)` and `;`** — it now requires the caret to still be inside an argument list, re-checked after the async compile and on every caret move. CLAUDE.md note 59.
- [x] **Stale / unwanted completion popups** — an auto-triggered list is dropped if the caret moved during the compile, and an empty symbol list no longer opens a window of snippets while a new variable is being named. CLAUDE.md note 59.

### Recently Completed (2026-08-13) — IntelliSense Behaviour Fixes
- [x] **`VXYZ` hidden from every completion list** — `ShouldHide`'s "all-uppercase names are interop noise" rule was eating the geometry library's core type. User-facing types now bypass BCL decluttering. CLAUDE.md note 47.
- [x] **Space no longer commits a completion** — it was rewriting `new ` to `VXYZ ` and `new VXYZ(10, ` to `new VXYZ(10,Viz )`. `Editor/CompletionInteraction.cs`, CLAUDE.md note 48.
- [x] **No completion while naming a new variable/parameter**; **keywords added** so `int`/`for` match exactly; **window opens on snippets alone** so a half-typed `for` still offers the snippet. CLAUDE.md note 49.
- [x] **Signature help shows all overloads** (and resolves cross-file). CLAUDE.md note 50.
- [x] **Ctrl+Space always re-queries.**

### Recently Completed (2026-08-13) — IntelliSense & Quick Actions
- [x] **Double-clicking a `.vizproj` opens it directly** — `App.OnStartup` now reads `e.Args` (`FindProjectArgument`/`TryOpenProject`) and shows `MainWindow` with the project loaded; the installer already registered the association and passed `"%1"`, the app was discarding it. A load failure reports the error and falls back to the welcome window so the process is never left with no UI. CLAUDE.md note 46, `Tests/AppStartupTests.cs`.
- [x] **Generate method targets the owning type, across files** — `RefactoringProvider.ResolveGenerationTarget` binds the invocation receiver and locates the insertion point via `DeclaringSyntaxReferences`; `MethodStubBuilder` builds the stub; `MainWindow.GenerateMethodFromQuickAction` applies it (in-document when it's the open file, in-memory + open otherwise). Replaces a backwards brace-count over the active document. Metadata types offer no action. CLAUDE.md note 42.
- [x] **Editor compilations are offset-faithful** — `ModuleCompiler.CreateCompilationAsync(project, forExecution)`; `AnimationNameRewriter` + `StackGuardRewriter` now run only for execution. Fixes F12/Shift+F12 resolving the wrong token and rename writing at shifted offsets into other files. CLAUDE.md note 41.
- [x] **Completion dropouts fixed** — dot-while-list-open now reopens as a member list; `_completionWindow` is published only once shown (a single internal error used to kill IntelliSense for the session); the workspace tracks files added/removed mid-session and the outgoing file on tab switch. CLAUDE.md note 43.
- [x] **Member-access classification and filtering** — receiver classified by `GetSymbolInfo` before `GetTypeInfo` (a class name was being treated as a value, hiding its statics); instance access hides statics; unbindable receiver returns nothing instead of the global list; `ToString`/`Equals`/`GetHashCode`/`GetType`/type parameters are no longer hidden. CLAUDE.md note 44.
- [x] **Full editor context menu** (nav + quick actions + rename) and **quick actions reuse the cached workspace** instead of rebuilding the compilation and re-running NuGet restore per invocation. CLAUDE.md note 45.
- [x] **19 new tests** — `Tests/RefactoringProviderTests.cs`, `Tests/CompletionServiceTests.cs`, `Tests/EditorWorkspaceTests.cs`. Verified in the live GUI end to end on the reported repro.

### Recently Completed (2026-08-13)

### Recently Completed (2026-08-10)
- [x] **Auto Save** — `Settings > Application Settings > Auto Save` writes every modified file in the project to disk on a configurable interval (5–3600 s, default 60). Only dirty files are written, and the status bar reports each save. When the project has no location on disk yet (still in the temp folder, or a file that never went through the Save dialog) it prompts to save instead; answering No keeps the changes in memory and silences the reminder until the project has a real path. `ApplicationSettings.cs`, `MainWindow.xaml.cs`.
- [x] **Zoom-relative line weight / line type scale (new default)** — `Settings > Application Settings > Line Style Rendering` lets each be measured in world units (default: strokes and dash patterns scale with the geometry as you zoom) or screen pixels (the previous behaviour), independently. `RenderCanvas.GetShapePen()`. See CLAUDE.md note 38.

### Recently Completed (2026-08-03)
- [x] **Global Parameters** — project-wide named values (`GlobalParameters.Set/Get`) shared by every module, surviving across runs. `Get(...)` returns a self-converting `ParamValue` so `Get("Length") * 0.5` and `Get("Broken") ? a : b` need no type argument. Changing a value re-executes the code, so everything derived from it updates at once (no dependency graph). `C2VGeometry/Parameters/`.
- [x] **Global Parameters sidebar (`F6`)** — lists every parameter grouped by `group:`, with a value box + `[min] [slider] [max]` for numbers, checkbox for booleans, text box for strings. Dragging a slider updates the canvas in real time (re-executes against the resident assembly, no compile pause) and on release writes the new value back into the `Set(...)` literal in the source, preserving the other arguments and the undo history. `GlobalParametersPanel.xaml`, `Project/ParameterCodeWriter.cs`. See CLAUDE.md note 37.

### Recently Completed (2026-07-02)

### Recently Completed (2026-08-17)
- [x] **Arrowhead geometry has one implementation, and `VArrow.HeadAngle` finally works** — there were **five** copies of "where do the wings go", and they disagreed. `RenderCanvas.DrawArrow` and `VArrow.GetArrowheadPoints` hard-coded a `HeadLength / 6` perpendicular half-width (an effective ≈9.46° half-angle) and **never read `HeadAngle`**; `ShapeTessellator` honoured it but drew an open V; `PdfExporter` honoured it *and* clamped the head to 20% of the shaft; `DxfExporter` hard-coded both 30° and `min(length * 0.2, 10)`, ignoring `HeadLength` too. Net effect: setting `HeadAngle` did nothing on screen, changed the raster/GPU/PDF output, and an arrow's head was a different shape *and* size depending on which backend or exporter drew it. Now `VArrow.ArrowheadWings(tip, from, headLength, headAngleDegrees)` is the only implementation and all five call it; the head is a closed triangle everywhere. **Visible consequence: heads are wider than they used to render**, because the canvas was pinned at ≈9.5° whatever `HeadAngle` said. Also fixed while there: `DoubleEnded` was ignored by the PDF, SVG *and* DXF exporters, silently dropping the start head. Guarded by `Tests/ArrowheadConsistencyTests.cs`, including a source scan that fails if any renderer reintroduces local wing maths. CLAUDE.md note 92.
- [x] **Dimension arrowheads unified too** — an independent instance of the same bug: the tessellator drew them at a hard-coded 20° while the canvas, SVG and PDF each used a fixed `ArrowSize / 6`. `VDimension.DimensionArrowAngleDegrees` (public const 20) is now shared by all four.
- [x] **`VDimension.ExtensionLength` marked `[Obsolete]`** — declared with default 10, copied in `Clone()`, scaled in `Scale()`, and **read nowhere**. An extension line spans `OffsetFromOrigin` → `Offset + ExtendBeyondDimLines`, so those three already determine its length completely and there is nothing left for it to control. Deprecated rather than deleted so existing code still compiles (the warning is the message); no longer scaled, since scaling a value that controls nothing only maintained the illusion that it did. `Clone()` still copies it under a local pragma so a clone reads back what was set.
- [x] **The F10 readout and selection handles no longer leak into exports** — `_overlayVisual` is a visual child of `RenderCanvas` and every image/video export renders the canvas, so the frame-timing readout, selection handles, rubber band, snap markers and measuring overlay were all being baked into exported PNGs, GIFs and MP4s. `RenderCanvas.SuppressOverlayForCapture()` returns an `IDisposable` scope used by all three export paths; `RedrawOverlay` checks the flag itself, so a repaint triggered mid-capture cannot put the chrome back.
- [x] **`RenderBackend` has a settings UI and a complete XML doc** — `ShouldUseRasterBackend` has always honoured a fourth value, `"GPU"`, that the property's own doc never mentioned, and there was **no UI for the key at all**: it was reachable only by hand-editing `appsettings.json`. Now a dropdown under `Settings > Application Settings > Rendering` (Auto / Legacy / Managed / GPU) with an explanatory note, and the doc lists all four plus the unrecognised-value-behaves-as-Auto rule.
- [x] **`RegionBooleanOps.Union` no longer ignores `segmentsPerCurve`** — `Intersect`/`Difference`/`Xor` collection folds all took it; `Union` did not, so a union was the one operation that silently discarded the caller's chosen precision. The `IEnumerable<Region>` overload now takes it (the `params Region[]` form cannot — an optional parameter cannot follow `params` — so pass a list to control precision). Guarded behaviourally in `Tests/ExportFidelityTests.cs`: a coarse fold must produce a smaller union area than a fine one, which fails if the parameter is dropped again.
- [x] **F1 Help was hiding most of the API** — `Documentation/DocGenerator.cs` reflected its member tables with `Public | Instance | DeclaredOnly`. Without `Static`, all 23 static classes (`VColor`, `BooleanOps`, `Chart`, `GlobalParameters`, `ArrayOps`, `GeometryHelper`, `Frame`, `EasingFunctions`, …) rendered a page with no members, and static factories (`VCircle.FromCenterDiameter`, `VArc.FromStartCenterEnd`, `VXYZ.BasisX`, `VTransform.CreateRotationDegrees`) were invisible — **339 written member descriptions were unreachable**. Enums were worse: with neither properties nor methods they listed nothing at all, including `ColorName` (83 values) and `BuiltInHatch` (73), the two pages most likely to be opened to look up a name. Const fields (`GeometryTolerance.Epsilon`) were dropped too. Now: `DocGenerator.MemberFlags` is the single shared flag set (used by `HelpWindow`'s search index as well, so the two cannot drift), enum values and fields have their own sections, and staticness is marked in the signature column. Guarded by 21 new cases in `Tests/DocGeneratorTests.cs`. See CLAUDE.md note 91.
- [x] **Documentation audit against the built assemblies** — the whole public surface was dumped by reflection and every `Type.Member` in `README.md` / `DocGenerator.cs` checked back against it (the note-62 procedure). Found 8 documented members that do not exist — `SvgExporter.ExportToString`/`Width`/`Height`, `PdfExporter.Margin`/`PageSize`, `GifEncoder.FrameDelay`/`Repeat`/`Save` (several are constructor *parameters* misdocumented as properties) — plus 391 real user-facing members with no description, and two shipped features (F10 readout, Direct3D backend) documented nowhere.
- [x] **`requestAnimationFrame`-style per-frame callbacks** — `Animation/Frame.cs`: `Frame.Request(callback)` queues a callback for the next frame; it re-requests to continue and stops asking to end. Complements rather than replaces the timeline, which stays because `Animation.Apply(t)` is a pure function of normalised time and therefore *seekable* (the scrub bar and the GIF/MP4 exporters render at time T without playing up to it). Two queues swapped per pump so a callback that re-requests during the pump runs next frame instead of looping forever; `Frame.Clear()` runs before every execution so a queued delegate can't pin the collectible ALC. See CLAUDE.md note 90.
- [x] **Direct3D 11 render backend** — geometry uploaded once in world coordinates, so pan/zoom rewrite only a 64-byte constant buffer; the only backend with flat frame time across navigation and the only one viable at 4K. 3840×2160/100k shapes: 2.89–3.91 ms; mixed-cad worst frame 120.9 → 44.9 ms. No `unsafe` (verified by spike first — `AllowUnsafeBlocks=false` still stands), fails soft hardware → WARP → CPU path. Residual cost is text (~2,700 `FormattedText` labels), which needs a GPU glyph atlas. See CLAUDE.md note 88.
- [x] **Frame-timing readout on F10** — in-app overlay of cull/tessellate/raster split, visible-vs-considered counts and the active backend, from `Rendering/FrameMetrics.cs`.
- [x] **No exporter silently drops a shape type** — every exporter's type switch now ends in a `default` that falls through to `ShapeTessellator` instead of emitting nothing. The switches had been written separately and drifted: **`VDimension` was absent from DXF export entirely** and `VRadialDimension` produced an SVG with no drawing element. `ShapeTessellator` learned to decompose the annotation shapes it used to decline (`VArrow`, `VDimension`, `VRadialDimension`, `VRay`, `VXLine`); its `bool` return is not optional. Guarded by `Tests/ExporterCoverageTests.cs`, which walks the real shape surface by reflection. See CLAUDE.md note 87.
- [x] **Font-size crash when zoomed far in** — `FormattedText` throws above ~35,791 em and `text.Height * scale` reached it, escaping the render pass and killing the process. `DrawText` now clamps. See CLAUDE.md note 89.
- [x] **Vector logo** — `Assets/Logo.xaml` as a `DrawingImage` replacing a 2048px PNG that resampled to a blur and sat on the welcome panel as a visibly lighter rectangle. Wordmark is a `TextBlock`; a reduced `LogoMarkSmall` variant carries small sizes, and the `.ico` ships different artwork per size. See CLAUDE.md note 86.

### Recently Completed (2026-06-15)
- [x] **`TransformAnimation`** — new animation type that morphs one shape into another (e.g. a VLine unfurling into a VCircle) by sampling both outlines and interpolating point-by-point through an internal `VPolyline` proxy. Source shown first, morphing outline during the transition, destination revealed at the end. Robust with "Auto-Draw Shapes" off (registers proxy + both shapes regardless of `Shape.AutoRegister`) and with chained transforms. `Animation/Animations.cs`; guarded by `Tests/TransformAnimationTests.cs` + `Tests/AutoRegisterAnimationTests.cs`.
- [x] **Welcome screen prunes missing recent files** — `RecentProjectsManager`/`RecentAnimationsManager` getters now drop entries whose file no longer exists on every read (not only at startup), so deleted/moved projects and animations stop appearing. Guarded by `Tests/RecentFilesPruningTests.cs`.

### Recently Completed (2026-05-30)
- [x] **Boolean ops migrated to Clipper2** — `C2VGeometry/Operations/PolygonClipper.cs` now delegates polygon Union/Intersect/Difference/Xor (and the `*WithHoles` variants + `MakeSimple`) to the robust Clipper2 library (`Clipper2` NuGet package) instead of a hand-rolled Greiner-Hormann tracer. Fixes the reported bug where unioning an overlapping circle+rectangle returned `null` (a circle centered on the rect corner samples vertices exactly onto the rect edges → zero detected crossings → spurious "disjoint"), plus the collinear-shared-edge-band mis-union. `installer.iss` ships `Clipper2Lib.dll`; `PointInPolygonTest` stays the local ray-cast. Guarded by new regression tests in `Tests/BooleanOpsTests.cs` and `Tests/RegionFromClosedCurveTests.cs`. See CLAUDE.md note 32.

### Recently Completed (2026-05-27)
- [x] **Calendar versioning (`YEAR.MONTH.PATCH`)** — `scripts/release.ps1` no longer takes `-Bump`; it stamps year/month from the release date and bumps patch within a month (resets on a new month/year). Version sources (`Directory.Build.props`, `installer.iss`) moved `2.0.0` → `2026.5.0`; the `/release` section in `CLAUDE.md` was updated to match.
- [x] **CodeLens blink-on-broken-syntax fix** — `Editor/CodeLensProvider.cs`: a nearby structural syntax error made Roslyn error-recovery intermittently fail to re-parse the following declaration as a method, so alternating recomputes added/dropped its 2×-tall CodeLens row and blinked it in/out. `UpdateCodeLens` now swaps `_items` only on a clean parse; on a broken parse it merges (keeps prior items, only adds new, never removes) and a failed build leaves `_items` untouched. Shared `Editor/` source — flows to both apps.
- [x] **Canvas-focus fix for drawing-tool shortcuts** — `RenderCanvas.OnMouseDown` now grabs keyboard focus on click, so single-key drawing-tool shortcuts (P/L/C/R, plus Delete/A/Esc) fire on the canvas instead of typing into the code editor. Click the canvas first to focus it, then press the key.

### Recently Completed (2026-05-26)
- [x] **CodeLens vertical-jitter fix** — `Editor/CodeLensProvider.cs` stored each CodeLens row as a frozen absolute offset and only recomputed on the 500 ms semantic-update debounce, but AvalonEdit redraws touched lines on every keystroke. Typing above a code-lensed declaration shifted the real offsets while the cached ones went stale, so the 2×-tall CodeLens row rendered on the wrong line and snapped back after the debounce. Each item now holds a live `TextAnchor` (`AfterInsertion`, `SurviveDeletion`) and the element generator reads `CurrentOffset`, so the row tracks edits instead of snapping; only the debounced count text lags, which causes no movement. Flows to both apps via the shared `Editor/` source.

### Recently Completed (2026-05-25)

### Recently Completed (2026-05-24)
- [x] **`VText.Rotate(pivot, angle)` fix** — used to only rotate `Location` around the pivot; the text's own `Angle` field was never updated, so a rotation with `pivot == Location` produced no visible change. Now matches the convention of `Rectangle2D` / `Arc2D` / `Hatch2D` / `RadialDimension2D`: both `Location` (around pivot) and `Angle` (`+= angleDegrees`) are updated. Mirrored across `DoodleSharp.Geometry` and `C2VGeometry`.
- [x] **`VText.DoesIntersect`** — text-aware intersection: builds the (possibly rotated, anchor-aware) bounding quad and tests it against the other shape's bounding box via SAT. Mirrored in both `DoodleSharp.Geometry` and `C2VGeometry`. `Shape.DoesIntersect` falls back to `other.DoesIntersect(this)` when `other is VText` so the check is symmetric.
- [x] **Windows menu polish** — Run no longer force-shows the console when the user has hidden it; `Windows > Console` toggle now persists; Console menu checkmark no longer shows stale-checked at launch; collapsing both Console and Canvas now reclaims their shared column so the editor fills the row; new `Windows > Ribbon` toggle hides/shows the top logo/version panel (persisted).

### Recently Completed (2026-05-21)
- [x] **F# support removed** — `FSharp.Compiler.Service` / `FSharp.Core` package refs gone; `FSharpModuleCompiler`, `FSharpTemplates`, `FSharpHighlighting.xshd`, `FSharp/VizDsl.fs` deleted; `ProjectLanguage` enum + `VizProjectFile.Language` removed; all `isFSharp` / `== ProjectLanguage.FSharp` branches stripped from `MainWindow`, `Canvas/CodeGenerator`, `Canvas/CodeSyncManager`, `Execution/ModuleCompiler`, `Editor/SharedEditorController`, `Project/*`; F# tab removed from `HelpWindow`; Welcome / New-Project language ComboBox gone. Net: 25 files, +186 / −1942.
- [x] **Squiggle visibility fix** — `Editor/TextMarkerService.Draw` now uses amplitude 2 / pen 1.2 / position `r.Bottom - amplitude` instead of amplitude 1 in the inter-line gap.
- [x] **`VCircle(VXYZ, double)` overload** added to `DoodleSharp.Geometry.VCircle` so the help-sample-style `new VCircle(new VXYZ(50, 50), 30)` now compiles in DoodleSharp projects. Uses `VPoint.Internal(center.X, center.Y)` to avoid auto-registering a stray marker.

---

## Known Defects (open)

Found by the 2026-08-17 documentation pass — reading every signature turns up things no bug report
would. None is fixed; each needs a decision rather than just a code change.

- [ ] **`VEllipse.SplitAtPoint` shares its `Center` object with the original.** `new VEllipse(Center, …)` rather than `Center.Clone()`, unlike `VCircle.SplitAtPoint` which clones. Moving one of the returned pieces mutates the original ellipse's centre. A one-line fix, listed here only because it was found after the defect-fix pass closed.
- [ ] **`ICurve.SplitAtPoint` and `ICurve.Offset` auto-register their results and leave the original in place** — so splitting a circle leaves three shapes on the canvas. This is note 64's "query methods must not draw their answer" rule reaching further public API than that note covers. Whether a split *should* consume its source is a design call (`Region(ICurve)` does, per note 29), which is why it is recorded rather than changed.
- [ ] **`PolylineFallbackSink.Unhandled` is never populated by anything in the repo.** Its `BeginShape` unconditionally returns `true`, so the sink cannot fill the list itself — it is the *caller* that must append when `Tessellate` returns false, and no caller does. The XML doc reads as though the sink maintains it, so "an empty `Unhandled` means the export was complete" is currently false.
- [x] **Several documented types are unreachable in the F1 tree.** ~~`SvgExporter`, `SnapType`, `SnapResult`, `SnapEngine`, `DrawingTool` and `DrawingInputMode` all live in `DoodleSharp.Canvas`, which is not in `DocGenerator`'s namespace prefix list.~~ Fixed in 2026.8.4 by `DocGenerator.AllowedInternalTypes`, a per-type allowlist keyed on full names — exactly as this entry proposed. `GlyphOutlineProvider` and `DrawingMode` went in alongside the six. See note 104; `EveryAllowlistedInternalTypeIsReachable` asserts each one both resolves and lands in the tree.
- [ ] **API surprises worth either fixing or keeping documented** — `VArc.MoveControlPoint` indices 2 and 3 set `Radius` as well as the angle, so dragging an arc's end both sweeps *and* resizes it; `VArc.PointAtSegmentLength` clamps at `EndAngle` while `VCircle`'s wraps; `VBezier.Offset` uses only the end tangents, so it is exact only for near-straight or near-circular curves; `VSpline.PointsAtChordLengthFromPoint` returns the midpoint of the crossing segment rather than the true intersection; `VPolygon.Offset` (inherited by `VRectangle`) has no miter compensation and its direction depends on the winding of `Points`.
- [ ] **An arrowhead is filled on the vector backend and hollow on the raster and GPU backends.** Verified by offscreen render (note 69) at `Legacy` vs `Managed`: the *geometry* now matches exactly — same angle, same size, closed triangle, both heads on a double-ended arrow — but `RenderCanvas.DrawArrow`/`DrawDimensionArrowhead` fill the triangle with the **stroke** colour (`GetCachedBrush(arrow.Color)`), while `ShapeTessellator` emits it via `EmitPolyline(closed: true)` and the raster/GPU sinks therefore stroke its outline. Closing this needs a way for the tessellator to say "fill this loop in the stroke colour": `IPrimitiveSink.EmitFilledLoops` fills with `PenSpec.FillColor`, which is `Transparent` on a default arrow, so it cannot express it today. That is a sink API addition (plus implementations in the raster and D3D sinks), which is why it is recorded rather than bundled into the geometry fix. Much narrower than the original defect, which also had the wrong angle, the wrong size, an open V, and missing heads.

## Notes

- Coordinate system: Mathematical (Y-up) - DO NOT CHANGE
- Grid spacing: Currently fixed at 50 units - make configurable
- Color parsing: Uses WPF ColorConverter - supports all named colors
- Script execution: Uses Roslyn - any C# syntax works
