# Product Requirements Document (PRD)
## DoodleSharp - 2D Geometry Visualizer

### Document Information
- **Version**: 1.0
- **Last Updated**: August 2026
- **Status**: Implemented

---

## 1. Product Overview

### 1.1 Purpose
DoodleSharp is a desktop application that enables users to visualize 2D geometric shapes by writing and executing C# code. It serves as an educational tool for learning geometry concepts and a prototyping tool for geometric algorithms.

### 1.2 Target Users
- Students learning computational geometry
- Developers prototyping geometric algorithms
- Educators teaching geometry concepts
- Anyone needing quick 2D shape visualization

### 1.3 Key Value Propositions
- **Code-driven visualization**: Write C# code to create shapes programmatically
- **Interactive canvas**: Zoom, pan, and explore geometric constructions
- **Immediate feedback**: Execute code and see results instantly
- **Familiar syntax**: Standard C# with intuitive geometry classes

---

## 2. Functional Requirements

### 2.1 Shape Support

#### 2.1.1 Basic Shapes (P0 - Must Have)
| ID | Shape | Status | Description |
|----|-------|--------|-------------|
| FR-001 | Point | Done | Single point marker with coordinates |
| FR-002 | Line | Done | Line segment between two points |
| FR-003 | Circle | Done | Circle with center and radius |
| FR-004 | Arc | Done | Circular arc with start/end angles |

#### 2.1.2 Extended Shapes (P1 - Should Have)
| ID | Shape | Status | Description |
|----|-------|--------|-------------|
| FR-005 | Rectangle | Done | Axis-aligned rectangle |
| FR-006 | Ellipse | Done | Ellipse with two radii |
| FR-007 | Polygon | Done | Closed polygon with N vertices |
| FR-008 | Polyline | Done | Open polyline with N vertices |

#### 2.1.3 Future Shapes (P2 - Nice to Have)
| ID | Shape | Status | Description |
|----|-------|--------|-------------|
| FR-009 | Bezier Curve | Done | Cubic Bezier curve |
| FR-010 | Spline | Done | B-spline or Catmull-Rom |
| FR-011 | Text | Done | Text labels on canvas |

### 2.2 Shape Styling

| ID | Feature | Status | Description |
|----|---------|--------|-------------|
| FR-020 | Stroke Color | Done | Customizable border color |
| FR-021 | Fill Color | Done | Customizable fill color |
| FR-022 | Stroke Thickness | Done | Customizable line width |
| FR-023 | Dash Pattern | Done | LineType property (Dashed, Dotted, DashDot, etc.) |
| FR-024 | Opacity | Done | Transparency support |
| FR-025 | Line Weight Render Mode | Done | Stroke thickness relative to zoom (world units, default) or absolute screen pixels |
| FR-026 | Line Type Scale Render Mode | Done | Dash/gap lengths relative to zoom (world units, default) or absolute screen pixels |
| FR-027 | Draw Order | Done | `ZIndex` on every shape — global, higher on top, ties keep creation order, negatives behind |
| FR-028 | Text Mask | Done | Solid plate behind text so a label reads over geometry; on by default, coloured from the canvas background |

### 2.3 Canvas Features

| ID | Feature | Status | Description |
|----|---------|--------|-------------|
| FR-030 | Mouse Wheel Zoom | Done | Zoom centered on cursor |
| FR-031 | Middle-Click Pan | Done | Drag to pan view |
| FR-032 | Zoom Extents | Done | Auto-fit all shapes |
| FR-033 | Grid Lines | Done | Toggleable grid display |
| FR-034 | Coordinate Axes | Done | X/Y axes at origin |
| FR-035 | Coordinate Display | Done | Real-time mouse coords |
| FR-036 | Snap to Grid | Done | Snap points to grid (F9 toggle) |

### 2.4 Code Editor

