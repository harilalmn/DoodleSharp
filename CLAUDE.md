# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DoodleSharp is a WPF 2D geometry visualization application that allows users to write C# code to create and visualize shapes on an interactive canvas. Users write code in `.cs` files, which are compiled at runtime using Roslyn and executed to render shapes.

## Tech Stack
- **Framework**: WPF on .NET 9.0
- **Code Editor**: AvalonEdit (6.3.0.90)
- **Code Compilation**: Roslyn CSharpCompilation (Microsoft.CodeAnalysis.CSharp 4.8.0)
- **Boolean Operations**: Clipper2
- **PDF Export**: PDFsharp
- **Coordinate System**: Mathematical (Y-up, origin at center)

## Commands
```bash
# Build
dotnet build

# Run
dotnet run

# Run tests
dotnet test Tests/DoodleSharp.Tests.csproj
```

## Architecture

### Project Structure
```
DoodleSharp/
├── C2VGeometry/        # ── Referenced library (separate project): THE geometry namespace.
│                       #   Shapes (VPoint, VLine, VArc, VCircle, VRectangle, VPolygon, ...),
│                       #   VXYZ coordinate value-type, RayCaster/SpatialGrid/KDTree,
│                       #   CurveIntersection, Region, hatch, ShapeDefaults. Single source of
│                       #   truth for all geometry. (The old WPF-coupled DoodleSharp.Geometry
│                       #   namespace was retired; see docs/GEOMETRY_MERGE_SPIKE.md.)
├── Canvas/             # RenderCanvas (zoom/pan), CanvasRenderer (the C2VGeometry.IShapeRegistry), DrawingTool, SnapEngine
├── Console/            # VizConsole (output), ConsoleOutput (singleton collector)
├── Editor/             # Code editor: IntelliSenseProvider, SemanticHighlighter, CodeLensProvider, Minimap,
│                       #   CachedCompilationWorkspace, FuzzyMatcher, DocumentationSidecar, RoslynCompletionService
├── Diagnostics/        # Crash journal: Journal (writer), SystemSnapshot (machine/GPU facts),
│                       #   AppDiagnostics (global exception hooks + UI hang watchdog).
│                       #   Writes %TEMP%\DoodleSharp\YYYYMMDDhhmmss.log.
├── Execution/          # ModuleCompiler (Roslyn CSharpCompilation), StackGuardRewriter
├── Project/            # VizCodeFile, VizCodeProject, Templates
├── Rendering/          # SceneIndex (culling), LodPolicy, FrameMetrics, StrokeBatcher
│   └── Raster/         #   Bespoke software rasterizer + adaptive backend selection
├── Sketch/             # Sketch base class + SketchRuntime (sketch mode inside DoodleSharp)
├── Animation/          # Animator, animation types (Draw, Move, Rotate, Fade, etc.), plus the two
│                       #   host-pumped seams user code hooks into: Frame (per-frame callbacks,
│                       #   note 90) and Mouse/MouseInfo (canvas mouse events, note 95)
├── Docking/            # LayoutFile (versioned layout envelope) + ScreenBounds (off-screen
│                       #   recovery). The DockingManager itself lives in MainWindow.xaml; see note 100
├── Commands/           # TransactionManager + undo/redo commands
├── Export/             # DXF, PDF, GIF, video exporters (SVG lives in Canvas/SvgExporter.cs)
├── Documentation/      # DocGenerator: the F1 Help content (three name-keyed dictionaries) and
│                       #   the FlowDocument renderer. Owned by the docs-author agent; see note 91
│                       #   for what the renderer lists and why Static must stay in MemberFlags.
├── Search/             # Find/Replace dialog + results panel
├── Tests/              # Unit tests (separate project)
├── Bench/              # Render benchmark (separate project; built by CI, never installed)
├── MainWindow.xaml     # UI layout (tabbed editor, console panel)
└── App.xaml            # Dark theme resources
```

### Module System
- **Entry Point**: `StartViz.Viz.Main()` in `StartViz.cs`
- All `.cs` files in the same directory are compiled together
- Available imports: `C2VGeometry`, `DoodleSharp.Animation`, `DoodleSharp.Console`

