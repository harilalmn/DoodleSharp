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
- [x] Alt+Shift+F - Format code

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

### Phase 47: The Completion List Opens on the Type You Asked For (2026-08-20)
- [x] **`Editor/CompletionPreselect.cs`** — `IndexOf(items, expectedType)` picks the row to highlight when the window opens: an exact ordinal name match, else row 0. `RoslynCompletionService` had returned the expected type at the caret as the fourth value of its tuple all along, and **both hosts discarded it with `_`** once note 115 removed the ranking. So `VXYZ p1 = new ` opened highlighted on `AccessViolationException` with `VXYZ` several hundred rows below the fold — the list was right, the highlight was useless, and Tab inserted the wrong type. Reported from hands-on use with a screenshot.
- [x] **Not note 115 being walked back.** Note 115 is about an order you scan by eye; which single row starts selected is a different question, and Visual Studio splits it the same way — preselect the expected type, never reorder for it. `Tests/CompletionOrderingTests.cs` still fails on an `expectedType` ordering key inside `SortCompletions`, and that guard is untouched. A snippet at row 0 still keeps the selection (note 101); in practice the two never compete, since no snippets are offered after `new`.
- [x] **`SelectedItem` highlights a row but does not scroll to it** — the first version selected `VXYZ` off-screen and looked exactly like the bug it fixed. `ListBox.ScrollIntoView` from the window's `Loaded` handler, where the list finally has a layout.
- [x] **Both editor implementations changed** (note 43) — `MainWindow.ShowCompletionWindowWithSelection` and `SharedEditorController.ShowCompletionWindow`.
- [x] **`Tests/CompletionPreselectTests.cs`** — 7 cases: `IndexOf` unit coverage (exact/ordinal match, snippet precedence, absent type, empty list), one end-to-end pass asserting the real alphabetical list genuinely opens on something other than `VXYZ` before preselection moves it, and a source scan over both hosts for the `_`-discard coming back. CLAUDE.md note 122.

