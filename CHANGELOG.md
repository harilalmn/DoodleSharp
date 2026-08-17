# Changelog

All notable user-facing changes to DoodleSharp are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses calendar
versioning (`YEAR.MONTH.PATCH`).

Each GitHub release also carries auto-generated notes built from the commit log between
tags; this file is the curated, human-friendly summary.

## [Unreleased]

### Added
- **Mouse events for your code** — `Mouse.OnMove`, `OnDown`, `OnUp`, `OnClick`, `OnDoubleClick`,
  `OnDrag`, `OnWheel`, `OnEnter` and `OnLeave` hand the canvas's mouse input to a callback, in the
  style of the browser's `onmousemove(e)`. The event carries the cursor position in world
  coordinates, which button and modifier keys are down, the click count, the wheel amount, and the
  shape under the cursor. Assigning a handler replaces it rather than adding another, so re-running
  your code — or dragging a Global Parameter — never stacks duplicates.

  ```csharp
  Mouse.OnDown(e => new VCircle(e.Position, 10) { FillColor = "Cyan" });
  Mouse.OnMove(e => VizConsole.Log($"over {e.Target?.Name ?? "empty space"}"));
  ```

  Registering a handler puts the canvas into **interactive mode**: it stops competing with you for
  the mouse, so click-to-select, wheel zoom and double-click-zoom-to-fit step aside and your handlers
  see every gesture. Zoom controls and a live zoom percentage appear over the top-right of the canvas
  in their place, and middle-button drag still pans. Nothing is removed — a project that registers no
  handlers behaves exactly as before, and the drawing tools and measuring tape keep priority while
  they are armed.
- **`Mouse.X`, `Mouse.Y` and `Mouse.IsDown`** report the pointer without registering anything, so
  they can be read from a `Frame` callback or a sketch's `Draw()`.

### Fixed
- **A sketch's `MouseX`, `MouseY` and `MousePressed` now actually report the mouse.** All three were
  documented from the start but nothing ever wrote them, so every sketch saw `0`, `0` and `false` for
  as long as the feature has existed. (`KeyPressed` and `LastKey` are still not wired up.)
- **A shape created by an animation callback now appears.** A `Frame` callback that *moved* an
  existing shape worked, but one that *created* a shape drew nothing at all — the canvas kept
  repainting the set of shapes captured when the run finished. Which of the two you happened to write
  decided whether the feature looked broken.
- **Stop now stops a `Frame` animation.** The Stop button only ever halted sketches and timelines, so
  motion driven by a self-rescheduling callback carried on with no way to halt it short of running
  again.
- **Dragging a Global Parameter no longer multiplies running animations.** Each tick of the drag
  re-ran your code without dropping the previous run's callbacks, so a drag left several animation
  loops running at once and the motion visibly sped up as it went.
- Removed the leftover **Show Toolbar** setting, which controlled a drawing toolbar that was replaced
  by the Draw menu and had done nothing since.
- **F1 Help now lists events.** Every public event was missing from its page and from Help search —
  including `Frame.CallbackFailed` and `GlobalParameters.Changed`, which had written descriptions no
  reader could reach.

## [2026.8.2] - 2026-08-17

> `2026.8.1` was tagged but never published: `installer.iss` had been left unparseable by an earlier
> commit, so the release build aborted at the Inno Setup step. Nothing reads that file until a release
> runs, which is why it went unnoticed. These are that release's notes, shipped as `2026.8.2` along
> with the installer fix and a test that now validates the script on every build.

### Added
- **Direct3D 11 render backend.** Geometry is uploaded to the GPU once, in world coordinates, so
  panning and zooming cost essentially nothing — it is the only backend whose frame time stays flat
  across navigation, and the only one that holds up at 4K, where the CPU backends spend their whole
  frame budget copying a 33 MB bitmap. At 3840×2160 with 100,000 shapes it renders in under 4 ms;
  the heaviest test drawing went from 121 ms a frame to 45 ms. It fails soft at every step —
  hardware device, then WARP, then quietly back to a CPU path — so a machine with no usable GPU
  simply carries on. Pick the backend under Settings: `Auto` (the default, which switches per frame
  and beats either fixed choice), `Legacy` (WPF vector) or `Managed` (the software rasterizer).