| ID | Feature | Status | Description |
|----|---------|--------|-------------|
| FR-040 | Syntax Highlighting | Done | C# syntax colors |
| FR-041 | Line Numbers | Done | Visible line numbers |
| FR-042 | Code Formatting | Done | Auto-format with Alt+Shift+F |
| FR-043 | Error Display | Done | Errors shown in footer |
| FR-044 | Autocomplete | Done | IntelliSense for geometry |
| FR-045 | Error Highlighting | Done | Inline error markers |

### 2.5 File Operations

| ID | Feature | Status | Description |
|----|---------|--------|-------------|
| FR-050 | New File | Done | Create new code file |
| FR-051 | Open File | Done | Open existing .cs/.viz files |
| FR-052 | Save File | Done | Save current code |
| FR-053 | Export PNG | Done | Export canvas to PNG |
| FR-054 | Export SVG | Done | Export as vector graphics |
| FR-055 | Auto Save | Done | Periodically save all modified files; prompts for a location when the project has never been saved |

---

## 3. Non-Functional Requirements

### 3.1 Performance
| ID | Requirement | Target |
|----|-------------|--------|
| NFR-001 | Code execution time | < 2 seconds for typical scripts |
| NFR-002 | Canvas redraw | < 100ms for < 1000 shapes |
| NFR-003 | Zoom/Pan responsiveness | < 50ms latency |

### 3.2 Usability
| ID | Requirement | Description |
|----|-------------|-------------|
| NFR-010 | Learning curve | New users productive within 5 minutes |
| NFR-011 | Error messages | Clear, actionable error descriptions |
| NFR-012 | Keyboard shortcuts | Standard shortcuts for common actions |

### 3.3 Compatibility
| ID | Requirement | Status |
|----|-------------|--------|
| NFR-020 | Windows 10/11 | Supported |
| NFR-021 | .NET 9.0 | Required |

---

## 4. User Interface Requirements

### 4.1 Layout
```
┌─────────────────────────────────────────────────────────────┐
│ [New] [Open] [Save] | [Run] [Clear] | [Format] | [Export] □ Grid │  <- Ribbon
├────────────────────────────────┬────────────────────────────┤
│                                │                            │
│                                │    // Code Editor          │
│         Canvas Area            │    Point p = new Point();  │
│         (2/3 width)            │    p.Draw();               │
│                                │                            │
│                                │    (1/3 width)             │
├────────────────────────────────┴────────────────────────────┤
│ Status: Ready              X: 50.00  Y: 25.00    Scroll: Zoom │  <- Footer
└─────────────────────────────────────────────────────────────┘
```

