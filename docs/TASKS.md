# Task History - DoodleSharp Development

## Completed Tasks

### Phase 1: Project Setup
- [x] Create WPF .NET 8.0 project structure
- [x] Add NuGet packages (AvalonEdit, Roslyn)
- [x] Setup project directories (Geometry, Canvas, Editor, Execution)

### Phase 2: Core Geometry Classes
- [x] Create `IDrawable` interface
- [x] Create `Shape` abstract base class with styling properties
- [x] Implement `Point` class
- [x] Implement `Line` class
- [x] Implement `Arc` class
- [x] Implement `Circle` class

### Phase 3: Extended Geometry Classes
- [x] Implement `Rectangle` class
- [x] Implement `Ellipse` class
- [x] Implement `Polygon` class
- [x] Implement `Polyline` class

### Phase 4: Canvas Implementation
- [x] Create `RenderCanvas` custom control
- [x] Implement world-to-screen coordinate transformation
- [x] Implement screen-to-world coordinate transformation
- [x] Implement mouse wheel zoom (centered on cursor)
- [x] Implement middle-click pan
- [x] Implement `ZoomExtents()` method
- [x] Implement grid line drawing
- [x] Implement coordinate axes drawing
- [x] Add `MouseWorldPositionChanged` event

### Phase 5: Shape Rendering
- [x] Create `CanvasRenderer` singleton
- [x] Implement Point rendering
- [x] Implement Line rendering
- [x] Implement Arc rendering (using PathGeometry)
- [x] Implement Circle rendering
- [x] Implement Rectangle rendering
- [x] Implement Ellipse rendering
- [x] Implement Polygon rendering
- [x] Implement Polyline rendering
- [x] Implement color parsing from string names

### Phase 6: Code Editor
- [x] Integrate AvalonEdit component
- [x] Create C# syntax highlighting definition (XSHD)
- [x] Add geometry class highlighting (Point, Line, etc.)
- [x] Implement `CodeFormatter` class
- [x] Apply light theme to editor

### Phase 7: Script Execution
- [x] Create `ScriptRunner` class
- [x] Configure Roslyn ScriptOptions with geometry imports
- [x] Implement async code execution
- [x] Implement error handling and reporting

### Phase 8: Main Window UI
- [x] Design three-row layout (Ribbon, Content, Footer)
- [x] Implement resizable split view (Canvas | Editor)
- [x] Create ribbon with file operations
- [x] Create ribbon with Run/Clear buttons
- [x] Create ribbon with Format button
- [x] Add Export PNG button
- [x] Add Grid toggle checkbox
- [x] Display coordinates in footer
- [x] Display status messages in footer

### Phase 9: File Operations
- [x] Implement New file functionality
- [x] Implement Open file functionality
- [x] Implement Save file functionality
- [x] Add unsaved changes prompts
- [x] Implement PNG export

### Phase 10: Keyboard Shortcuts
- [x] F5 - Run code
- [x] Ctrl+N - New file
- [x] Ctrl+O - Open file
- [x] Ctrl+S - Save file
- [x] Ctrl+Shift+F - Format code

### Phase 11: Dark Theme
- [x] Define color resources in App.xaml
- [x] Style ribbon buttons
- [x] Style canvas background
- [x] Style footer

### Phase 12: Bug Fixes
- [x] Fix canvas placement issue (transform approach)
- [x] Fix Line type ambiguity (WPF vs Geometry)
- [x] Switch editor to light theme for visibility

---

### Phase 13: Animation & Selection Enhancements
- [x] Add ObjectPropertyAnimation<T> for animating numeric properties on any object
- [x] Switch animation loop to CompositionTarget.Rendering (vsync-aligned)
- [x] Add crossing/window selection (drag direction determines mode)
- [x] Add VizConsole.Log itemize parameter for collection output control
- [x] Add VLine constructor from start point, angle, and length
- [x] Add Auto-Draw Shapes checkbox (moved from status bar to Settings > Canvas Settings)
- [x] Reset Shape ID counter on each code execution

---

### Phase 14: Region Support & Animation Bug Fixes
- [x] Add Region shape (curve-bounded 2D area with holes support)
- [x] Add RegionBooleanOps (Union, Intersect, Difference, Xor)
- [x] Add Region rendering in RenderCanvas (DrawRegion method)
- [x] Fix DrawSpline missing DrawFactor support (broke DrawAnimation for VSpline)
- [x] Fix DrawSpline missing OffsetX/OffsetY support (broke MoveAnimation for VSpline)
- [x] Add Region case in main draw switch and VGroup child draw switch
- [x] Fix polygon Union issue (Greiner-Hormann winding order normalization)
- [x] Add C2VGeometry standalone geometry library
- [x] Add minimap with syntax coloring and viewport indicator
- [x] Add BoundingBox class and refactor Shape.GetBounds() return type
- [x] Add Area and Circumference properties to VCircle and VEllipse

---