### Geometry namespace (`C2VGeometry`)
- **Single geometry namespace** for the whole solution. The old WPF-coupled `DoodleSharp.Geometry` was deleted; everything now uses `C2VGeometry`.
- **`VXYZ` is the coordinate value-type** (immutable-ish, not a `Shape`). **`VPoint` is only a drawable point marker.** Shape coordinates (`Center`, `Start`/`End`, polygon `Points`) are `VXYZ`; methods that take a position take `VXYZ`.
- **`CanvasRenderer` implements `C2VGeometry.IShapeRegistry`** and is set as `C2VGeometry.Shape.DefaultRegistry`, so `new VCircle(...)` auto-registers onto the canvas. There is no longer any `C2VGeometryAdapter`/conversion layer.
- **Charts (`C2VGeometry/Charts/`)** — `Chart` is a static helper that builds Chart.js-style charts (`Bar`/`Line`/`Scatter`/`Pie`/`Area`) by composing existing primitives (VLine, VRectangle, VPolyline, VPolygon, VText) into a single `VGroup`. To prevent the dozens of child shapes from auto-registering individually onto the canvas, the helper flips `Shape.AutoRegister = false` during construction and registers only the outer `VGroup`; the flag is restored in a `finally` block. This is the only sanctioned `Shape.AutoRegister` flip in user-construction code — never reintroduce it in `RayCaster` or other hot paths (see note 9). Pie sectors are polygon-approximated (no `VSector` shape).

### Shape System
- All shapes extend `Shape` abstract class which implements `IDrawable`
- Curve shapes (VLine, VCircle, VArc, etc.) also implement `ICurve` interface
- **Shapes auto-register on construction** - no need to call `Draw()`
- `Draw()` is kept for backwards compatibility but is a no-op
- Each shape overrides `GetControlPoints()` and `MoveControlPoint()` for interactive editing

### Code Execution (ModuleCompiler)
- Compiles all `.cs` files using Roslyn CSharpCompilation
- Creates in-memory assembly with unique name
- Invokes `StartViz.Viz.Main()` via reflection
- Uses collectible AssemblyLoadContext for proper unloading
- Shape ID counter resets on each code execution (IDs start from 1)

### Coordinate System
- Origin (0,0) at canvas center
- Y-axis points UP (mathematical, not screen coordinates)
- WorldToScreen/ScreenToWorld methods handle conversion
- Animation loop uses `CompositionTarget.Rendering` for vsync-aligned frame updates

### IntelliSense Engine (Editor/)
The IntelliSense system uses incremental Roslyn compilation for responsive completions:
- **CachedCompilationWorkspace** - Maintains a cached `CSharpCompilation` with incremental file updates (`UpdateFile`/`RemoveFile`). Avoids rebuilding the full compilation on every keystroke by using `ReplaceSyntaxTree`. Thread-safe.
- **RoslynCompletionService** - Provides context-aware completions via Roslyn's `Recommender` API. Detects context (generic type arguments, object initializers, attributes) and classifies symbol scope (Local/ClassMember/Imported/Global) for priority sorting.
- **FuzzyMatcher** - Subsequence fuzzy matching with scoring. Rewards prefix matches, word-boundary hits, camelCase alignment, and consecutive runs. Used to filter and rank completions as the user types.
- **DocumentationSidecar** - WPF `Popup` that displays XML documentation (signature, summary, parameters, returns) beside the completion window. Tracks the completion window position and updates on selection change.
- **CompletionData** - Extended with `SymbolScope`, `MatchScore`/`MatchPositions`, and `Symbol` properties. Renders match-highlight characters in bold within the completion list.

### WPF type aliases (in RenderCanvas.cs)
`RenderCanvas.cs` uses `C2VGeometry` types **directly** — there are no `*2D` geometry aliases anymore (no name clash). It only aliases the **WPF** types it also needs, to avoid clashing with geometry: `Point` = `System.Windows.Point`, plus `Brush`/`Pen`/`Color`/`Size`/`Rect` etc. from `System.Windows[.Media]`. World coordinates are `C2VGeometry.VXYZ`; screen coordinates are `System.Windows.Point`.