### 4.2 Theme
- Dark theme for canvas area (reduces eye strain)
- Light theme for code editor (better code readability)
- Accent color: Blue (#007ACC)

---

## 5. Technical Architecture

### 5.1 Technology Stack
- **Framework**: WPF (.NET 9.0)
- **Code Editor**: AvalonEdit
- **Script Execution**: Roslyn (Microsoft.CodeAnalysis.CSharp.Scripting)
- **Coordinate System**: Mathematical (Y-up, origin at center)

### 5.2 Key Components
1. **Geometry Module**: Shape classes with Draw() methods
2. **Canvas Module**: Custom WPF canvas with transforms
3. **Editor Module**: Syntax highlighting and formatting
4. **Execution Module**: Roslyn-based C# script runner

---

## 6. Success Metrics

| Metric | Target |
|--------|--------|
| Shape rendering accuracy | 100% geometric correctness |
| Code execution success rate | > 95% for valid code |
| User error recovery | Clear guidance within 1 error message |

---

## 7. Release History

### Version 1.0 (Current)
- Core shapes: Point, Line, Arc, Circle
- Extended shapes: Rectangle, Ellipse, Polygon, Polyline
- Shape styling: Colors, thickness
- Canvas: Zoom, Pan, Grid, Coordinates
- Editor: Syntax highlighting, formatting
- Export: PNG

### Version 1.1 (Implemented)
- Bezier curves and splines
- Autocomplete and IntelliSense in editor
- SVG, DXF, PDF, MP4 export
- Snap to grid with 8 snap types
- Region shape (curve-bounded areas with boolean ops)
- Animation system (Draw, Move, Rotate, Flip, Fade, ValueAnimation)
- Properties panel, shape editing, minimap
- Code navigation (Go to Definition, Find References, Rename)
- Boolean operations (Union, Intersect, Difference, Xor)

### Version 1.2 (Implemented)
- VDimension with AutoCAD-style arrowheads, extension lines, and text formatting
- Drag-and-drop file/folder moving in Project Explorer
- "Go to Location" context menu to open file/folder in Windows File Explorer

### Version 1.3 (Implemented)
- RayCaster: accelerated 2D ray-casting against large shape collections (flat BVH with Surface Area Heuristic split, iterative traversal, allocation-free hot path, scales to millions of shapes)
- Query API: `FindIntersection` (closest hit, optional `maxDistance`), `HasIntersection` (any-hit early-out), `FindIntersections` (parallel batch), `Refit` (in-place AABB refresh after shape movement)
- Inline ray-vs-shape math for VLine, VCircle, VArc, VEllipse, VPolygon (and VRectangle), VPolyline; AABB fallback for other shape types
- `RayHit` and `RayQuery` record structs for ergonomic results and batching

### Version 1.3.1 (Implemented)
- RayCaster constructor now snapshots every visible Shape on `CanvasRenderer.Instance` (`new RayCaster()` — no explicit shape collection arg) — **superseded**: the geometry unification removed the canvas-snapshot constructor, since `C2VGeometry` has no canvas. The current signature is `RayCaster(IEnumerable<Shape> shapes, int leafSize = 8)`; app callers pass `CanvasRenderer.Instance.GetShapes()`.
- `VPoint` markers are always excluded from the index (zero-area visual labels; not useful ray targets)
- Optional `List<Shape>? exclusionList` on `FindIntersection` — skip specified shapes from the candidate set (useful for casting off a source shape or finding the next hit past a known set)
- Slab-test robustness fix: zero direction components on a perpendicular degenerate AABB no longer poison the comparison chain with NaN

### Version 1.3.2 (Implemented)
- `CurveIntersection.IsPolylineSelfIntersecting`, `IsPolygonSelfIntersecting`, and `GetSegments` no longer allocate canvas-registered `VLine` objects in their inner loops. Discovered while debugging an isovist ray-cast workload that took ~5 s wall-clock: the slowness was not in `FindIntersection` but in the trailing `new VPolygon(points.ToArray())`, whose self-intersection check was dumping ~65k phantom `VLine` shapes onto the canvas (one per inner-loop iteration of an O(N²) test). Construction of a 360-vertex polygon now takes <1 ms and adds zero phantom shapes.
- Internal `VLine.Internal(VPoint, VPoint)` factory added (mirrors `VPoint.Internal`) for utility code that needs a `VLine` as a data container, not a drawn shape.
- Fixes mirrored to the parallel `C2VGeometry` namespace (which has the same auto-register pattern against `DefaultRegistry`).

### Version 1.4 (Implemented) — `ICurve.SetBounds`
- New `void SetBounds(double startParameter, double endParameter)` on `ICurve`: trims the curve in place so its parameter sub-range [s, e] becomes the new [0, 1]. Inputs clamped to [0, 1] and swapped if reversed.
- **Open curves** are trimmed in place:
  - **VLine** — `Start`/`End` mutated via `Evaluate`; the VPoint instances are preserved so external references stay live.
  - **VArc / VEllipse** — `StartAngle`/`EndAngle` rescaled.
  - **VBezier** — exact trim via two De Casteljau subdivisions (split at end, then within that piece at start/end); P0..P3 instances preserved.
  - **VPolyline** — Points list rebuilt with trimmed endpoints plus interior vertices strictly within [s, e]; `_selfIntersecting` recomputed.
  - **VSpline** — dense resample at the original render resolution. Catmull-Rom tangents depend on neighboring control points, so simply retaining inner CPs visibly bent away from the original path; resampling at `numSpans × SegmentsPerSpan × (e - s)` points keeps the trimmed Catmull-Rom passing through enough interpolating samples that it tracks the original closely.
- **Closed/infinite curves throw `NotSupportedException`** because their trimmed form would be a different shape type: VCircle → arc, VPolygon → polyline, VRay/VXLine → line. The exception message points callers to `SplitAtPoint` instead.
- All changes mirrored to the parallel `C2VGeometry` namespace.
- Test coverage: 17 cases in `Tests/SetBoundsTests.cs` covering trim correctness, instance preservation, parameter clamping/swap, fidelity (Bezier exact, Spline resample), and the throw paths. Full suite passes (117/117).

### Version 1.6 (Implemented) — Geometry Unification
- **`VXYZ` is the coordinate type; `VPoint` is only a drawable marker** — all positions, vectors, and coordinate parameters/properties/return types (circle centers, line endpoints, polygon vertices, `BoundingBox.Min/Max`, `ICurve.Divide` results, etc.) are the `VXYZ` value type. `VPoint` is now reserved exclusively for a visible point marker drawn on the canvas.
- **`RayCaster` ported into `C2VGeometry`** — the accelerated 2D ray caster (flat BVH + SAH split, allocation-free hot path, parallel batch, in-place `Refit`) lives in the unified namespace; `VPoint` markers and infinite-bounds shapes (VRay/VXLine) remain excluded from the index.
- **`ShapeDefaults` reconciled into `C2VGeometry` construction** — global style and dimension-style defaults are applied at shape-construction time in the unified namespace.

### Version 1.6.1 (Implemented) — Editor & Canvas Fixes
- **CodeLens no longer blinks on broken syntax** — a nearby structural syntax error made Roslyn error-recovery intermittently drop the following declaration's method classification, so alternating recomputes added/dropped its 2×-tall CodeLens row and bounced the code below. `UpdateCodeLens` now only swaps the item set on a clean parse, merges (never removes) on a broken parse, and leaves the set untouched on a failed build. Shared `Editor/` source, so both apps benefit.
- **Canvas click grabs keyboard focus** — `RenderCanvas.OnMouseDown` now takes focus on any click, so single-key drawing-tool shortcuts (P/L/C/R, plus Delete/A/Esc) activate the tool instead of typing the letter into the code editor. Click the canvas to focus it first.

### Version 2026.6.1 (Implemented) — Shape Morphing + Characters as Shapes
- **`TransformAnimation`** — morphs one shape into another (e.g. a VLine unfurling into a VCircle) by sampling both outlines and interpolating point-by-point through an internal `VPolyline` proxy. The source is shown first, a morphing outline plays during the transition, and the real destination is revealed at the end. Works with "Auto-Draw Shapes" off and with chained transforms.
- **Welcome screen prunes missing recent files** — recent projects/animations whose files were deleted or moved no longer appear (filtered on every read, not just at startup).

### Version 2026.7.0 (Implemented) — Arc Rendering Side Fix

### Version 2026.8.0 (Implemented) — Global Parameters
- **Project-wide named values** — `GlobalParameters.Set<T>(name, value, min, max, step, group, description)` declares a value once; `GlobalParameters.Get(name)` reads it from any file in the project. Supported types are the numeric family (stored as `double`), `bool`, `string` and `DateTime`; names are case-insensitive. Storing an instance of a user-declared type is rejected because it would pin the collectible user assembly in memory.
- **Self-converting reads** — `Get(...)` returns a `ParamValue` that converts implicitly to `double`/`bool`/`string`/`DateTime`, so `Get("String Length") * 0.5` and `Get("String Broken") ? " " : " not "` compile without a type argument. `int`/`float` are explicit conversions on purpose (an implicit `int` would make `Get("n") * 2` ambiguous between `int*int` and `double*double`). The documented trade-off: converting to both `double` and `string` makes `+` ambiguous, so `Get("n") + 1` does not compile — `.Num`/`.Text`/`Get<T>()` are the escape hatches.
- **Reactivity by re-execution, not a dependency graph** — the registry lives in the host assembly so it outlives the collectible user `AssemblyLoadContext`. Changing a value simply re-runs `Main()`, recomputing every derived value at once, which is always correct and needs no invalidation logic.
- **Declare-vs-override** — `Set(...)` is an idempotent declaration, so re-running the code does not discard a value dialled in from the panel; but editing the literal in code wins and clears the override. Declarations deleted from the code are pruned on the next successful run.
- **Global Parameters panel (`Windows > Global Parameters`, `F6`)** — lists every parameter grouped by `group:`. Numbers get a value box plus a `[min] [slider] [max]` row, booleans a checkbox, strings a text box, dates a runtime-only text box. Dragging a slider re-executes the code against the already-compiled assembly, so the canvas tracks the drag with no compile latency; on release the new value is written back into the `GlobalParameters.Set(...)` literal that declared it, replacing only the number and preserving the other arguments, the undo history and the caret. The `min`/`max` boxes retarget the slider only and are never written to source.

### Unreleased (Implemented) — GPU Rendering, Per-Frame Callbacks, Documentation Audit
- **A Direct3D 11 render backend, and navigation stops costing anything** — geometry is uploaded to the GPU once in world coordinates, so a pan or zoom rewrites a single small constant buffer instead of redrawing from scratch. It is the only backend whose frame time is flat across pan, zoom and idle, and the only one that holds up at 4K, where the CPU backends spend the whole frame budget copying a 33 MB bitmap. At 3840×2160 with 100,000 shapes a frame takes under 4 ms; the heaviest test drawing went from 121 ms to 45 ms. The render backend is selectable (`Auto`, the default, which switches per frame and beats either fixed choice; `Legacy`; `Managed`), and the GPU path fails soft at every step — hardware device, then WARP, then back to a CPU path — so a machine without a usable GPU simply carries on.
- **Per-frame animation callbacks** — `Frame.Request(callback)` runs a callback on the next frame in the style of the browser's `requestAnimationFrame`: it re-requests to continue and stops asking to end. This is a far shorter path to "move this a bit each frame" than composing an animator and adding animation objects. The timeline remains, because it is *seekable* — scrubbing and GIF/MP4 export render the drawing at a given time without playing up to it, which a self-rescheduling callback cannot do.
- **A frame-timing readout on F10** — where each frame's time goes (cull, tessellate, raster), how many shapes were visible against how many were considered, and which backend drew it.
- **Every exporter handles every shape** — each format still maps shapes to its own native constructs, but an unrecognised shape now falls through to tessellation instead of producing nothing. The exporters' type switches had been written separately and drifted apart: dimensions were missing from DXF export entirely, and radial dimensions produced an SVG with no drawing element.
- **F1 Help was hiding most of the API** — its member tables were built without static members, so all 23 static classes (colours, boolean operations, charts, global parameters, easing functions and the rest) showed pages with no members on them, and static factory methods such as `VCircle.FromCenterDiameter` were invisible. 339 descriptions had been written for members no reader could reach. Enums were worse still: having neither properties nor methods, every enum page listed nothing at all — including the 83 colour names and 73 hatch-pattern names, the two pages most likely to be opened by someone looking up a name to type. Static members, enum values and constants now all appear, and Help's search finds them.
- **A documentation audit against the built assemblies** — the public surface was dumped by reflection and every type and member named in the documentation checked back against it. Eight documented members did not exist (several were constructor parameters described as properties), 391 real members had no description, and two shipped features were documented nowhere.
- **A crash when zooming far into text** — text layout throws above roughly 35,791 em and a large enough zoom reached it, taking the application down. Font size is now clamped, which changes nothing visible because a glyph that large already fills the viewport many times over.
- **Arrowheads obey their angle setting, and match across backends** — an arrow's `HeadAngle` did nothing on screen while quietly changing the raster, GPU, PDF and DXF output, because five separate calculations of the arrowhead's wings disagreed about both angle and size: the canvas ignored the setting entirely, the PDF shrank the head on short arrows, and the DXF ignored the head length too. One calculation now serves every renderer and exporter. **Arrowheads are visibly wider than before** — the canvas had been fixed near a 9.5° half-angle whatever the setting said, and the documented 30° default now genuinely applies. Dimension arrowheads had the same problem independently and agree now too, and a double-ended arrow no longer loses its second head when exported.
- **Exported images and video contain only the drawing** — anything on the canvas overlay (the F10 readout, selection handles, the rubber band, snap markers, the measuring overlay) was being captured into exported PNGs, GIFs and MP4s.
- **The render backend is selectable from Settings** — it had no interface at all and could only be changed by hand-editing the settings file. All four choices are now listed, including a GPU option the code had always honoured but nothing documented.
- **Region union respects the curve precision it is given** — every other region boolean honoured the `segmentsPerCurve` argument when folding a collection; union ignored it, so a union of curve-bounded regions could come back coarser than an intersection of the same inputs.

### Version 2026.8.9 (Implemented) — Auto-Run, and the Wheel Interactive Mode Took
- **Auto-Run** — a checkbox beside Run re-executes the project every 500 ms, as if the user kept pressing Run. Off unless a project turns it on, and saved in the `.vizproj`, so an armed project comes back armed on the next session and no other project is affected. A tick arriving while the previous run is still going is dropped rather than queued; errors go to the status bar and the editor squiggles, never a dialog. A tick recompiles only when the code actually changed — an unchanged tick re-invokes the already-compiled assembly, which is what keeps a time-dependent drawing updating without the canvas flickering while Roslyn works.
- **Wheel zoom survives a mouse handler** — registering any `Mouse.*` handler used to hand the wheel to user code along with selection and double-click-zoom-to-fit, so a sketch that merely watched clicks lost the main way to navigate a drawing larger than the viewport. The canvas now keeps its wheel zoom until user code claims the wheel with `Mouse.OnWheel`, and detaching that handler hands zoom straight back.
- **Switching projects through the New/Open dialog reloads the Settings tab** — it had kept showing the previous project's values, because that path never re-read them.

### Version 2026.8.8 (Implemented) — Viewports
- **The canvas is a recursive grid of independent views** — `Viewports.Rows` and `Viewports.Columns` divide the drawing surface (both default to 1, so nothing changes until asked), `Place(Viewports[1][2])` sends a shape to a cell, and any cell can be subdivided again, which is how uneven layouts are expressed. Each cell pans and zooms on its own and keeps its view across a run, so one cell can stay zoomed into a detail while another shows the whole drawing. Row heights and column widths take XAML's grid-length spelling (`"3*"`, `"240"`).
- **The container is what docks**, not the individual views: the pane is still one panel titled "Canvas", and floating or rearranging it behaves exactly as before.
- **Export follows the division** — PNG/GIF/MP4 capture the whole container; SVG and PDF wrap each cell in a clipped group carrying that cell's own scale and pan, so the file reproduces what is on screen. DXF has no viewport concept, so a divided drawing is flattened into model space and the console says plainly that its coordinates become screen distances. An undivided drawing exports through the path it always took.

### Version 2026.8.7 (Implemented) — Text Masks, Draw Order, Alphabetical IntelliSense
- **Text is masked by default** — every `VText` fills a rectangle behind its glyphs in the canvas background colour, so a label crossing the geometry it describes stays readable: invisible over empty canvas, a clean interruption over anything it crosses. The colour follows the canvas background live (change the background and every label follows), the padding is a fraction of the text height so a small and a large label look alike, and the plate is neither a shape nor part of the text's bounding box. Over a *filled* shape it punches a canvas-coloured hole — turn it off there with `Mask = false`.
- **`ZIndex` decides draw order, for the whole drawing** — higher draws on top, shapes sharing a value keep the order they were created in, negatives push a backdrop behind everything, and hit-testing follows the same order. It replaces `BringAbove`/`SendBehind`, which settled an argument between two shapes and were undone by the very next shape constructed, so "this label is always on top" could not be said at all.
- **The IntelliSense list is alphabetical** — what you type still filters it and matched characters are still bold, but the surviving items are in plain A–Z order instead of a five-key ranking that ended in *name length*, which listed a `VLine`'s members as End, Flip, Move, Clone, Scale, Start, Divide, Offset. Snippets still sit at the top and are still what `Tab` expands.
- **A rotation-unit trap was closed** — `VXYZ.AngleTo` answers in radians while every rotation API here takes degrees, so a label rotated by the angle between its line and the X axis came out 3.14° crooked instead of turned right round, on exactly the half of a drawing whose lines pointed the other way. `AngleToDegrees` and `AngleToRadians` say which they are; the old name is deprecated, not redefined.

### Version 2026.8.5 (Implemented) — DoodleSharp on the Web + Slice Correctness
- **The JavaScript web app was deleted** — it was a parallel reimplementation of the geometry that drifted for four months across 150 commits, ending with a polygon union that returned the convex hull of both inputs and called points outside either one "inside". Sharing the library instead of duplicating it is the whole point of the replacement, and a standing web-parity instruction now makes divergence a reportable event rather than a silent one.
- **Slicing a polygon no longer loses most of it** — cutting a concave parcel whose notch straddled the line returned two thin slivers totalling 12,945 out of an area of 225,561; the body of the parcel was dropped, and because the slivers were valid polygons nothing reported an error. `Slice` is now **area-preserving**: the pieces always add back up to the original. A concave polygon crossed more than twice correctly returns three or more pieces, so code that assumed exactly two results should check the count.
- **Undo after deleting a shape on the canvas works** — removing the code counts as an edit, which starts the automatic canvas update, and every run used to wipe the undo history including the entry for the delete that had just caused it.

### Version 2026.8.4 (Implemented) — Geometry API Correctness
- **`Contains` and `DistanceTo` are real on every shape** — both used to fall through to bounding-box stubs on `Shape`, so `line.Contains(point)` was true for points far off a diagonal and a point exactly on a circle reported a distance equal to its radius. Lines, polylines, arcs, Béziers, splines, polygons, circles, ellipses, hatches, regions, rays and construction lines now measure against their own geometry; area shapes get a genuine interior test and a distance to their outline. A reflection guard (`ShapeOverrideConsistencyTests`) keeps it that way.
- **Rotating a rectangle by animation works** — `VRectangle.RotationAngle` shadowed `Shape.RotationAngle`, so the renderer and `RotateAnimation` were reading and writing two different properties and the animation had no effect at all.
- **`new VGrid(loc, 5, 5, 10)` compiles** — the uniform-spacing constructor was unreachable (CS0121); `ySpacing` is now optional and means "same as xSpacing".
- **`VEllipse` is arc-length parameterised** like every other `ICurve`, so `Divide` spaces points evenly instead of bunching them at the flat ends; `EvaluateByAngle` retains the old behaviour.
- **Geometry diagnostics reach the user** through `GeometryDiagnostics.Sink` rather than `System.Console`, which a WPF process discards; **`ChartOptions.ShowLegend`** draws a legend for bar and pie charts instead of being read by nothing; **`BuiltInHatches.Get`** returns a deep copy, so mutating one hatch no longer corrupts every later use of that pattern.

### Version 2026.8.3 (Implemented) — Documentation Overhaul + Editor Correctness
- **And code defects only visible from reading the whole API** — animations applied part of their effect before their start time under non-linear easings; arc intersections registered phantom circles on the canvas; `VHatch`/`VRadialDimension`/`VSpatialGrid` were missing from the auto-naming list so they vanished after a run; `GetClosestCell` could only be called by constructing a drawable `VPoint`.

### Version 2026.8.3 (Implemented) — IntelliSense & Quick Action Correctness
- **Double-clicking a project opens it** — `.vizproj` files launch straight into the main window with the entry-point file open. The installer had registered the file association from the start, but the application ignored the path Explorer handed it and showed the welcome screen. A project that cannot be read reports why and falls back to the welcome screen.
- **Generate method goes to the right class** — the quick action resolves the invocation's receiver through the semantic model and writes the stub into the file that declares the owning type, naming it in the menu ("Generate method 'DrawVector' in VectorManager"). A type-name receiver produces a `public static` member, a value receiver an instance member, a bare call a member of the enclosing type. Types that cannot be edited (C2VGeometry, the BCL, NuGet packages) offer no action rather than generating something unreachable.
- **Editor analysis matches the editor's text** — the compiler's source rewriters (shape/animation naming, stack guard) now run only on the execute path. They insert text, so running them for the editor made go-to-definition, find-all-references and rename resolve the wrong token in any file containing a named shape declaration, and made rename write at shifted offsets into other files.
- **Suggestions appear when they should** — auto-popup while typing an identifier and after `new`/`is`/`as`; a `.` typed while the list is open reopens it as a member list instead of showing nothing; a single internal error can no longer disable completion for the rest of the session; files created or deleted mid-session are picked up immediately.
- **Suggestions are the right ones** — a class name is recognised as a class name (previously treated as a value, which hid its static members), instance access no longer offers statics, an unbindable receiver produces an empty list rather than unrelated locals, and `ToString`/`Equals`/`GetHashCode`/`GetType`/type parameters are no longer filtered out of every list.
- **Discoverable refactoring** — the editor's right-click menu now carries Go to Definition, Peek Definition, Find All References, Quick Actions and Rename; quick actions and navigation reuse the editor's live compilation instead of rebuilding the project (and re-running a NuGet restore) on each invocation.

### Version 2026.8.2 (Implemented) — Crash Diagnostics
- **What is captured** — machine, OS, .NET, CPU/RAM/disk, locale, screens, GPU model and display-driver version, WPF render tier and every loaded assembly; every project and file opened or saved with size, timestamp and content hash; every compile, assembly load and entry into user code; unhandled exceptions on the UI thread, background threads and unobserved tasks with full inner-exception chains; the identity of the shape being drawn when rendering throws; a ten-second health pulse with memory, thread and GDI/USER handle counts; UI-thread hang detection; and a crash-time dump of the open project, the editor contents and global parameter values.
- **Pinpointing the code** — every record carries a hand-assigned **site key** (`AREA.SUBSYSTEM.EVENT`) that is unique across the repository, enforced by the test suite, alongside the compiler-captured `File.cs:line Member`. A key from a user-submitted journal therefore resolves to exactly one line of source.
- **Surviving uncatchable failures** — records are flushed synchronously rather than queued, so a stack overflow, access violation or fail-fast (which no .NET handler can intercept) still leaves the last thing the app did on disk. An unclosed `ENTER` scope localises the crash; the absence of the clean-exit marker identifies the session, which the next launch indexes into `crashes.txt`.
- **Privacy and housekeeping** — journals never leave the machine on their own, are pruned after 30 days (60 files max, 64 MB each), and can be disabled with `DOODLESHARP_JOURNAL=0`. `Help > Open Diagnostic Journals` and `Help > Copy Current Journal Path` make the file easy to find and send.

### Version 2026.8.1 (Implemented) — Auto Save + Zoom-Relative Line Styles
- **Auto Save** — `Settings > Application Settings > Auto Save` writes every modified file in the project to disk on a configurable interval (5-3600 seconds, default 60). Each tick flushes the editor into the active file and saves only the files that actually changed, so an idle project produces no disk writes; the status bar reports `Auto-saved at HH:mm:ss`. Auto Save supplements `Ctrl+S` rather than replacing it — a crash costs at most one interval of work.
- **Prompt when the project has nowhere to write** — a project still living in the temp folder, or a file that has never been through the Save dialog, has no path to auto-save to. Rather than silently dropping the changes, Auto Save reports this and offers to save the project now. Answering No keeps the changes in memory and stops the reminder until the project has been saved, so a background timer never turns into a repeating modal.
- **Zoom-relative line weight and line type scale (new default)** — each can be measured either in world units (the default: they scale with the geometry, so a `LineWeight = 3` stroke is 3 world units wide and a dashed line keeps the same number of dashes along its length however far you zoom) or in screen pixels (the previous behaviour: strokes and dash patterns keep a constant on-screen size at any zoom). The two are independent settings, so combinations such as absolute strokes with a relative dash pattern are supported. Changing either redraws the canvas immediately.