### Phase 15: Console & UI Bug Fixes
- [x] Fix console panel resize expanding to maximum height with multiline content
- [x] Fix console scroll behavior with variable-height (multiline) entries
- [x] Remove ConsolePanel Grid.RowSpan spanning into Auto row (root cause of layout issue)
- [x] Add pixel-based virtualized scrolling (VirtualizingPanel.ScrollUnit="Pixel")
- [x] Add HorizontalContentAlignment="Stretch" for full-width selection highlight

---

### Phase 16: Project Explorer Enhancements
- [x] Add drag-and-drop file/folder moving in Project Explorer TreeView
- [x] Prevent dragging root node, entry point files, and reference items
- [x] Validate drop targets (no self-drop, no subtree drop, no same-parent drop)
- [x] Update open file references and tabs after move
- [x] Add "Go to Location" context menu item to open file/folder in Windows File Explorer

### Phase 17: UI & Multi-Cursor Fixes
- [x] Move Auto-Draw Shapes checkbox from status bar to Settings > Canvas Settings
- [x] Fix multi-cursor paste (Ctrl+V) only pasting at first cursor

---

### Phase 18: Ray Casting & Spatial Acceleration
- [x] Add `RayCaster` class with flat-array BVH and Surface Area Heuristic split
- [x] Iterative traversal with `stackalloc` index stack (no per-query heap allocation)
- [x] Inline ray-vs-shape math for VLine, VCircle, VArc, VEllipse, VPolygon (and VRectangle), VPolyline; AABB fallback for other shape types
- [x] `RayHit` and `RayQuery` readonly record structs for results and batch input
- [x] `FindIntersection(location, direction)` and `FindIntersection(location, direction, maxDistance)` for closest-hit queries
- [x] `HasIntersection(location, direction, maxDistance = +∞)` for any-hit / shadow-ray queries
- [x] `FindIntersections(IReadOnlyList<RayQuery>, parallel = true)` for parallel batch queries
- [x] `Refit()` to refresh AABBs in O(N) after shape movement without rebuilding the tree
- [x] Thread-safe queries (BVH is read-only after construction)
- [x] xUnit test coverage: closest/any-hit, max-distance pruning, arc/ellipse angle filter, rectangle/polygon edges, batch parallel-vs-sequential parity, refit correctness

---

### Phase 19: RayCaster Refinements
- [x] Replace the `IEnumerable<Shape>` constructor with a canvas-driven `new RayCaster(leafSize = 8)` that snapshots every visible `Shape` on `CanvasRenderer.Instance` at construction time (no explicit collection arg) — **later reverted** by the geometry unification: `C2VGeometry` has no canvas, so the explicit-collection constructor `RayCaster(IEnumerable<Shape>, int leafSize = 8)` is once again the only one.
- [x] Always exclude `VPoint` markers from the index (zero-area visual labels, not useful ray targets) — independent of `IsVisible` or how the `VPoint` was registered
- [x] Fix ray-vs-AABB slab-test NaN when ray direction is zero on an axis intersecting a degenerate AABB (kept as defensive code even after the `VPoint` exclusion removes the most common trigger)
- [x] Add optional `List<Shape>? exclusionList = null` parameter to both `FindIntersection` overloads — converted to `HashSet<Shape>` once per query for O(1) per-leaf-shape lookup, useful for casting off a source shape or finding the next hit past a known set
- [x] Move RayCaster tests into a `"CanvasState"` xUnit collection with `DisableParallelization = true` so they don't race against other test classes that auto-register shapes; setup/teardown `Clear()`s `CanvasRenderer.Instance`

---

### Phase 20: CurveIntersection Canvas-Pollution Fix
- [x] Rewrite `IsPolylineSelfIntersecting` to use raw-double segment math via a new private `SegmentsIntersectRaw` helper — eliminates the per-iteration `new VLine(...)` allocations that were auto-registering on the canvas. A 360-vertex polygon used to dump ~65k phantom shapes; now zero. Construction time drops from ~5 s (real-world isovist case) to <1 ms.
- [x] Rewrite `IsPolygonSelfIntersecting` to flatten curves into `(sx, sy, ex, ey)` tuples via a new private `AppendRawSegments` helper and run `SegmentsIntersectRaw` directly — bypasses `GetSegments` entirely on this hot path. `SharedEndpointTouchOnly` preserves the original knot-vertex exemption from `IsOnlyAtSharedEndpoints`.
- [x] Add internal `VLine.Internal(VPoint, VPoint)` factory and `VLine(start, end, bool register)` constructor — mirrors the existing `VPoint.Internal` pattern, lets utility code allocate `VLine` data containers without auto-registering on the canvas
- [x] Update `GetSegments` to use `VLine.Internal` for the synthesised segments (the `VLine`→[line] passthrough is unchanged) — `IntersectGeneric` and any future caller now gets pollution-free segment tessellation for free
- [x] Mirror all four changes to the parallel `C2VGeometry` namespace (uses `VXYZ` instead of `VPoint`, registers with `DefaultRegistry` instead of `CanvasRenderer.Instance`)