## User Projects Are Not a Compatibility Surface (STANDING INSTRUCTION)

**Never spend design effort, code, or prose on user projects that already exist on disk.** DoodleSharp
projects are small, disposable sketch files, not documents with a long life to protect — treating them
as a compatibility surface buys nothing and taxes every change with migration logic and hedged
documentation.

Concretely, do not: auto-migrate or rewrite project files on open; prompt the user to update them; add
a fallback whose only justification is "otherwise projects created before this release break"; write
README/CHANGELOG caveats explaining what an existing project must do by hand; raise "but existing
projects would break" as an objection to a change; or log it as an open follow-up.

Design for freshly created projects. If a change alters what generated code looks like, change the
generator and stop there.

**A mechanism with an independent, present-tense justification is still fine** — the entry-point scan
(`ModuleCompiler.FindEntryTypeByScan`, note 111) earns its place because renaming a namespace or a
project folder by hand should work *today*, not because it rescues old projects. Keep such things;
just do not propose or defend them on backward-compatibility grounds.

This is about **user projects only**. The app's own persisted state is unaffected and keeps its
established handling: unknown keys in `appsettings.json` are ignored (notes 98, 106), and a docking
layout whose schema does not match is discarded whole (note 100).

## Key Implementation Notes (index)

**The full notes live in [`docs/NOTES.md`](docs/NOTES.md).** The list below is an index, one line per
note — enough to tell whether a note bears on what you are about to change. **Read the full note in
`docs/NOTES.md` before changing anything its line touches**; the index states the rule but not the
reasoning, and the reasoning is what stops the bug coming back.

**Note numbers are stable IDs, not positions.** Notes cross-reference each other by number, so a
number is never reused or renumbered; the gaps are deleted notes (the Animator sub-app, the
`SketchHost` isolation child, the Blazor web port, the MCP server). A new note takes the next unused
number, and gets **both** a body in `docs/NOTES.md` and a line here.