- **Per-frame animation callbacks** — `Frame.Request(callback)` runs a callback on the next frame,
  in the style of the browser's `requestAnimationFrame`. The callback re-requests to keep going and
  simply stops asking to end, which is a much shorter path to "move this a bit each frame" than
  composing an `Animator` and adding `Animation` objects. The timeline is unchanged and still the
  right tool when you need to *seek* — scrubbing, and GIF/MP4 export, render a drawing at a given
  time without playing up to it, which a self-rescheduling callback cannot do.
- **Frame-timing readout on F10** — an in-app overlay showing where each frame's time goes
  (cull / tessellate / raster), the visible-versus-considered shape counts, and which backend drew
  the frame.

### Fixed
- **Arrowheads obey `HeadAngle`, and look the same everywhere.** Setting an arrow's `HeadAngle` did
  nothing on screen while quietly changing the raster, GPU, PDF and DXF output — there were five
  separate calculations of where an arrowhead's wings go, and they disagreed about both its angle and
  its size. The canvas ignored `HeadAngle` entirely; the PDF shrank the head on short arrows; the DXF
  ignored `HeadLength` as well. There is now one calculation, used by every renderer and exporter, so
  an arrow looks the same on screen as in an export whichever backend drew it. **Arrowheads are
  visibly wider than before**, because the canvas had been pinned near a 9.5° half-angle regardless
  of the setting; the documented 30° default now genuinely applies. Dimension arrowheads had the same
  problem independently and are consistent now too. A double-ended arrow also lost its second head in
  PDF, SVG and DXF exports. One difference remains: the arrowhead is drawn solid by the `Legacy`
  backend and as an outline by `Managed` and `GPU`.
- **The frame-timing readout and selection handles no longer appear in exported images and video.**
  Anything drawn on the canvas overlay — the F10 readout, selection handles, the rubber band, snap
  markers, the measuring overlay — was being captured into exported PNGs, GIFs and MP4s.
- **The render backend is selectable from Settings.** It had no interface at all and could only be
  changed by hand-editing `appsettings.json`. It is now a dropdown under
  *Settings > Application Settings > Rendering*, listing all four choices — the `GPU` option was
  always honoured by the code but documented nowhere.
- **Region booleans respect the curve precision you ask for.** Every other region boolean took a
  `segmentsPerCurve` argument when folding a collection; union ignored it and always used the default,
  so a union of curve-bounded regions could come back coarser than an intersection of the same
  inputs. Separately, all eight `Region` overloads on `BooleanOps` forwarded without the argument at
  all, so reaching a region boolean through that shorter entry point silently pinned precision to the
  default whichever operation you used.
- **`VDimension.ExtensionLength` is deprecated.** It never had any effect: an extension line's length
  is fully determined by `OffsetFromOrigin`, `Offset` and `ExtendBeyondDimLines`. Existing code that
  sets it still compiles, now with a warning saying so.
- **No exporter silently drops a shape type any more.** Each format still maps shapes to its own
  native constructs — a DXF keeps a circle as a `CIRCLE` rather than sixty-four chords — but an
  unrecognised shape now falls through to tessellation instead of producing nothing at all. The
  switches had been written separately and drifted apart: **`VDimension` was missing from DXF
  export entirely**, and `VRadialDimension` produced an SVG containing no drawing element. A
  reflection-driven test now fails the build if a new shape type ships uncovered.
- **F1 Help was hiding most of the API.** Member tables were reflected without
  `BindingFlags.Static`, so all 23 static classes — `VColor`, `BooleanOps`, `Chart`,
  `GlobalParameters`, `ArrayOps`, `GeometryHelper`, `Frame`, `EasingFunctions` and the rest — showed
  a page with no members on it, and static factories such as `VCircle.FromCenterDiameter`,
  `VArc.FromStartCenterEnd` and `VXYZ.BasisX` were invisible. 339 member descriptions had been
  written for members no reader could reach. Enums fared worse still: having neither properties nor
  methods, every enum page listed nothing whatsoever, including `ColorName`'s 83 colour names and
  `BuiltInHatch`'s 73 pattern names — the two pages most likely to be opened by someone looking up a
  name to type. Constants like `GeometryTolerance.Epsilon` were missing for the same reason. Static
  members, enum values and constants now all render, staticness is marked in the signature column,
  and Help's search index covers them too. Enums and structs were also missing from the Help tree
  altogether — 15 enums and 5 structs had no page at all — and a member inherited from a base class
  could display a description belonging to an unrelated type that happened to share a member name.
  Every public type and member of the geometry, animation, console and export APIs is now documented
  with a worked example: 1,478 member descriptions, verified by reflection against the built
  assemblies so that nothing is documented that does not exist.