---

### Phase 21: ICurve.SetBounds (Parameter-Range Trim)
- [x] Add `void SetBounds(double startParameter, double endParameter)` to the `ICurve` interface in both `DoodleSharp.Geometry` and `C2VGeometry`. The parameter sub-range [s, e] becomes the new [0, 1]; inputs are clamped to [0, 1] and swapped if reversed.
- [x] **VLine** — Set `Start`/`End` to `Evaluate(s)`/`Evaluate(e)`. The `VPoint` instances are preserved (X/Y mutated) so external references stay live.
- [x] **VArc, VEllipse** — Rescale `StartAngle`/`EndAngle` so the new endpoints sit at the trimmed parameters.
- [x] **VBezier** — De Casteljau twice: split at `e`, take the left piece, then split that piece at `s/e` and keep its right piece. Exact trim; P0..P3 instances are preserved.
- [x] **VPolyline** — Rebuild `Points`: trimmed start, original interior vertices strictly within [s, e], trimmed end. Recompute `_selfIntersecting`.
- [x] **VSpline** — Dense resample at the original render resolution (`numSpans * SegmentsPerSpan` scaled by `(e - s)`) so the trimmed Catmull-Rom passes through enough interpolating points to track the original path. Catmull-Rom tangents depend on neighboring control points, so simply retaining inner CPs visibly bent away from the original.
- [x] **VCircle / VPolygon / VRay / VXLine** — Throw `NotSupportedException` with a message pointing to `SplitAtPoint`. Their trimmed form would change shape type (circle→arc, polygon→polyline, ray/xline→line).
- [x] `_selfIntersecting` made non-readonly on `VPolyline`, `VBezier`, `VSpline` so it can be recomputed after the trim.
- [x] xUnit coverage: 17 cases in `Tests/SetBoundsTests.cs` — VLine subrange + identity + instance preservation + swap + clamp; VArc/VEllipse rescale; VPolyline drop-out-of-range and within-single-segment; VBezier fidelity (trimmed midpoint matches original at remapped parameter) + instance preservation; VSpline endpoint exactness + interior tracking via dense resample; throw-paths for VCircle/VPolygon/VRay/VXLine. All 117 tests in the suite pass.

---