1. `RenderCanvas.cs` aliases the **WPF** types (Point, Brush, …); geometry types are used directly.
2. **Y is inverted** in `WorldToScreen` — mathematical (Y-up) coordinates.
3. Syntax highlighting files are **embedded resources**.
4. Colours are parsed by WPF `ColorConverter` — any named colour works.
5. The **working directory** is the project folder during execution; relative paths resolve from there.
6. Use `VXYZ` for intermediate coordinates; `VLine.Internal` for non-registering `VLine` data containers — a plain `new VLine(...)` pollutes the canvas.
7. Every `Draw*` in `RenderCanvas` must handle `DrawFactor` and `OffsetX`/`OffsetY`; `DrawPolyline` is the reference pattern.
8. `ConsolePanel` must **not** span into `Auto` grid rows — an infinite-height measure breaks console scrolling.
9. **`RayCaster`** — BVH ray accelerator over an explicit shape collection; `VPoint`/`VRay`/`VXLine` excluded by type test; never flip `Shape.AutoRegister`. Canvas-touching tests go in the `"CanvasState"` collection.
10. `CurveIntersection` self-intersection scans must **not allocate `VLine`** (raw-double math); same for `VPolygon`/`Region` edge building.
11. `ICurve.SetBounds` trims in place; closed/infinite curves throw; per-shape contracts (`VBezier` De Casteljau, `VSpline` resample).
14. `SharedEditorController` is the editor host glue and is **unused by `MainWindow`** — editor fixes must be made in both.
19. CodeLens rows anchor to live `TextAnchor`s, never frozen offsets; merge-preserving on a broken parse.
20. `RenderCanvas.OnMouseDown` grabs keyboard focus on any click — the canvas shortcuts depend on it.
21. `StackGuardRewriter` injects `EnsureSufficientExecutionStack` on the **execute path only** — turns an uncatchable stack overflow into a catchable exception.
26. `DrawGroup` applies the group's `OffsetX`/`OffsetY` as one `TranslateTransform` around the whole child loop.
27. `VPoint` implicitly converts to `VXYZ` (compat shim); generated code and snippets emit `new VXYZ`.
28. Completion auto-triggers on the space after `new`/`is`/`as` and on the first letter following them.
29. `Region(ICurve closedCurve)` **consumes** its source curve; loops are built with `VLine.Internal`.
30. Properties-panel flex sliders: live preview during drag, **one** commit on release — never `RaisePropertyChanged` per tick.
31. Region boolean ops take collections; `BooleanOps` forwards to `RegionBooleanOps`; no `params Region[]` on `BooleanOps`.
32. `PolygonClipper` delegates to **Clipper2** at `precision: 8`; `Clipper2Lib.dll` needs its own `installer.iss` line.
33. `VLine`/`VArrow` expose only `Start`/`End`; `ICurve.StartPoint` is an explicit interface implementation on `VLine`.
34. `VPoint` arithmetic operators live in `VPoint.cs` and always return **`VXYZ`**, never `VPoint`.
36. `VText` glyph→shape extraction goes through the injected `IGlyphOutlineProvider` seam (WPF impl in `Canvas/GlyphOutlineProvider.cs`).
37. **Global Parameters** — registry in the host assembly, re-run-everything reactivity, declare-vs-override, resident-assembly fast path, two-tier panel updates, `ParameterCodeWriter` write-back.
38. **Superseded by 106.** Kept for the dash-vs-thickness reasoning.
39. **Auto Save** lives in `MainWindow` and reuses the save path; the prompt-suppression rules are the part with real invariants.
40. **Crash journals** (`Diagnostics/`) — synchronous writer, repo-unique site keys, `AppDiagnostics.Install` first in `OnStartup`, clean-exit marker. Reference: `docs/DIAGNOSTICS.md`.
41. `CreateCompilationAsync(forExecution)` — only the execute path rewrites source; editor paths must stay **offset-faithful**.
42. "Generate method" resolves its target from the **semantic model**; insert at the closing brace's line start; no action for metadata types.
43. `MainWindow` has its own inlined editor implementation — **completion fixes go in both** it and `SharedEditorController`.
44. Member-access completion classifies the receiver **by symbol**, not `GetTypeInfo`; reject error types.
45. `RefactoringProvider.Workspace` is the fast path for quick actions and navigation (avoids a NuGet restore per invocation).
46. `App.OnStartup` opens a `.vizproj` passed on the command line; a load failure must fall through to the welcome window.
47. `ShouldHide`'s BCL decluttering must never touch user-facing types — it was silently hiding `VXYZ`.
48. **Space must never commit a completion**; `Commits()` is the explicit set `( [ { ; , )`.
49. Completion has to work in half-written code: snippets **plus** symbols, injected keywords, suppressed while declaring a name.
50. Signature help lists the **whole** overload set; a workspace overload resolves cross-file methods.
51. Diagnostics are frequently **zero-width** and must still be underlined (`DiagnosticRange`); count errors independently of markers.
52. **Superseded by 63** — `RefreshFilesFromDisk` used not to re-read already-open files.
53. Overload sets must not collide once defaults are applied (`VGrid`).
54. Every geometric shape overrides **both** `Contains` and `DistanceTo`; the base versions are bbox stubs. Reflection guard with a justified exemption list.
55. `Shape.RotationAngle` is `virtual`; `VRectangle` **overrides** it — never `new`-shadow.
56. `VEllipse` is **arc-length** parameterised; `EvaluateByAngle` keeps the angular behaviour.
57. `GeometryDiagnostics.Sink` is how the UI-free geometry library talks to the user.
58. `BuiltInHatches.Get` returns a `Clone()`, not the cached instance.
59. Anything computed behind an `await` must **re-check the caret** before it is shown.
60. Canvas delete finds the declaration with a **balanced scan**, searches every file, and says so when it cannot.
61. Rotation angles are **degrees everywhere** except `VTransform.CreateRotation` (radians). A new rotation API takes degrees.
62. The documentation surfaces have repeatedly documented API that does not exist — the **reflection diff is a required release gate**.
63. `RefreshFilesFromDisk` re-reads open files and reports what it did; never overwrites unsaved changes; caret and scroll preserved.
64. **Query methods must not draw their answer** — build results under `Shape.SuspendAutoRegistration()`.
65. Canvas delete is one undoable step through `TransactionManager`; `ICommand.SurvivesCodeRun` + `PruneAfterCodeRun()` (never `Clear()` on the run path).
66. `Shape.Place()` is the name; `Draw()` is the alias, and the two must stay **exactly** equal.
67. The four "documented but never implemented" conveniences now exist (`DoubleExtensions`, `VCircle.Diameter`, `VPolyline.PointCount`, public `CopyStyleTo`).
68. Animated rotation is applied **once**, in `DispatchShapeDraw` — never per shape. `VRectangle` is the one exclusion.
69. Renderer changes can be verified **visually, offscreen**; the recipe is in the note. Build the harness outside the repository.
70. `VTransform.CreateRotation` is `[Obsolete]`; `CreateRotationRadians`/`CreateRotationDegrees` are the names.
71. `BooleanOps.UnionAll` returns every piece (`Union` insists on a single polygon).
73. `VPolygon.Slice` cuts via two half-plane intersections through `PolygonClipper` — never a perimeter walker.
74. `Rendering/SceneIndex.cs` is the culling structure — multi-cell binning, bitset z-order, `VRay`/`VXLine` always visible by type.
75. Culling stays **on** during animation; `ReindexForAnimationFrame` instead of drawing everything.
76. Sketch mode needs `SetFrameShapes`, not `Refresh` — a sketch creating shapes in `Draw()` was frozen at frame 0.
77. `Shape.Revision` is the cache-invalidation seam; the `VHatch`/`Region` cached lists are **shared and must not be mutated**.
78. **LOD** (`Rendering/LodPolicy.cs`) bounds frame cost once culling stops helping — dot batching, dense hatch → filled boundary.
79. `Bench/` is the render benchmark, and the only way to argue about performance here. Measure first.
80. The measured **WPF ceiling is ~880 ns per primitive** — no cosmetic pen, so a thickness change invalidates every cached tessellation.
81. `C2VGeometry/Rendering/` holds the **one** shape→primitive type-switch; `Tessellate`'s return value is not optional; the instance is not thread-safe.
82. `Rendering/Raster/` is a bespoke software rasterizer — `WriteableBitmap`, tessellate once and replay per tile, no `unsafe`, no packaging cost.
83. The backend is chosen **per frame** (`Auto`/`Legacy`/`Managed`/`GPU`); the two switch thresholds are deliberately different quantities.
84. Overlays live in their **own visual**, so a mouse-move does not rebuild the scene.
85. `Shape`'s eight animation fields sit behind a lazily-allocated `AnimationState`.
86. The logo is **vector** (`Assets/Logo.xaml`); two variants; the `.ico` ships different artwork per size.
87. Exporters keep their own type switch but must **fall through to the tessellator** in the `default` arm; a reflection guard covers new shape types.
88. `D3D11RasterBackend` — geometry uploaded once in world coordinates, fails soft, no `unsafe`, eight Vortice DLLs in `installer.iss`.
89. `DrawText` **clamps its font size** — `FormattedText` throws above ~35,791 em and takes the process down.
90. `Animation/Frame.cs` is the requestAnimationFrame model — two queues swapped per pump, `Frame.Clear()` at every ALC boundary.
91. `DocGenerator.MemberFlags` must include **`Static`**; enums and structs must be in the tree; a new member *kind* means three sites (section, signature switch, search index). **Constructors key on their printed signature, not a name** — a name-keyed diff is blind to them.
92. `VArrow.ArrowheadWings` is the **only** arrowhead geometry and every renderer and exporter calls it; `VDimension.ExtensionLength` is inert and `[Obsolete]`.
93. The overlay layer must be **suppressed for any capture** of the canvas (PNG/GIF/MP4).
94. `RenderBackend` has **four** values and a Settings dropdown; a settings key with no UI row is unreachable.
95. `Animation/Mouse.cs` — registration is **assignment, not accumulation**; interactive mode is `Mouse.HasHandlers`; the `OnMouseDown` dispatch order is load-bearing. **Wheel zoom is given up per-handler (`Mouse.HasWheelHandler`), not with the rest of interactive mode.**
96. Repainting after per-frame user code needs `RepaintAfterUserCode`/`SetFrameShapes`, not `Refresh` — a shape *created* by a callback is otherwise invisible.
97. Sketch polled input is fed from `Mouse` (`TrackPointer`); `KeyPressed`/`LastKey` are still inert.
98. `AppSettingsData.ShowToolbar` was removed — unknown keys in `appsettings.json` deserialize harmlessly.
99. F1 Help renders an **Events** section; adding a member kind touches section + signature switch + search index.
100. The content area is an **AvalonDock `DockingManager`**; `ContentId` is panel identity; `Hide()` never `Close()`; documents need `CaptureContent`; a layout is a preference, discarded on schema mismatch.
101. Snippets sort **first** and are accepted by **Tab alone**; Enter and commit characters must never expand one.
102. `Alt+Shift+F` formats, `Ctrl+Shift+F` is Find in Files; a gesture lives in **five** places; no inert `Window.InputBindings`.
103. A Settings control must not declare its value in markup, and every settings handler guards on `SettingsUiBusy`.
104. F1 Help reachability has **two** guards; `DocGenerator.AllowedInternalTypes` is a per-type allowlist; `_summaries` has its own accuracy guard.
105. `SnapEngine` — a null `SceneIndex` throws; `SnapType.None` pins the zero value; `SnapResult.ConstraintPoint` is `[Obsolete]`.
106. One **`DisplayLineWeight`** checkbox, off by default; line type scale is always absolute (supersedes 38).
107. One dash definition (`LineTypePatterns`); the grid gets its own layer below the raster bitmap; `_sceneVersion` bumped by `Refresh`/`UpdateShapePosition`.
108. `C2VGeometry.Canvas` is the user-facing surface API — `Canvas.Clear()` is **not** `CanvasRenderer.Clear()`.
109. `StrokeBatcher` enrols a pen when its bucket is **empty**, not when it is new — otherwise every stroke after the first flush is silently dropped.
110. `Shape.DoesIntersect`/`Intersect(Shape)` defer to `CurveIntersection` for curve pairs; `VRay`/`VXLine` convert to their finite extent instead of being sampled.
111. A generated project namespace must not **shadow** a template-imported type; the entry point falls back to a scan.
112. A shadowed DoodleSharp name is reported **at the declaration** (`ShadowedNameDiagnostics`), wired at all three compile sites.
113. Draw order is **`Shape.ZIndex`**, sorted in `CanvasRenderer.GetShapes()`; `BringAbove`/`SendBehind` are gone.
114. `VText.Mask` is drawn by `DrawText` before the glyphs; a null `MaskColor` means the live canvas background; it must not change `GetBounds()`.
115. The completion list is **alphabetical**; only the fuzzy *filter* survives.
116. `VXYZ.AngleTo` is radians and `[Obsolete]`; `AngleToDegrees`/`AngleToRadians` are the names — and `AngleToDegrees` is unsigned.
117. **Viewports** — a recursive viewport tree in one docked pane; injected `global using static`; a leaf's only cell is itself; the reset lives in `Clear()`, never `ClearShapes()`.
118. `ViewportHost` is the fan-out point and `MainWindow.RenderCanvas` is a **property** meaning the active cell; the split is drawing-versus-workplace.
119. A divided drawing exports **tiled**; an undivided one takes the historical path; DXF is flattened into model space and says so.
120. **Auto-update is gone** — code runs on F5 / Run only.
121. **Auto-Run** is a per-project 500 ms timer — opt-in, `.vizproj`-persisted, ticks dropped while one is in flight, and it recompiles only when the source changed (a full run blanks the canvas while Roslyn works). Not note 120 coming back.