### Phase 46: Auto-Run, and the Wheel Interactive Mode Took (2026-08-20)
Two requests, one of which turned out to be a defect. Both are about the canvas doing what the user
expects while their own code is in charge of it.
- [x] **Wheel zoom survives a mouse handler** — registering *any* `Mouse.*` handler put the canvas into interactive mode, which suppressed selection, double-click-zoom-to-fit **and the wheel** together. So a sketch that merely watched clicks lost the main way to move around a drawing larger than the viewport, with only the floating nav buttons left. The canvas now stands aside only when user code has explicitly claimed the wheel with `Mouse.OnWheel`, and `Mouse.OnWheel(null)` hands zoom straight back. Everything else about interactive mode is unchanged. New public `Mouse.HasWheelHandler` (a `volatile bool` maintained beside `_hasHandlers`, for the same reason that one is). CLAUDE.md note 95, `Tests/MouseWiringTests.cs` + `Tests/MouseCallbackTests.cs`.
- [x] **The status-bar hint had to move with it** — it read "Mouse: your code | Middle-click: Pan", which is now wrong in the common case; it reports scroll separately when the wheel is not claimed. A hint that describes gestures is only worth having while it is true.
- [x] **Auto-Run — re-run the project every 500 ms** — a checkbox beside Run, off by default, saved per project as `AutoRun` in the `.vizproj` so an armed project comes back armed and every other project is untouched. Ticks are dropped rather than queued while a run is in flight, the timer stops on window close, and the toggle is guarded by `SettingsUiBusy` with no `IsChecked` in markup (note 103). CLAUDE.md note 121, `Tests/AutoRunSettingTests.cs`.
- [x] **It is per-project and opt-in precisely because note 120 deleted its predecessor** — auto-update ran on every keystroke, was on by default and applied to every project. `Tests/AutoUpdateRemovalTests.cs` now pins **three** silent-run callers rather than two, and states in the file why the third is not the old feature returning, so a fourth has to be argued for there.
- [x] **A tick recompiles only when the source actually changed — a correctness fix, not an optimisation.** `CompileAndExecuteAsync` clears the canvas **before** it compiles, so recompiling every tick left the drawing blank for most of each 500 ms cycle: the first working build drew nothing you could see while the status bar cheerfully said "3 shapes". An unchanged tick re-invokes the resident assembly instead — clear and re-execute microseconds apart, ~16 ms measured — which is the same mechanism that has always made a Global Parameters slider drag look smooth. `ReExecuteResidentSilentlyAsync(label)` is **shared** with that path rather than copied (notes 43/92).
- [x] **Switching projects through the New/Open dialog never reloaded the Settings tab** — a pre-existing bug, found because Auto-Run made it consequential: the tab kept showing the previous project's values, which with a timer attached would have meant the previous project's Auto-Run staying armed. One `LoadSettingsToUI()` call, placed where the other project-open path already had one rather than hooked onto each of the four `_currentProject =` sites (note 39's self-healing preference).
- Worth keeping: **the feature was verified live, and had to be.** None of this is observable from a unit test — a probe project with `AutoRun: true` and a `DateTime.Now` clock, captured twice four seconds apart, showed the checkbox restored from the `.vizproj` and the clock reading 09:07:39 then 09:07:44 with the second hand moved. It is also what exposed the blank-canvas problem above, which every test in the suite was happy with.
- Worth keeping: **the first probe drew nothing, and that was the probe's fault** — bare `new VCircle(...)` statements are anonymous, so `HideUnnamedShapes` hid all three, and the warning went to a console pane that was closed. Assign to a `var`. Half an hour went into suspecting the render backend before reading that method.

### Phase 45: Reserved Names — Shadowing, and Saying So Where It Helps (2026-08-18)
Reported as a project named **Mouse** that could not call `Mouse.OnMove(...)`. One fix stops the
collision happening; the other makes it legible when it happens anyway.
- [x] **A project named after part of the API could not use that API** — the project name becomes the namespace of every generated file, and C# searches the **enclosing namespace declaration before any `using`**, so inside `namespace Mouse` the identifier `Mouse` binds to the user's own namespace and `DoodleSharp.Animation.Mouse` is unreachable by its short name: **CS0234, "the type or namespace name 'OnMove' does not exist in the namespace 'Mouse'"**. The same trap applied to `Frame`, `Canvas`, `VCircle`, `Shape`, `Console`, `Math`, `List` and every other imported type, and to C# keywords. `Templates.SanitizeIdentifier` now renames a colliding project name (`Mouse` → `MouseProject`). Note 108's `DoodleSharp.Canvas` shadowing is the same failure from the other direction, and its guard could not catch this one: it pins what the templates *import*, while this collision comes from what the project is *named*. CLAUDE.md note 111.
- [x] **The reserved set is reflected, not hard-coded** (`Project/ReservedNames.cs`) — walks the exported types of the namespaces the templates import, so it cannot go stale as the API grows. Arity stripped (`List\`1` → `List`), comparison **ordinal** because C# is case-sensitive (a project named "mouse" shadows nothing and is left alone), and the assembly scan is **seeded explicitly** so that whichever assemblies happened to be loaded at first use cannot decide whether a name is caught.
- [x] **Renaming the namespace would have stranded every project already on disk**, so `ModuleCompiler` falls back to `FindEntryTypeByScan` when `{sanitized name}.Viz` is absent. This also fixes a pre-existing complaint in its own right: renaming a namespace by hand, or renaming the project directory, used to break Run — precisely the workaround a user hitting the shadowing bug would reach for.
- [x] **Ctrl+N produced a file that could not compile** — `EmptyModuleTemplate` took both names raw, so the default `Untitled-1` became `public class Untitled-1`, and a project name with a space produced an invalid namespace. Both now go through `Templates.GetEmptyModuleTemplate`.
- [x] **`Tests/TemplateNamespaceTests.cs`** — compiles the real template plus a `Mouse.OnMove(...)` call against `ModuleCompiler`'s reference set, **paired with a negative control** that puts the shadowing namespace back and asserts CS0234 returns. Without that control the positive test would pass just as happily if the API call had silently stopped being exercised. 16 cases.
- [x] **The error named the one token that was not wrong** — Roslyn blames the token it failed to look up, so shadowing `Mouse` underlined **`OnMove`** and never mentioned the declaration that has to change. `Execution/ShadowedNameDiagnostics.cs` remaps those onto the declaration's identifier as **`Mouse is a keyword. try another name`**, once however many uses it broke, cause listed first. Covers namespace (every dotted segment), type, delegate, property, local, field, parameter, `foreach` and pattern designations. CLAUDE.md note 112.
- [x] **The `Diagnostic` objects are remapped, not the formatted string** — the console message, the status-bar error count and the editor squiggles all read the same `CompilationResult.Diagnostics`, so rewriting that one collection fixes three surfaces at once; rewriting the text would have fixed only the console and left the squiggle under `OnMove`. Wired at all three compile sites: both `ModuleCompiler` paths and `SharedEditorController` (note 43 — a parallel implementation, so the fix belongs in both).
- [x] **Three conditions gate every remap, and none is redundant** — the name is declared in user source (the cheap gate that makes a healthy compile return the input untouched); the error is one of the four lookup failures shadowing produces (`CS0234`/`CS0426`/`CS0117`/`CS1061`), so a wrong argument count stays a wrong argument count; and the qualifier **binds to that source declaration** rather than the library type, asked of the semantic model. The third is what separates "you shadowed `Mouse`" from "you typo'd a member of the real `Mouse`", and is why the message text is never parsed.
- [x] **`Tests/ShadowedNameDiagnosticsTests.cs`** — 20 cases. The two that matter most are the **negative control** (the raw compile, asserting Roslyn really does underline the use site) and the **over-reach** pair (an ordinary typo against the real API, and an unrelated error on a shadowed name, both keeping their own diagnostic). The run path is driven **separately** from the syntax-check path rather than assumed equivalent, because only the former compiles with `forExecution: true` and so locates the declaration in a rewriter-shifted tree (note 41). Suite 829 → 865.

### Phase 44: Two Silent Losses — Batched Strokes and Curve Intersections (2026-08-18)
Both reported from the same user project, both invisible from the call site: the work was done
correctly and then thrown away without an error.
- [x] **Every batched stroke after the first flush was dropped** — `StrokeBatcher.Add` enrolled a pen in `_order` only when it *created* that pen's bucket, but `Flush` clears `_order` while deliberately keeping the buckets (their lists are reused so a frame doesn't allocate one per pen). From the second flush onward `TryGetValue` succeeded, the pen was never re-enrolled, and `Flush` iterated an empty list — drawing nothing, and never clearing the segments, so they grew without bound. Affected `VLine`, `VPolyline` and unfilled `VRectangle`/`VPolygon` (both of `Flush`'s branches live inside that loop, so short runs lost their `DrawLine` calls exactly as long runs lost their geometry), and it bit *within* a frame too, since the render loop flushes whenever an unbatchable shape appears. The shapes stayed culled in, tessellated, selectable and correct in the Properties panel — they simply were not painted. CLAUDE.md note 109.
- [x] **`Tests/StrokeBatcherTests.cs`** — the class had **no tests at all**, which is how it shipped. Six cases: every frame draws (not just the first), a second run in the same frame draws, short runs draw every frame, nothing is left held after a flush, pens emit in first-segment order, `Reset()` leaves the batcher usable. All six fail against the original `Add`.
- [x] **`Shape.DoesIntersect` reported no intersection for almost every pair** — `Shape.Intersect(Shape)` returned null by default and only four types overrode it (`VLine`, `VRectangle`, `VPoint`, `VGroup`), while `DoesIntersect` is defined as `Intersect(other) != null`. Ray/circle, circle/circle, arc, polyline and polygon pairs all came back false while `ICurve.Intersect(ICurve)` on the same pair returned real points. Overload resolution hid it: a member declared on the derived type beats one inherited from `Shape`, so `ray.Intersect(circle)` bound to `VRay.Intersect(ICurve)` and worked while the adjacent guard did not. Both now defer to `CurveIntersection` for curve pairs. CLAUDE.md note 110.
- [x] **`VRay`/`VXLine` intersections were sampled into a million segment pairs** — no dispatch case, so they fell through to `IntersectGeneric`. `GetSegments` caps at `Min(Max(2, (int)(length * 10)), 1000)` and `VRay.GetLength()` is infinity, which **saturates to `int.MaxValue`** on the cast since .NET Core 3.0 — so the ray became 1000 segments, the circle 1000, and one query cost 65 ms. The reported 359-ray loop took over two minutes. Both types are finite over `RenderExtent`, exactly the span `Divide` samples, so they now convert via the existing `ToFiniteLine()` and re-enter the switch to reach the exact closed-form routines: same coverage, analytic rather than chord-approximated, **65 ms → 0.35 µs**, and the loop 154 s → 0.9 ms with identical answers.
- [x] **`Tests/ShapeIntersectionTests.cs`** — 8 cases: the two APIs agree across curve types in both argument orders, misses stay misses, the `Intersect(Shape)` shape-kind contract (`VPoint` for one crossing, `VGroup` for several), **zero registrations during a query** via a counting registry, ray and equivalent line agree to 9 decimals, and a timing assertion that fails if the generic sampling path returns. Five fail against the original code. Suite 819 → 827.
- [x] **`RayCaster` was indexing the construction guides it documents as excluded** — found by the `docs-author` agent reading the paths side by side, not by a bug report (the note-92 pattern). `VRay`/`VXLine` override `GetBounds()` to return a **finite** box derived from `RenderExtent`, so `IsFiniteBox` accepted them and the "non-finite bounds are skipped" claim in the XML doc, in CLAUDE.md note 9 and in the README was simply false. Neither is in the inline exact-math set, so a hit on one was a hit on its **AABB**: a 45° `VXLine` probed horizontally at y=10 answered `(-50, 10)` — the query ray's own origin — where the true crossing is `(10, 10)`, and being nearest it beat the real geometry behind it. Now excluded by an explicit type test, with the reasoning recorded at the call site so nobody "simplifies" it back into the bounds check. `Tests/C2VRayCasterTests.cs` +2. Suite 827 → 829.

### Phase 43: Defects Surfaced by the Documentation Audit (2026-08-17)
Five code defects the audit found by reading paths side by side, none of which had been reported —
each is invisible from any single call site.
- [x] **Arrowhead geometry: five implementations, four of them wrong** — `RenderCanvas.DrawArrow` and `VArrow.GetArrowheadPoints` hard-coded a `HeadLength / 6` perpendicular half-width (≈9.46° half-angle) and **never read `HeadAngle`**; `ShapeTessellator` honoured it but drew an open V; `PdfExporter` honoured it *and* clamped the head to 20% of the shaft; `DxfExporter` hard-coded both 30° and `min(length × 0.2, 10)`, ignoring `HeadLength`. So `HeadAngle` did nothing on screen, silently changed raster/GPU/PDF/DXF output, and one arrow's head differed in shape *and* size by backend. `VArrow.ArrowheadWings` is now the sole implementation; closed triangle everywhere. Note 68's failure repeated — per-renderer geometry drops a property with nothing failing. **Arrowheads are now visibly wider**, the documented 30° default finally applying. CLAUDE.md note 92.
- [x] **Dimension arrowheads: the same bug, independently** — tessellator at a hard-coded 20°, canvas/SVG/PDF each at `ArrowSize / 6`. Now `VDimension.DimensionArrowAngleDegrees` (public const 20) in all four.
- [x] **`DoubleEnded` dropped by three exporters** — PDF, SVG and DXF each drew only the end head, silently losing the start head of a double-ended arrow.
- [x] **`Tests/ArrowheadConsistencyTests.cs`** — 15 cases: wings at `HeadAngle` for four angles, wing length, straddling, an explicit assertion that the angle is *not* the old ≈9.46° ratio, the double-ended direction, degenerate-input safety, the extension-line span, the obsolete marker, and a **source scan of all five files** that fails on a reintroduced `/ 6.0;`. A behavioural test alone cannot catch a sixth copy in an unrendered path, which is how five accumulated.
- [x] **`VDimension.ExtensionLength` is `[Obsolete]`, not deleted** — inert since it was written (declared, cloned, scaled, read nowhere); `OffsetFromOrigin`/`Offset`/`ExtendBeyondDimLines` already fix an extension line's length completely. Deprecated so existing code compiles, per note 70's precedent; no longer scaled, still cloned under a local pragma.
- [x] **Overlay chrome no longer captured by exports** — the overlay is a visual child of `RenderCanvas` and every export renders the canvas, so the F10 readout, selection handles, rubber band, snap markers and measuring overlay were baked into exported PNG/GIF/MP4. `SuppressOverlayForCapture()` is an `IDisposable` held across each whole export (the letterbox paths capture more than once), and `RedrawOverlay` checks the flag itself so a mid-capture repaint cannot restore the chrome. CLAUDE.md note 93.
- [x] **`RenderBackend`: undocumented fourth value, and no UI whatsoever** — `"GPU"` was always honoured but absent from the property's own doc, and no XAML referenced the key, so it was reachable only by hand-editing `appsettings.json`. Now a dropdown under `Settings > Application Settings > Rendering`; unrecognised values behave as `Auto` and the combo mirrors that. Changing it calls `Refresh()` because backends differ in layer ordering (note 83). CLAUDE.md note 94.
- [x] **`RegionBooleanOps.Union` ignored `segmentsPerCurve`** — `Intersect`/`Difference`/`Xor` folds all took it, so union was the one operation discarding the caller's precision. Added to the `IEnumerable<Region>` overload; the `params Region[]` form cannot take it (an optional parameter cannot follow `params`). Guarded behaviourally: a coarse fold must yield less area than a fine one.
- [x] **The same defect one layer up, found by the docs agent** — all eight `Region` overloads on `BooleanOps` were **bare forwards** to `RegionBooleanOps`, so the precision argument existed on the canonical API but was unreachable through the convenience facade: reaching a region boolean the documented-easy way silently pinned sampling to the default. All eight now take `segmentsPerCurve`, and `RegionBooleanOps.DefaultSegmentsPerCurve` was promoted from private to `public const` so the default is nameable and cannot drift between the two entry points. Worth noting the audit found this *because* the README author was checking a signature — the same reading that finds fabricated API finds unreachable API.
- [ ] **Outstanding: arrowhead fill still differs by backend** — geometry matches exactly (verified by offscreen render at `Legacy` vs `Managed`), but the canvas fills the triangle with the stroke colour while the raster/GPU sinks stroke its outline. `IPrimitiveSink.EmitFilledLoops` fills with `PenSpec.FillColor` (`Transparent` on a default arrow), so the API cannot currently express "fill in the stroke colour". Needs a sink API addition; recorded in `docs/TODO.md`.
- [x] **`Tests/ExportFidelityTests.cs`** — 6 cases covering the suppression scope's existence and signature, a per-method source scan that each of the three export paths uses it, and the `Union` parameter both structurally and behaviourally. In the `"CanvasState"` collection per note 9, since building Regions touches the registry statics.
- [x] Suite 594, all green.

### Phase 42: Documentation Audit + F1 Help Rendering Fix (2026-08-17)
- [x] **The audit is mechanical, per note 62** — the built `C2VGeometry.dll` and `DoodleSharp.dll` are dumped by reflection (95 public types, every ctor/property/field/event/method with parameter names, defaults, interfaces, enum values and `[OBSOLETE]` markers), then every `Type.Member` key in `README.md` and `DocGenerator.cs` is diffed against that dump in both directions. Prose review does not catch this class of defect; a diff does.
- [x] **8 documented members do not exist** — `SvgExporter.ExportToString`/`Width`/`Height`, `PdfExporter.Margin`/`PageSize`, `GifEncoder.FrameDelay`/`Repeat`/`Save`. The pattern is instructive: several are **constructor parameters documented as properties** (`GifEncoder(…, int frameDelayMs, bool repeat)`), and `SvgExporter`'s `width`/`height` are **parameters of `Export`**. `SvgExporter` also lives in `DoodleSharp.Canvas`, not `DoodleSharp.Export`, and its real `SaveToFile` was undocumented.
- [x] **391 real user-facing members had no description**, concentrated in `ColorName` (83 values), `BuiltInHatch` (73), `VFont` (15), `VEllipse` (13), `VGroup` (12), `VSpline`/`VBezier` (10 each) and the `DoodleSharp.Console` types.
- [x] **`DocGenerator` was hiding most of the API** — member tables reflected with `Public | Instance | DeclaredOnly`. No `Static` meant all **23 static classes** rendered an empty page and every static factory was invisible, leaving **339 already-written descriptions unreachable** — which is why it went unnoticed, since the dictionaries looked healthy and only the rendered page was empty. Enums declare neither properties nor methods, so all 12 listed nothing at all. Const fields were dropped too. Fixed: shared `DocGenerator.MemberFlags` (used by `HelpWindow`'s search index as well, so page and search cannot drift), dedicated `Values`/`Fields` sections, staticness marked in the signature column. CLAUDE.md note 91.
- [x] **`Tests/DocGeneratorTests.cs`** — 21 new cases rendering real pages and asserting the members appear: static classes, static factories on instance types, enum values, constants. 3 → 24 tests.
- [x] **Two shipped features were documented nowhere** — the F10 frame-timing readout and the Direct3D backend / `RenderBackend` setting.
- [x] **Two further help-engine defects, found by the agent while filling the gaps** — (a) `GetDocumentableTypes` filtered on `IsClass || IsAbstract`, which covers classes and interfaces (an interface is abstract in metadata) but **excluded every enum and struct**: 15 enums and 5 structs had no page in the tree at all, so the enum-value rendering above was unreachable *as written*, and the tests passed because they call `GenerateDocForType` directly. Reachability and rendering are separate failures needing separate assertions — `EnumsAndStructsAreReachableInTheTree` closes it. (b) The inherited-description fallback tried a hard-coded `Shape` then `ICurve` for **every** type, so any unrelated type with a member named `Name`/`Color`/`Move`/`Intersect` could display someone else's description; now walks the member's real declaring type and interfaces. Also page titles and `GenerateSyntax` said "X Class" for enums and structs.
- [x] **`README.md` + `DocGenerator.cs` passes delegated to the `docs-author` agent** (two agents, disjoint file ownership so they ran in parallel), per the standing ownership instruction. `CHANGELOG.md`, `CLAUDE.md` and `docs/*` updated by the main session. Final state independently re-verified from a fresh reflection dump: **127 summaries, 86 samples, 1478 member descriptions; 0 fabricated, 0 undocumented in scope, 0 duplicates.** Suite 603.
- [x] **Coordination lesson** — the main session briefed both agents that `VArrow.HeadAngle` was inert, then fixed it mid-flight, which made the briefing wrong. Correcting a running agent is cheap; letting it finish and publish the stale claim is not. Two agents editing the same file would have been worse — disjoint file ownership is what made the parallel run safe.

### Phase 41: GPU Backend, Per-Frame Callbacks, Exporter Coverage (2026-08-16)
- [x] **`D3D11RasterBackend`** — geometry uploaded once in world coordinates; pan/zoom rewrite a 64-byte constant buffer while the GPU re-transforms and re-clips. The only backend with flat frame time across navigation, and the only one viable at 4K (the CPU paths copy 8 MB at 1080p / 33 MB at 2160p every frame, over budget before drawing anything). 3840×2160 / 100k shapes: city-grid 2.89–3.91 ms; mixed-cad worst frame 120.9 → 44.9 ms. **No `unsafe`** — verified by spike before any code was written, so `AllowUnsafeBlocks=false` stands. Fails soft: hardware → WARP → unavailable-with-a-reason. Vortice ships managed-only assemblies, so eight explicit `installer.iss` lines. Residual cost is text (~2,700 `FormattedText` labels), needing a GPU glyph atlas. CLAUDE.md note 88.
- [x] **`Animation/Frame.cs`** — `Frame.Request(callback)`, the requestAnimationFrame model. Two queues swapped per pump (draining one in place re-enters forever, which *is* the self-rescheduling idiom, so not an edge case); `Frame.Clear()` before every execution or a queued delegate pins the collectible ALC; a throwing callback stops the loop rather than reaching WPF's dispatcher 60×/second. The timeline stays — `Animation.Apply(t)` is pure in normalised time and therefore seekable, which the scrub bar and both video exporters depend on. CLAUDE.md note 90, `Tests/FrameCallbackTests.cs`.
- [x] **F10 frame-timing readout** from `Rendering/FrameMetrics.cs` — cull/tessellate/raster split, visible-vs-considered counts, active backend.
- [x] **No exporter silently drops a shape type** — every switch now ends in a `default` that tessellates. Written separately, they had drifted: **`VDimension` was absent from DXF entirely** and `VRadialDimension` produced an SVG with no drawing element. `ShapeTessellator` learned `VArrow`/`VDimension`/`VRadialDimension`/`VRay`/`VXLine`; its `bool` return is not optional. The dimension label must be a **fresh `VText` per call** — reusing one is the obvious optimisation and is wrong, because the raster sink defers text to end of frame and every label ended up showing the same number. `Tests/ExporterCoverageTests.cs` walks the real shape surface by reflection. CLAUDE.md note 87.
- [x] **`DrawText` clamps font size** — `FormattedText` throws above ~35,791 em and `text.Height * scale` reached it, escaping the render pass and killing the process. CLAUDE.md note 89.
- [x] **Vector logo** (`Assets/Logo.xaml`), a reduced small-size variant, and an `.ico` with correct per-size artwork. CLAUDE.md note 86.

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

### Phase: The Blank Canvas, the Completion Filter, and Multi-Line Justification (2026-08-20)
- [x] **`CanvasRenderer.GetShapes(Viewport)` resolves a detached leaf** — `Viewport.Reset()` installs a new root on every run, `ViewportHost.Sync()` re-keys the cells at `DispatcherPriority.Render`, and the render runs first on a Normal-priority await continuation, so the render path routinely asked for a viewport that had left the tree and got `Array.Empty` back. Result: a blank canvas on a stock layout, every run, while the status bar counted `GetShapes()` with no viewport and reported success. `ResolveVisible()` instead of `FirstLeaf()`. `Tests/ViewportPlacementTests.ADetachedLeafStillSeesTheScene` (+ the `_byViewport` sibling). Note 123.
- [x] **`FuzzyMatcher` anchors the first typed character to a word start** — `IsWordStart` (index 0, after `_`/`.`, or a capital opening a camelCase hump, including the last capital of an acronym run). Later characters stay a free subsequence. Kills the several-hundred-row popup that one letter used to open, without touching abbreviation matching. Only the earliest qualifying anchor is tried — a later anchor's remainder is a suffix of the earlier one's, so scanning on is provably wasted. 15 new xUnit cases. Note 124.
- [x] **`SharedEditorController` tests the filtered completion count** before building the window, so an empty popup can no longer reach the screen. Matches `MainWindow` (note 43).
- [x] **`VText.Justify` / `VTextJustify`** — multi-line line alignment inside the text block, orthogonal to `Anchor`. `RenderCanvas.ApplyJustification` sets `MaxTextWidth` to the measured natural width first, because `TextAlignment` alone is inert; the `DrawFactor` reveal aligns inside the full block width so finished characters do not slide. Bounds are provably unaffected. `Tests/TextJustifyTests.cs` (6). Note 125.
- [x] **Verified in the running app and offscreen** — the canvas fix confirmed live on the reported project, and the three justifications rendered through the note 69 offscreen harness rather than argued about.

### Phase: The Flickering Line, and the Checkbox That Closed the App (2026-08-20)
- [x] **`ConsoleOutput.BeginRewrite`/`EndRewrite`** (internal) — a run's output accumulates in a staging list and is swapped in only when the text differs from what is displayed, so re-running an unchanged program announces nothing. Replaces the `Clear()`-then-rewrite that made the console blink twice a second under Auto-Run. `EndRewrite` is idempotent and lives in a `finally`, because a run that throws still has to give the console back. `Tests/ConsoleRewriteTests.cs` (5). Note 136.
- [x] **Two reads, deliberately** — `GetEntries`/`GetFormattedOutput` answer for the running program (the staged list while a rewrite is open), `GetDisplayedEntries`/`GetDisplayedOutput` for the panel and the Export button. Without the split, a program that logs and then reads its own output back would have been handed the previous run's lines on any tick that skipped compilation: a silent wrong answer, and a worse defect than the flicker. `Tests/ConsoleRewriteTests.TheRunningProgramReadsBackItsOwnOutputNotThePanels`.
- [x] **`MainWindow.RefreshConsole` updates in place** — one `ObservableCollection` bound once, shared prefix found by reference, only the tail touched. Reassigning `ItemsSource` regenerated every row and reset scroll and selection.
- [x] **`DurableFile` retries the final rename** — six attempts over ~620 ms, `IOException` only. A sync client, indexer or scanner holding a file it has just seen change is ordinary on the default projects folder, which lives under OneDrive; a read-only destination raises `UnauthorizedAccessException` and still fails immediately. `Tests/DurableFileTests.cs` (2 new, one of which asserts the retry is what carried the write). Note 137.
- [x] **`MainWindow.TrySaveProjectFile`** — a failed project-file write becomes a status-bar message, not a process exit. Used by the Auto-Run checkbox and the Settings panel; the Add Reference dialog keeps its own guard and stays open so the references are not silently lost. `Tests/ProjectSaveSafetyTests.cs` parses both window code-behinds and fails on any `SaveProjectFile()` outside a `try` — the synchronous sibling of `AsyncVoidSafetyTests`.

### Phase: Seven Editor Faults from Live Use (2026-08-22)
All seven reported from ordinary typing, and all seven invisible to the existing tests: the editor
paths that need a live `TextArea` had no coverage at all, and the completion service answered the
same way in three contexts where the same answer is wrong.
- [x] **Multi-cursor Tab was never claimed** — left to AvalonEdit, which sees only the main selection, so one keystroke indented the first line and outdented the others and the cursors drifted apart permanently. `MultiSelectionRenderer.IndentAtAllCursors` carries each cursor to *its own* next tab stop; `OutdentAtAllCursors` is a line operation keyed by line number, so two cursors on a line strip one level once. Dispatched from both hosts (notes 14, 43). Note 140.
- [x] **Multi-cursor paste pasted everything everywhere** — the copy joins one fragment per cursor with newlines, and paste put that whole joined string at every cursor, so four copied words came back as four words in all four places. `SplitForCursors` is VS Code's `multiCursorPaste: spread` rule (line count must equal cursor count, trailing separator discarded first); it is `static` and pure so the arithmetic is testable without a `TextArea`. Both paths now run through `InsertTextsAtAllCursors`, the single place a multi-cursor insert edits the document. Note 140.
- [x] **Enter inside a string literal produced code that would not compile** — a raw newline between the quotes is not legal C#, and the diagnostic that followed pointed at the quote rather than at the key. `Editor/StringLiteralSplitter.cs` closes the literal and continues it (`$"hello " +` / `$"world"`, caret inside the reopened quote). Verbatim, raw, interpolation holes and mid-escape positions deliberately keep the plain Enter — a newline is already legal in the first three, and splitting inside an escape produces `\"` rather than a closed literal. Pure by design, so `StringLiteralSplitterTests` (15) can pin the decision with no window. Note 139.
- [x] **Alt+Shift+Down left the caret past the copy** — an insert *at* the caret's own offset carries the caret with it (`AnchorMovementType.AfterInsertion`), so the `Caret.Line += lineCount` that followed double-counted whenever the caret sat at the end of the line, which is the common case. Both hosts now capture line/column before the insert and assign a `TextViewPosition` after. `CopyLineUp` had the mirror-image bug, unreported and contradicting its own comment. Note 138.
- [x] **Completion answered with the global list inside an argument list** — `Draw(` opened every type, method, namespace and statement keyword in scope, burying the two or three locals meant to be passed. `IsInArgumentPosition` restricts it to values (locals, parameters, fields, properties, range variables) plus the expected parameter type and the keywords that can start an argument; the restriction lifts after `new` and inside any nested body. Deliberate trade: a static call written *as* an argument is no longer offered from the list. Note 141.
- [x] **A property's accessor list offered `GetHashCode` and never `get;`** — an accessor is not a symbol, so nothing was adding it while `LookupSymbols` supplied rows that cannot compile there. `IsInPropertyAccessorList` answers with the accessors and their modifiers alone, stopping at a block or expression body so an accessor's *body* is still ordinary code; event accessor lists are excluded rather than given get/set. Note 142.
- [x] **A property initialiser did not know its own type** — `public List<string> Names { get; set; } = new ` suggested nothing, because the expected-type walk looked only for a `VariableDeclaratorSyntax` parent and a property initialiser's `EqualsValueClause` hangs off the `PropertyDeclarationSyntax`. Parameter defaults had the same shape and the same gap. Both handled, which also feeds note 122's preselect. Note 142.
- [x] **Tests** — `StringLiteralSplitterTests` (15), `CompletionContextTests` (14), `MultiCursorEditingTests` (9, including source scans that fail if either host regresses to the old duplicate-line spelling or loses the Tab dispatch — notes 14, 43). Suite 1151 → 1189.

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