### Phase 23: Shared Editor + Visible Squiggle + F# Removal (2026-05-21)
- [x] **Visible realtime squiggles** — `Editor/TextMarkerService.Draw` was drawing a 1px-amplitude zigzag at `r.Bottom + 1` (i.e., in the inter-line gap, where it's effectively invisible). Now amplitude 2, period 4, pen 1.2, positioned at `r.Bottom - amplitude` so the squiggle tucks under the text baseline.
- [x] **`VCircle(VXYZ, double)` overload** added in `Geometry/Circle2D.cs`. Internally constructs `VPoint.Internal(center.X, center.Y)` to avoid auto-registering a marker — matches C2VGeometry semantics where coordinates are `VXYZ` and `VPoint` is reserved for visible markers. Existing `(VPoint, double)` ctor remains.
- [x] **`/update_docs` sweep** target list refreshed — see `CLAUDE.md` item #14 for the shared-editor architecture note + squiggle behavior, and TODO.md "Recently Completed" for the F# removal manifest.

---

### Phase 24: Manual Release Flow (2026-05-21)
- [x] **`CLAUDE.md` `/release` Command** — documents the procedure (run `/update-docs` first as a separate commit so the release ships with current documentation; never bump versions by hand). Mirrored in a `release_command.md` claude memory entry so future sessions follow the same flow.
- [x] **First release: v1.0.0** — tagged the current state without a bump (the script's bump-then-tag flow is for subsequent releases).

---

### Phase 25: Geometry Unification — Single `C2VGeometry` Namespace (2026-05-27)
- [x] **Port `RayCaster` into `C2VGeometry`** — the spatial accelerator (flat-array BVH, SAH split, inline ray-vs-shape math, `Refit`) now lives in the unified namespace and takes an explicit shape collection (the library has no canvas to snapshot). `VPoint` markers and infinite-bounds shapes (VRay/VXLine) stay excluded.
- [x] **Reconcile `ShapeDefaults` into `C2VGeometry` construction** — global style + dimension defaults are applied at shape-construction time in the unified namespace (no parallel copy in the old namespace).
- [x] **Repoint the whole app from `DoodleSharp.Geometry` to `C2VGeometry`** — every `using DoodleSharp.Geometry;` across Canvas, Editor, Execution, Export, Project, Commands, samples, and templates now uses `C2VGeometry`. User scripts import `using C2VGeometry;`.
- [x] **`VPoint` is now only a drawable marker; `VXYZ` is the coordinate type** — coordinates/positions/vectors (circle centers, line endpoints, polygon vertices, `BoundingBox.Min/Max`, `ICurve.Divide` results, etc.) are `VXYZ` value types. `VPoint` is reserved for visible point markers on the canvas.

---

### Phase 26: Editor & Canvas Fixes (2026-05-27)
- [x] **CodeLens blink-on-broken-syntax fix** — a nearby structural syntax error made Roslyn error-recovery intermittently fail to parse the following declaration as a method, so alternating recomputes added/dropped its (2×-tall) CodeLens row, blinking it in/out and bouncing the code below. `UpdateCodeLens` now swaps `_items` outright only on a clean parse; on a broken parse it merges via `MergePreservingExisting` (keeps all prior items, only adds new `(Kind, SymbolName)`, never removes) and a failed build leaves `_items` untouched instead of blanking the gutter. Shared `Editor/` source — flows to both apps.
- [x] **Canvas-focus fix for P/L/C/R drawing-tool shortcuts** — `RenderCanvas.OnMouseDown` never took keyboard focus on click, so focus stayed in the code editor and pressing P/L/C/R (and Delete/A/Esc) typed the letter into the editor instead of activating the drawing tool. Any canvas click now grabs focus if it doesn't already have it. Pre-existing bug, independent of the geometry-unification work.

---

### Phase 29: Boolean Ops on Clipper2 (2026-05-30)
- [x] **Replaced the hand-rolled Greiner-Hormann clipper with Clipper2 (`C2VGeometry/Operations/PolygonClipper.cs`)** — the old tracer wrongly reported clearly-overlapping shapes as "disjoint" on degenerate inputs (a polygon vertex landing exactly on another's edge — e.g. a circle centered on a rectangle's corner — and full collinear shared-edge bands), so `BooleanOps.Union`/`RegionBooleanOps.Union` returned `null`. Now delegates polygon Union/Intersect/Difference/Xor + `*WithHoles` + `MakeSimple` to the `Clipper2` NuGet library (`ClipperD`/`PolyTreeD`, `FillRule.NonZero`); `PointInPolygonTest` unchanged. Added the package to `C2VGeometry.csproj` and an explicit `Clipper2Lib.dll` line to `installer.iss`. Guarded by new regression tests in `Tests/BooleanOpsTests.cs` (collinear band, donut-with-hole) and `Tests/RegionFromClosedCurveTests.cs` (circle-on-corner). See CLAUDE.md note 32.

### Phase 30: Shape Morphing + Characters as Shapes (2026-06-15)
- [x] **`TransformAnimation`** — morphs one shape into another by sampling both outlines and interpolating point-by-point through an internal `VPolyline` proxy (`Animation/Animations.cs`). Source shown first → morphing outline → destination revealed. Self-sufficient with "Auto-Draw Shapes" off (registers proxy + both shapes regardless of `Shape.AutoRegister`) and with chained transforms. Tests: `Tests/TransformAnimationTests.cs`, `Tests/AutoRegisterAnimationTests.cs`.
- [x] **Welcome screen prunes missing recent files** — `RecentProjectsManager`/`RecentAnimationsManager` getters drop entries whose file no longer exists on every read. Tests: `Tests/RecentFilesPruningTests.cs`.

### Phase 31: Global Parameters (2026-08-03)
- [x] **`GlobalParameters` registry (`C2VGeometry/Parameters/`)** — project-wide named values declared with `Set<T>(name, value, min, max, step, group, description)` and read anywhere with `Get(name)`. Lives in the host assembly so it outlives the collectible user `AssemblyLoadContext`; changing a value re-executes `Main()`, so every derived value is recomputed without any dependency graph. Supported types: numerics (stored as `double`), `bool`, `string`, `DateTime` — user-defined types are rejected to avoid pinning the user assembly. Names case-insensitive.
- [x] **`ParamValue` self-converting read** — implicit conversions to `double`/`bool`/`string`/`DateTime` so `Get("Length") * 0.5` and `Get("Broken") ? a : b` compile with no type argument; `int`/`float` deliberately explicit (an implicit `int` would make `Get("n") * 2` ambiguous). Documented cost: `+` is ambiguous, with `.Num`/`.Text`/`Get<T>()` as escape hatches.
- [x] **Declare-vs-override semantics** — `Set(...)` is idempotent so a re-run doesn't clobber a panel-dialled value, but a changed literal in code wins (`DefaultValue` + `IsOverridden`). `SetRange` pins a panel-widened slider range against the next run's `min:`/`max:` arguments.
- [x] **Run lifecycle** — `BeginRun()`/`EndRun(pruneStale)` wrap the `Main()` invoke in `ModuleCompiler.InvokeMainAsync`. `BeginRun` suppresses `Changed` (mandatory: otherwise `Set` inside `Main` → Changed → re-run → unbounded loop); `EndRun` prunes declarations deleted from code, but only on a completed run.
- [x] **Resident-assembly fast path** — `ModuleCompiler` keeps the last successful `Main()` assembly + ALC loaded (`HasResidentAssembly`/`ReExecuteResidentAsync`/`InvalidateResident`), so a parameter change re-runs in ms instead of paying for a Roslyn compile. Any source write invalidates it.
- [x] **Global Parameters sidebar (`GlobalParametersPanel.xaml`, `Windows > Global Parameters`, `F6`)** — grouped rows with type-appropriate editors: numbers get a value box plus `[min] [slider] [max]`, booleans a checkbox, strings a text box, dates a runtime-only text box. Two-tier updates: slider ticks drive a live resident re-execution; the code write-back + full recompile happens once on commit. End-of-drag detected via `Thumb.DragStarted`/`DragCompleted` (not `PreviewMouseLeftButtonUp` — the Thumb's mouse capture swallows it), with a 450 ms idle backstop for wheel/track gestures.
- [x] **Code write-back (`Project/ParameterCodeWriter.cs`)** — uses `[CallerFilePath]`/`[CallerLineNumber]` captured at declaration, then scans the argument list (bracket/string/char/comment aware, handles generics, named arguments and multi-line calls) for the value argument and replaces just that literal via `CodeEditor.Document.Replace`, preserving undo and caret. Reports a reason string when the call can't be located instead of failing silently.
- [x] **Tests** — `Tests/GlobalParametersTests.cs` (20 tests: round-trip per kind, implicit conversions matching the documented use sites, declare-vs-override, pruning, notification suppression, pinned ranges, caller info) and `Tests/ParameterCodeWriterTests.cs` (15 tests: nested calls, commas in strings, named arguments, multi-line and multiple-calls-per-line). Verified end-to-end in the live GUI via UI Automation. See CLAUDE.md note 37.

### Phase 32: Auto Save + Zoom-Relative Line Styles (2026-08-10)
- [x] **Auto Save (`AppSettingsData.AutoSaveEnabled`/`AutoSaveIntervalSeconds`)** — `DispatcherTimer` in `MainWindow` (`ApplyAutoSaveSettings`/`AutoSaveTimer_Tick`) flushes the editor into the active file and calls `VizCodeProject.SaveAllFiles()` on the configured interval (clamped to 5–3600 s). Only dirty files are written; the status bar reports `Auto-saved at HH:mm:ss`. Timer stopped on window close.
- [x] **Auto Save "project has no home" prompt** — `ProjectNeedsSaveLocation()` detects the temp-directory project state *and* files with `IsNew`/empty `FilePath`; `PromptForAutoSaveLocation()` offers a normal Save. `_autoSavePromptActive` blocks stacked prompts, `_autoSavePromptSuppressed` silences the reminder after a "No" and self-clears once the project has a real path.
- [x] **Zoom-relative line weight / line type scale, on by default** — `AppSettingsData.LineWeightRelativeToZoom`/`LineTypeScaleRelativeToZoom` (both default `true`) + `RenderCanvas.GetShapePen()`, which all 18 shape pen call sites now go through. Relative mode multiplies by `_viewport.Scale`; because WPF dash lengths are multiples of pen thickness, the dash scale divides the relative thickness back out so each mode applies exactly once. Clamped thickness/dash scale, rounded + size-capped pen cache. See CLAUDE.md note 38.
- [x] **Settings UI** — new "Line Style Rendering" and "Auto Save" groups in the Application Settings grid (`MainWindow.xaml`), wired live (no Save button needed) through `LineStyleModeCombo_Changed`, `SettingsAutoSaveCheck_Changed`, `SettingsAutoSaveIntervalBox_TextChanged`.
- [x] **Verified end-to-end in the live GUI via UI Automation** — auto-save wrote a typed edit to disk within the interval without any manual save; switching line weight to relative at a zoomed-in view more than doubled rendered stroke coverage while leaving dash lengths unchanged.

### Phase 33: Crash Diagnostic Journals (2026-08-13)
- [x] **`Diagnostics/Journal.cs`** — synchronous, auto-flushed, line-oriented writer to `%TEMP%\DoodleSharp\YYYYMMDDhhmmss.log`, one file per process (`FileMode.CreateNew` + `-N` suffix on same-second collisions). Each record: timestamp, monotonic sequence, uptime, thread, level, repo-unique **site key**, compiler-captured `File.cs:line Member`, message, data. Timed `Scope()` (ENTER/EXIT + elapsed, >2 s promoted to WARN), `Activity()` counters for hot paths, `RegisterStateProvider`/`CaptureState`, `WriteBlock` for source dumps, `DescribeFile`/`ShortHash` for content fingerprints. 64 MB cap, 30-day/60-file retention, `DOODLESHARP_JOURNAL`/`_LEVEL`/`_SYNC`/`_DIR` env overrides.
- [x] **`Diagnostics/SystemSnapshot.cs`** — machine facts without WMI: OS/.NET/CPU/RAM/disk/locale/elevation, every loaded assembly with version and path, **display adapter name + driver version/date from the registry**, and per-heartbeat process counters including GDI/USER handle counts via `GetGuiResources` (the classic precursor to a "random" WPF death at the 10,000-handle limit).
- [x] **`Diagnostics/AppDiagnostics.cs`** — one `Install()` call attaches `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, throttled `FirstChanceException`, `AssemblyLoad`, `ProcessExit`/`Exit`/`SessionEnding`; probes WPF render tier and screens; runs a UI-thread hang watchdog (dispatcher ping, 5 s threshold, hang/recovery records).
- [x] **Help menu access** — `Open Diagnostic Journals` (captures a fresh state dump, then opens `%TEMP%\DoodleSharp`) and `Copy Current Journal Path`.
- [x] **Tests** — `Tests/JournalTests.cs` (17 behavioural tests: naming, header contents, record format, single-line sanitisation, exception chains, level filtering, scopes, state providers incl. a throwing one, file description, truncation, zero-write `Activity`) and `Tests/JournalSiteKeyTests.cs` (repo-wide scan enforcing site-key uniqueness, format, and that the critical paths stay instrumented). Full suite: 254 passing.

### Phase 40: Slice Correctness (2026-08-15)
- [x] **`VPolygon.Slice` rewritten as two half-plane intersections** through `PolygonClipper`. The old code walked the perimeter, paired intersections in *perimeter* order and closed each piece with one chord — which assumes every output piece is one arc plus one chord, true only for a convex cut with exactly two crossings. A concave notch straddling the line gives four crossings: it emitted the arcs between intersections 0-1 and 2-3, discarded 1-2 and 3-0, and **could not represent the remaining piece at all** (that one is bounded by two arcs). Reported case: a parcel of 225,561 came back as two slivers totalling 12,945 — 94% of the area gone, with no error, because each sliver was itself a valid polygon. Now area-preserving by construction; the four-crossing cut correctly returns three pieces. CLAUDE.md note 73.
- [x] **Half-planes built under `Shape.SuspendAutoRegistration()`** (note 64) or every slice would drop two enormous phantom rectangles on the canvas; extent measured from the **line's origin**, not the polygon's size, since the defining points may sit far outside it; **fewer than two pieces means the line never separated anything** (missed, or grazed a vertex or a whole edge), so the documented clone-the-original contract still holds.
- [x] **`FindSliceIntersections`/`BuildSlicedPolygons`/`BuildSlicedPolygonsGeneral` deleted** (244 lines). Don't reintroduce a perimeter walker.
- [x] **`Tests/PolygonSliceTests.cs`** — 12 cases: the reported parcel verbatim, a synthetic notched polygon asserting the exact 1200/1200/6400 split, convex and diagonal cuts, the line-is-infinite case, miss / edge-graze / vertex-graze / coincident-points, style inheritance, and a registry probe asserting the clip rectangles never reach the canvas. Every case asserts the pieces sum back to `Area`. Suite 495 → 496.
- [x] **Undo after a canvas delete survives the run it triggers** — deleting edits the source, which starts the debounced auto-run, and every run used to `Clear()` the history including the entry just pushed. `ICommand.SurvivesCodeRun` + `TransactionManager.PruneAfterCodeRun()`. CLAUDE.md note 65, `Tests/UndoSurvivesRunTests.cs`.

### Phase 38: Geometry API Correctness (2026-08-13)
Closes the seven items Phase 37 left open for a decision, plus six the work surfaced.
- [x] **`Operations/CurveGeometry.cs`** + `Contains`/`DistanceTo` overrides on VLine, VPolyline, VArc, VBezier, VSpline, VPolygon, VCircle, VEllipse, VHatch, Region, VRay, VXLine. Exact for line/arc/circle/xline/ray, sampled for bezier/spline/ellipse. Open curves mean "on the stroke"; area shapes get an interior test and a boundary distance. CLAUDE.md note 54.
- [x] **`Tests/ShapeOverrideConsistencyTests.cs`** — reflection guard with **per-method** exemption lists plus a self-check that an exemption is never claimed for a shape that does implement the method. Caught `VRay`/`VXLine` (unreported) and `VGroup` (wrongly listed by me).
- [x] **`Shape.RotationAngle` virtual, `VRectangle` overrides** — the `new` shadow meant `RotateAnimation` on a rectangle did nothing. CLAUDE.md note 55.
- [x] **`VGrid`** nullable `ySpacing` + no default on the uniform overload's `centered`. CLAUDE.md note 53.
- [x] **`VEllipse`** arc-length parameterisation via a memoised cumulative table; `EvaluateByAngle`; `SetBounds` trims to match. CLAUDE.md note 56.
- [x] **`GeometryDiagnostics.Sink`** wired in both apps; `ChartOptions.ShowLegend` implemented for Bar/Pie; `BuiltInHatches.Get` returns `Clone()`. CLAUDE.md notes 57–58.
- [x] **Found by re-reading rather than from the list** — `VCircle`/`VHatch`/`Region` had an exact `Contains` with a bbox `DistanceTo`; `VEllipse` had neither; the `BooleanOps.Union` diagnostic named a nonexistent `UnionAll`; `Region.DistanceTo` ignored holes while `Contains` excluded them; `VHatch.Contains` duplicated the point-in-polygon test.
- [x] Suite 338 → 384. Docs updated by the `docs-author` agent across all four surfaces (two passes), with post-pass drift on `Region.DistanceTo` corrected directly.
- [ ] **Known limitation** — `VEllipse.Contains` assumes an axis-aligned ellipse, true only because there is no rotation property. Adding one silently invalidates it.

### Phase 37: Documentation Ownership + Full API Pass (2026-08-13)
- [x] **First pass, run as three parallel agents** — shapes/core/styling; operations/charts/hatches/parameters; animation/console/sketch. Every public type and member documented with a runnable example.
- [x] **Code defects found by the pass and fixed** — unclamped `t` in five `Apply` methods (queued animations rendered early under even-powered easings); `new VCircle` temporaries in three `CurveIntersection` arc methods registering phantom shapes (added `VCircle.Internal`); `VHatch`/`VRadialDimension`/`VSpatialGrid` missing from `AnimationNameRewriter.ShapeTypes`; `GetClosestCell` requiring a drawable `VPoint` (added a `VXYZ` overload); `VXYZ.AsVPoint`'s comment falsely claiming it avoids registration.
- [x] **`Tests/DocGeneratorTests.cs`** — constructs `DocGenerator` and renders every type page, guarding the duplicate-key crash that took out F1 Help once before and is now much likelier with several authors editing the dictionaries. Plus `AnimationTimingTests` (8) and three arc-intersection pollution tests. Suite: 324 → 338.
- [ ] **Open, needs a decision rather than a patch** — `VGrid`'s uniform-spacing ctor is unreachable (CS0121 between two overloads); `VLine` overrides neither `Contains` nor `DistanceTo` (both fall back to bounding-box behaviour, as do VPolyline/VArc/VBezier/VSpline); `VRectangle.RotationAngle` shadows `Shape.RotationAngle` via `new`, so meaning depends on the variable's static type; `VEllipse` parameterises by sweep angle not arc length, unlike every other `ICurve`; `BooleanOps.Union` reports failures via `System.Console.WriteLine`, which goes nowhere in a WPF app; `ChartOptions.ShowLegend` is read by nothing; `BuiltInHatches.Get` hands every caller the same mutable instance.

### Phase 36: Error Squiggles for Incomplete Code (2026-08-13)
- [x] **`Editor/DiagnosticRange.cs`** — resolves a diagnostic's line/column span to an underlinable range, widening empty spans (forward over an identifier, else backward over the preceding token, else one character, never the line break). Confirmed by probe against the real compiler: a bare `for` yields 7 diagnostics, **all** with `SourceSpan.Length == 0`, and every marker loop required `length > 0` before drawing *or* counting — hence no squiggle and "Ready" in the status bar for unbuildable code.
- [x] **Error counting decoupled from marker rendering**, and diagnostics resolving to the same range merged into one marker with a combined tooltip (7 overlapping squiggles otherwise).
- [x] Applied to all three marker loops: the realtime syntax check and both Run-path loops.
- [x] **`Tests/DiagnosticRangeTests.cs`** (7) including an end-to-end case that feeds real compiler diagnostics through the resolver. Suite: 317 → 324.
- [ ] **Not verified in the running app** — synthetic input remained blocked, and the external-edit route does not work either (see the `RefreshFilesFromDisk` gap, CLAUDE.md note 52).

### Phase 35: IntelliSense Behaviour Fixes from Live Testing (2026-08-13)
Nine issues reported from hands-on use of the Phase-34 build. Diagnosed by probing the completion service directly rather than by inspection — two of the causes were not what the symptoms suggested.
- [x] **`Editor/CompletionInteraction.cs`** — space no longer commits (`new ` → `VXYZ `, `new VXYZ(10, ` → `new VXYZ(10,Viz )` were both this). Explicit commit set `( [ { ; , )`; space dismisses and reopens after `new`/`is`/`as`. Extracted as pure rules for testability. CLAUDE.md note 48.
- [x] **`IsDeclaringAName`** — no completion while typing the identifier of a variable/parameter/foreach/method/type/property declaration.
- [x] **`AddKeywords`** — C# statement keywords injected in statement position (not after `.`/`new`), so `int` and `for` match exactly instead of ranking `IntersectionResult`.
- [x] **Window opens on symbols *or* snippets** — a half-typed `for` resolves no symbols, so the snippet was unreachable at the moment it was needed. Snippets are also prefix-filtered when built.
- [x] **Signature help expands to the full overload set** and gained a workspace overload; `SymbolInfo.Symbol` vs `CandidateSymbols` meant working code showed one signature and broken code showed all three. CLAUDE.md note 50.
- [x] **Ctrl+Space force-closes any open list first** so an explicit request always re-queries.
- [x] **Tests** — `CompletionServiceTests` 7 → 16 (all-caps types, expected type, declaration names, keywords, keyword suppression after dot/new, overload expansion), new `CompletionInteractionTests` (11). Suite: 280 → 317.
- [ ] **Interactive pass not completed this session** — Windows refused synthetic input (both SendInput and WM_CHAR) partway through, so the keystroke-level behaviour is covered by tests but was not re-verified in the running app.

### Phase 34: IntelliSense & Quick Action Correctness (2026-08-13)
- [x] **`.vizproj` file-association startup** — `App.OnStartup` parses `e.Args` for an existing `.vizproj` (case-insensitive, resolved to a full path, non-project args skipped) and opens `MainWindow` with it, adding it to recent projects; failures show the reason and fall back to the welcome window. Verified live for both paths: a valid project opened straight to StartViz.cs in ~1 s with no welcome screen, and a corrupt one showed the error then the welcome window. `Tests/AppStartupTests.cs` (7).
- [x] **`ModuleCompiler.CreateCompilationAsync(project, forExecution = false)`** — replaces `injectStackGuards`. Both source rewriters (`AnimationNameRewriter`, `StackGuardRewriter`) now run only for execution; the default is offset-faithful so a caller that forgets the flag gets correct offsets rather than silent corruption. Fixes F12/Shift+F12 resolving the wrong token and rename writing at shifted offsets into other files.
- [x] **`RefactoringProvider.ResolveGenerationTarget` / `FindMemberInsertionPoint` / `GenerationTarget`** — binds the invocation receiver (type symbol ⇒ static member on that type; value ⇒ instance member on its type; no receiver ⇒ enclosing type) and derives the insertion file/offset/indent from `DeclaringSyntaxReferences`. Inserts at the *start of the closing brace's line* (the brace's indent is leading trivia — inserting at the token strands a whitespace line and un-indents the brace, observed live). Metadata types set `IsInSource = false` and the action is withheld.
- [x] **`Editor/MethodStubBuilder.cs`** — signature, accessibility, indentation and placement built outside WPF so they are testable; `MainWindow.GenerateMethodFromQuickAction` applies it via the document (same file) or the file's in-memory content plus tab open (other file), and syncs the workspace so the new member completes immediately.
- [x] **Completion dropouts** — dot-while-open closes the stale list and re-triggers from `TextEntered` only (triggering from both handlers raced two async queries); `_completionWindow` published only after `Show()` plus a `_completionRunning` gate, so an internal error can no longer disable completion for the session; `ShowCompletionWindowWithSelection` clears the field on its empty early return.
- [x] **Workspace file tracking** — `SyncCompletionWorkspaceFiles()` after New File and file-watcher refresh, outgoing-file push in `SelectFile`, and `CachedCompilationWorkspace.GetFileIds()` to reconcile removals (`RemoveFile` previously had no call sites).
- [x] **Member-access classification** — `GetSymbolInfo` before `GetTypeInfo` (a class name reports a type from both, so static access was being treated as instance access and its statics filtered out); instance access hides statics except reduced extension methods and nested types; unbindable receiver returns an empty list instead of the global lookup; `ShouldHide` no longer suppresses `ToString`/`Equals`/`GetHashCode`/`GetType` or type parameters.
- [x] **Auto-popup + commit-on-non-identifier** in DoodleSharp (`IsAfterCompletionKeyword`, `RequestInsertion`), matching `SharedEditorController`.
- [x] **Editor context menu** gains Go to Definition / Peek Definition / Find All References / Quick Actions / Rename Symbol; "Move type to new file" detection moved from regex + brace counting to the syntax tree; quick-action failures surface in the status bar instead of an empty menu.
- [x] **`RefactoringProvider.Workspace`** — quick actions and navigation reuse the editor's `CachedCompilationWorkspace` instead of rebuilding the compilation and running a NuGet restore per invocation.
- [x] **Tests** — `RefactoringProviderTests` (8), `CompletionServiceTests` (7), `EditorWorkspaceTests` (4). Suite: 254 → 273 passing.
- [x] **Verified end to end in the live GUI** on the reported repro via UI Automation: the action reads "Generate method 'DrawVector' in VectorManager", writes a correctly indented `public static void DrawVector(VXYZ arg0)` into VectorManager.cs leaving StartViz.cs untouched, and the following auto-run reports the stub's `NotImplementedException` instead of CS0117.

---

## Implementation Statistics

| Category | Count |
|----------|-------|
| Shape classes | 15 |
| C# files created | 50+ |
| XAML files modified | 10+ |
| NuGet packages | 3 |
| Keyboard shortcuts | 30+ |
| Canvas features | 12+ |

---

## Time Allocation (Estimated)

| Phase | Effort |
|-------|--------|
| Project Setup | 5% |
| Core Geometry | 15% |
| Extended Geometry | 10% |
| Canvas Implementation | 25% |
| Shape Rendering | 15% |
| Code Editor | 10% |
| Script Execution | 5% |
| Main Window UI | 10% |
| File Operations | 3% |
| Bug Fixes | 2% |