## Keyboard Shortcuts (Key Bindings)

### File/Run
- `F5` / `Ctrl+Enter` - Run code
- `Ctrl+S` - Save all files
- `Ctrl+Shift+N` - New project
- `Ctrl+N` - New file
- `Ctrl+O` - Open project

### Editor
- `Ctrl+/` - Toggle comment
- `Ctrl+F` - Find, `Ctrl+H` - Find and Replace, `Ctrl+Shift+F` - Find in Files
- `Alt+Shift+F` - Format code
- `Alt+Up/Down` - Move line up/down
- `Ctrl+D` - Add next occurrence (multi-cursor)
- `Ctrl+Shift+L` - Select all occurrences

### Code Navigation
- `F12` - Go to Definition
- `Shift+F12` - Find All References
- `Alt+F12` - Peek Definition
- `F2` - Rename Symbol
- `Ctrl+.` - Quick Fix

### Canvas & Tools
- `Ctrl+M` - Measuring Tape tool
- `Ctrl+G` - Zoom to shape by ID
- `F4` - Toggle Properties panel (inert while user code has a `Mouse` handler registered — note 95)
- `Ctrl+R` - Reset Layout (restores the default docking arrangement — note 100)
- `F6` - Toggle Global Parameters panel
- `F9` - Toggle Snap to Grid
- `F10` - Toggle the frame-timing readout
- `Ctrl+Shift+M` - Toggle Minimap
- `Esc` - Cancel current tool