- **A crash when zooming far into text.** `FormattedText` throws above roughly 35,791 em, and a
  large enough zoom reached it, taking the process down with it. Font size is now clamped — a glyph
  that large already fills the viewport many times over, so nothing visible changes.

### Changed
- The logo is now vector artwork, sharp at any DPI and genuinely transparent, with a simplified
  variant for small sizes and an icon that ships correct artwork per size.

## [2026.8.0] - 2026-08-17

### Added
- **DoodleSharp** — write C#, watch the geometry appear. A Roslyn-powered editor with
  IntelliSense, refactoring and live diagnostics, beside an interactive canvas with drawing,
  snapping and measuring tools. The `C2VGeometry` shape library underneath: shapes and
  curves, charts, boolean operations, curve-bounded regions, hatches, dimensions and text.
  Plus a timeline animation system, Global Parameters you can tune live, a properties panel,
  undo/redo, export to PNG/GIF/MP4/DXF/PDF/SVG, NuGet package support, F1 help, and
  crash journals for when something does go wrong.
- **A new renderer.** Drawings that used to crawl now stay responsive. On a 100,000-shape
  benchmark, the worst frame of a mixed CAD drawing went from 107 ms (9 fps) to 49 ms
  (21 fps), a dense hatched drawing from 1,038 ms to 4 ms, and clicking to select from
  0.39 ms to 0.04 ms. Panning a 100,000-line drawing now costs 0.2 ms a frame.
  - **Viewport culling that works.** Only what is on screen is drawn, and the renderer no
    longer looks at every shape in the document to decide. Culling also stays on during
    animation and sketch playback, which is exactly when it was previously turned off.
  - **Level of detail.** Shapes too small to resolve are drawn as a single mark, or not at
    all. This is what keeps a large drawing usable when you zoom out over all of it.
  - **A software rasterizer**, used automatically when a frame is heavy enough to need it
    and stepped aside when it is not. Lines are crisp single pixels at every zoom.
  - **Cached geometry.** Hatch patterns and curved region outlines are generated once
    rather than rebuilt on every frame — the single largest cost in the old renderer.
  - **Interaction no longer redraws the drawing.** Moving the mouse with a tool active
    updates only the handles, rubber band and snap markers.
- **A render benchmark** (`Bench/`) with committed baselines, and an in-app frame-timing
  readout, so performance claims are measured rather than asserted.

### Fixed
- **Sketch mode showed only the first frame.** A sketch that created its shapes inside
  `Draw()` — which is what the built-in template does — rendered once and then sat still.
  Only sketches that modified shapes created in `Setup()` appeared to animate.
- **Rotation animations, dimensions and infinite construction lines** are all drawn
  correctly by the new renderer; several were previously dropped or mis-clipped.

### Notes for anyone writing sketches
- `Shape` gained a `Revision` counter and an `Invalidate()` method, used to keep cached
  geometry in step with edits. If you modify a shape's point list in place — rather than
  assigning a new one — call `Invalidate()` so the cache is rebuilt.

- **A Direct3D backend**, off by default and selectable with `RenderBackend: "GPU"`. It
  uploads the drawing to the graphics card once, so panning and zooming cost the same
  whether the drawing holds a thousand shapes or a million. It is the only mode that keeps
  up on a 4K display. If your machine cannot provide a device it quietly uses the software
  renderer instead and records why.
- **Exports no longer lose shapes.** Dimensions were missing entirely from DXF files, and
  radial dimensions produced an empty SVG. Every shape type is now checked, in every format.
- **A frame-timing readout on F10**, showing where each frame's time goes.

### Fixed (continued)
- **The application could be crashed by zooming in far enough** on a drawing containing
  text — the font size passed a limit inside Windows and took the whole process with it.

### Known limitations
- The rasterizer draws geometry beneath text, dimensions and annotation rather than in
  strict creation order. On technical drawings this is what you want; if you need exact
  ordering, set `RenderBackend` to `Legacy` in settings.
- Text is still drawn on the CPU in every mode. On a drawing with thousands of labels on
  screen at once that is the slowest part of a frame, and the Direct3D backend cannot help
  with it yet.