### Drawing Tools (when editor not focused)
- `P` - Point, `L` - Line, `C` - Circle, `R` - Rectangle
- `Shift` (hold) - Orthogonal constraint

## Documentation Ownership (STANDING INSTRUCTION)

**The `docs-author` subagent owns all user-facing documentation** (`.claude/agents/docs-author.md`).

- **Before cutting any release, delegate the documentation pass to it.** This is a required step of
  `/release`, not an optional one — see the `/release` section below. The main session must not hand-write
  the API documentation pass itself.
- Its remit is the two user-facing surfaces: `README.md` and `Documentation/DocGenerator.cs` (F1 Help).
- Its standard is **every public type and every public member documented, each with a working
  example** — the C2VGeometry API plus `DoodleSharp.Animation` and `DoodleSharp.Console`.
- `CLAUDE.md`, `CHANGELOG.md` and `docs/*` stay with the main session: they are engineering notes and
  release history, not API documentation.

Launch it with the Agent tool, `subagent_type: "docs-author"`. It reads the source before writing
examples, so give it the diff or feature area rather than a list of prose to insert. For a full audit,
launch **two** agents with disjoint file ownership — one on `README.md`, one on
`Documentation/DocGenerator.cs` — so they run in parallel without conflicting on a file.

**Give the agent a reflection dump, not a reading list.** Note 62 is the reason: prose review has
repeatedly failed to notice documented API that does not exist, because a fabricated member reads
exactly like a real one. The check that works is a diff, and it is cheap:

1. `dotnet build DoodleSharp.sln -c Release`, then dump the public surface of the **built**
   `C2VGeometry.dll` and `DoodleSharp.dll` by reflection — every type with its constructors,
   properties, fields, events, methods (parameter names *and* default values), interfaces, enum
   values and `[OBSOLETE]` markers — plus a flat `Type.Member` index with inherited members included.
   Build the dumper **outside the repository** (note 69: a second `Main()` under the project root
   breaks the app's build with a duplicate entry point).
2. Extract the keys of `_summaries`, `_csharpSamples` and `_memberDescriptions`, and diff both ways.
   `documented − real` is fabricated API and must be fixed. `real − documented` is the work list.
3. Namespaces to hold to the "every member documented" standard: `C2VGeometry`,
   `DoodleSharp.Animation`, `DoodleSharp.Console`, plus the exporters. `DoodleSharp.Canvas` types
   (`RenderCanvas`, `SelectionTool`, `SnapEngine`, `QuadTree`, `ViewportTransform`, …) are app
   internals and out of scope — but `SvgExporter` lives there and *is* in scope.

The 2026.8.17 pass found 8 fabricated members and 391 undocumented ones this way; note that several
of the 8 were **constructor parameters documented as properties**, which no amount of reading the
prose would have flagged.

## Documentation Policy (MANDATORY)

**After ANY code change that affects the public API (new classes, methods, properties, or signature changes), you MUST update ALL of the following:**

1. **README.md** - Update examples, API tables, and feature descriptions
2. **Help Documentation (DocGenerator.cs)** - Update `_summaries`, `_csharpSamples`, and `_memberDescriptions` dictionaries
3. **CLAUDE.md** - Update if the change affects project structure or key implementation details
4. **Commit and Push** - After all documentation is updated, commit all changes and push to remote

This is non-negotiable. No compromise on documentation, ever.

## /update-docs Command

When the user says "update all documentation", "update docs", or "/update-docs":

1. **Review recent changes**: `git log --oneline -20` and `git diff HEAD~5 --stat`
2. **Read all documentation files** to understand current state
3. **Update each file as needed**:
   - `docs/TASKS.md` - Mark completed tasks, add new tasks
   - `docs/TODO.md` - Move completed items, update current items
   - `docs/PRD.md` - Update feature status
   - `CLAUDE.md` - Update Current State, Known Issues, and the **note index**
   - `docs/NOTES.md` - Add the body of any new implementation note here (next unused number),
     and add its one-line entry to the index in `CLAUDE.md` in the same pass
   - `README.md` - Update feature descriptions, examples, API tables, keyboard shortcuts
   - `CHANGELOG.md` - Add a curated, user-facing section for the upcoming release (Keep a Changelog format: Added/Changed/Fixed). This is the human summary for people browsing the repo or following releases; the GitHub release body is auto-generated from the commit log separately.
   - `DocGenerator.cs` - Update summaries and samples for new/changed members
4. **Report summary** of all updates made

## /release Command

When the user says "/release", "cut a release", "ship a release", or "release":

1. **Delegate the API documentation pass to the `docs-author` subagent, then run `/update-docs`**, so the
   release ships with current documentation. The agent covers `README.md` and `DocGenerator.cs`;
   `/update-docs` then handles `CHANGELOG.md`, `CLAUDE.md` and `docs/*`. Commit + push the doc changes as a *separate* commit before bumping the
   version. **Do not skip the agent** — it is the standing owner of the user-facing docs, and a release
   whose API documentation was not reviewed by it is not ready to cut.
2. **No version to choose — versioning is calendar-based (`YEAR.MONTH.PATCH`).** The script stamps `YEAR`/`MONTH` from today's date and increments `PATCH` within the same month (resetting to 0 the first time you release in a new month or year). E.g. the second May 2026 release is `2026.5.1`; the first June release is `2026.6.0`.
3. **Run `scripts\release.ps1`** (no `-Bump`) — it guards working-tree cleanliness, computes the calendar version, writes it into `Directory.Build.props` + `installer.iss` (the two version sources, kept in sync because Inno Setup doesn't read MSBuild props), commits as "Release v<new>", tags `v<new>`, and pushes main + tag to origin. Pass `-LocalBuild` to also build Release configs + installer locally for smoke-testing; CI publishes the canonical artifacts regardless.
4. **Tag push triggers `.github/workflows/release.yml`** on `windows-latest`: it verifies `Directory.Build.props` matches the tag, builds `DoodleSharp.sln` in Release, runs the test suite, invokes Inno Setup (`ISCC.exe`, pre-installed on the runner) to produce `installer/output/DoodleSharp-<new>-Setup.exe`, then publishes the GitHub release with the installer attached. **Release notes are generated from `git log <prev-tag>..<tag>`** (commit subjects, excluding the `Release v*` bump commits) — *not* `--generate-notes`, which produces only a bare compare link here because the repo commits directly to `main` with no PRs. The curated human summary lives in `CHANGELOG.md` (updated during `/update-docs`). Watch progress at `https://github.com/harilalmn/DoodleSharp/actions/workflows/release.yml`.

Never bump versions by hand — the script is the only thing that touches both `Directory.Build.props` and `installer.iss`. Never create a `v*` tag by hand either — the workflow's "verify props match tag" step fails fast if `Directory.Build.props` is out of sync with the tag, which is what catches hand-tagged releases.
