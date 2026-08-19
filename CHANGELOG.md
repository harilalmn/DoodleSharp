# Changelog

All notable user-facing changes to DoodleSharp are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses calendar
versioning (`YEAR.MONTH.PATCH`).

Each GitHub release also carries auto-generated notes built from the commit log between
tags; this file is the curated, human-friendly summary.

## [Unreleased]

### Added
- **`VText.Mask` — a solid background behind a label, on by default.** A label sitting on the line it
  describes is hard to read, so every `VText` now fills a rectangle behind its glyphs in the **canvas
  background colour**: invisible over empty canvas, a clean interruption over anything it crosses.
  `MaskColor` is `null` by default, meaning *follow the canvas background* — resolved when the text
  is drawn, so changing the canvas colour updates every label with nothing to re-run — and can be set
  to any colour name or hex. `MaskOffset` is the padding as a *fraction of the text height*: `0` hugs
  the glyphs, `1` pads by a full text height on every side, default `0.15`, clamped to that range.
  The mask never appears as a shape of its own and does not change the text's bounding box, so
  zoom-extents is unaffected. Over a **filled** shape a masked label punches a canvas-coloured hole —
  set `Mask = false` there. Dimension labels keep their own `TextBackgroundOpaque` switch and are
  deliberately unmasked, so every render backend draws them identically. Honoured on the canvas and
  in SVG and PDF exports; DXF has no background fill in the R12 format DoodleSharp writes, so a
  masked label exports there as plain text.
- **`Shape.ZIndex` — global draw order.** Every shape now has an `int ZIndex`: the drawing is painted
  in ascending order, shapes sharing a value keep the order they were created in, and negatives push
  a backdrop behind everything. Hit-testing follows the same order, so the shape you click is the one
  you see on top. Assigning it is enough — the canvas re-sorts before the next paint, including from
  a `Mouse` or `Frame` callback.

### Changed
- **`VXYZ.AngleTo` is deprecated in favour of `AngleToDegrees` / `AngleToRadians`.** It returns
  radians, and this library works in degrees everywhere else — so
  `text.Angle = dir.AngleTo(VXYZ.BasisX)` on a direction pointing along −X assigned π as **3.14
  degrees**, drawing the label a hair crooked instead of turning it right round. Reported as a text
  mask that looked "slightly off axis" on one half of a drawing and square on the other, because for
  +X the answer is 0 in either unit. Behaviour of `AngleTo` is unchanged; only the name is retired.
  Both spellings are unsigned (0–180 / 0–π) — to orient something *along* a direction, where the sign
  matters, use `Math.Atan2(dir.Y, dir.X).ToDegrees()`.
- **The IntelliSense list is alphabetical.** What you type still filters it, and matched characters
  are still shown in bold, but among what survives the filter the order is plain A–Z. It used to be
  ranked by expected type, match-score band, type-vs-member, scope and then *name length*, which put
  the members of a `VLine` in the order End, Flip, Move, Clone, Scale, Start, Divide, Offset — an
  order with no rule a reader could see. Snippets still sit at the top and are still what `Tab`
  expands.

### Removed
- **`Shape.BringAbove(other)` and `Shape.SendBehind(other)`**, replaced by `ZIndex`. They reordered
  the shape list pairwise, so the result depended on the order the calls were made in and was undone
  by the next shape constructed — "this label is always on top" could not be expressed. Rewrite
  `label.BringAbove(x)` as `label.ZIndex = 1;`. The `IShapeRegistry.MoveAbove`/`MoveBehind` pair
  behind them is replaced by a single `NotifyOrderChanged(shape)`.

## [2026.8.6] - 2026-08-18

### Fixed
- **A project named after part of the API could not use it.** The project name becomes the namespace
  of the generated code, and a namespace declaration is searched before any `using` — so in a project
  called *Mouse*, `Mouse.OnMove(...)` resolved against the user's own namespace and failed to compile
  with *"the type or namespace name 'OnMove' does not exist in the namespace 'Mouse'"*. The same trap
  applied to any project named after a type the templates import (`Frame`, `Canvas`, `VCircle`,
  `Console`, `Math`, `List`, …) or after a C# keyword. New projects now get a non-shadowing namespace
  (*Mouse* → `MouseProject`).

  An existing project already carrying the shadowing namespace is fixed by renaming the namespace in
  `StartViz.cs` — which is now safe to do, because the entry point is found by scanning for the `Viz`
  class when the namespace no longer matches the project name. Fully qualifying the call
  (`DoodleSharp.Animation.Mouse.OnMove(...)`) works too.
- **A new module file (Ctrl+N) was created with an invalid class name.** The default file name
  `Untitled-1` was written straight into the template as `public class Untitled-1`, so the file failed
  to compile the moment it was created; a project name containing a space produced an equally invalid
  namespace. Both names are now sanitized.

### Changed
- **A name that collides with the DoodleSharp API is now reported where you can fix it.** The compiler
  blames the token it failed to look up, so shadowing `Mouse` underlined `OnMove` — the one part of
  the line that was correct — and said nothing about the declaration that caused it. The error now
  appears on the declaration itself, reading **"Mouse is a keyword. try another name"**, once however
  many uses it broke. This covers namespaces, classes, locals, fields, properties, parameters and
  `foreach` variables, and applies to the console, the editor squiggles and the error count alike.
  Ordinary mistakes are untouched: a typo against the real API still reports as a typo.

## [2026.8.5] - 2026-08-18

### Fixed
- **Lines, polylines and unfilled rectangles/polygons disappeared after the first redraw.** The
  stroke batcher — which collapses many same-coloured strokes into one draw call — stopped emitting
  anything for a pen once it had flushed that pen a first time, so the geometry was culled,
  tessellated and then dropped. The shapes were still there: they hit-tested, selected and showed
  their real values in the Properties panel; they just were not painted. Anything that triggers a
  full redraw (pan, zoom, select, run, an animation frame) made them vanish. The same bug left the
  undrawn segments in memory frame after frame.
- **`DoesIntersect` reported no intersection for almost every pair of shapes.** Only line/line,
  line/rectangle, rectangle/rectangle, point and group were ever answered; ray/circle,
  circle/circle, arc, polyline and polygon pairs all came back false — while `Intersect()` on the
  very same pair returned real points. Both `DoesIntersect` and `Intersect` now use the same
  engine, so the guard and the answer agree:

  ```csharp
  foreach (var obstacle in obstacles)
      if (ray.DoesIntersect(obstacle))
          VizConsole.Log(ray.Intersect(obstacle).Points);
  ```

- **Intersections involving `VRay` or `VXLine` were thousands of times slower than they needed to
  be** — and approximate. Each was chopped into a thousand chords and tested against a thousand
  more, so one ray against one circle cost ~65 ms and a 359-ray casting loop took over two minutes.
  They are now solved exactly, in well under a microsecond; that loop finishes in about 1 ms. A ray
  still reaches only as far as its `RenderExtent` (10000 by default) — raise it for obstacles
  further out.
- **`RayCaster` returned bogus hits for construction lines.** `VRay` and `VXLine` were documented as
  excluded from the index, and were not: both report a *finite* bounding box derived from
  `RenderExtent`, so the non-finite-bounds filter never caught them. Neither has exact ray math, so
  a hit on one was a hit on its bounding box — a diagonal construction line could answer with a
  point nowhere near itself, and being nearest it beat the real geometry behind it. They are now
  excluded by type, as the documentation always claimed. To find where a ray crosses a construction
  line, intersect them directly with `ray.Intersect(other)`.


## [2026.8.4] - 2026-08-18

### Added
- **`Canvas.Clear()` and `Canvas.Remove(...)`** — a way to take shapes back off the canvas from your
  own code. This is what a callback that *redraws* needs: shapes register themselves as you construct
  them, so a mouse-move handler that builds a circle leaves one behind per pixel of travel.

  ```csharp
  Mouse.OnMove(e =>
  {
      Canvas.Clear();
      var rings = (int)(e.X / 40);
      for (var i = 1; i <= rings; i++)
          new VCircle(new VXYZ(0, 0), i * 20) { Color = "Cyan" };
  });
  ```

  `Canvas.Remove(a, b)` — or `Canvas.Remove(list)` — takes off only what you name, ignoring nulls and
  shapes that are not on the canvas. Both are geometry only: neither rewinds shape IDs, stops a
  running timeline, nor resets the view.

  Note that **`Frame.Clear()` is not this** — it drops queued `Frame.Request` callbacks and leaves the
  drawing untouched. If you had been calling it expecting a blank canvas, that is why shapes kept
  piling up. And most handlers should not clear at all: when only *positions* change, build the shapes
  once and assign to them, which allocates nothing per event.

### Changed
- **Line weight rendering is now one checkbox, "Display Line Weight", and it is off by default.**
  It replaces the pair of Absolute / "Relative to zoom level" dropdowns for line weight and line
  type scale, which between them offered four combinations when two were wanted. Off, a shape's
  `LineWeight` is screen pixels and a stroke looks the same at any zoom; on, it is world units and
  strokes thicken as you zoom in, the way a CAD package shows true widths.

  **`LineTypeScale` is now always absolute** — dash and gap lengths keep a constant on-screen size
  and there is no setting for them. If you had either option set to "relative to zoom level", it
  now behaves as absolute until you tick Display Line Weight. Existing settings files are read
  without complaint; the retired keys are dropped on the next save.
- **Dash patterns are identical on every renderer.** `Center`, `Phantom` and `Hidden` drew as solid
  lines whenever the faster software renderer was active, and the other four patterns used different
  dash lengths there than on the vector renderer — so a dashed line could change appearance based only
  on how busy your drawing was. There is now one definition behind all of them, including SVG export.
- **Snapping reports a missing scene index instead of silently not snapping.** `SnapEngine.FindSnapPoint`
  returned "no snap" when handed no index, which is indistinguishable from nothing being near the
  cursor; it now fails loudly and names the overload to use. `SnapResult.ConstraintPoint` is deprecated
  — it was always exactly `Point`.

### Fixed
- **Seven help pages you could not open.** `SvgExporter`, `SnapEngine`, `SnapType`, `SnapResult`,
  `DrawingTool`, `DrawingInputMode` and `GlyphOutlineProvider` had written documentation that was
  unreachable in F1 Help and invisible to its search. Their pages now exist, with examples and a
  description for every member. `DrawingMode` was added alongside them.
- **`Ctrl+Shift+F` opens Find in Files.** The Search menu has advertised that shortcut all along,
  but nothing ever handled it.
- The F1 **Drawing Tools** page said Arc is "click center, start, end". It is start, a point on the
  arc, then end — the centre is derived. Four shape types missing from that table were added.
- **The grid no longer paints over your drawing.** With the faster renderers active the grid was drawn
  on top of the geometry rather than beneath it.
- **Dragging a shape works on the GPU renderer.** The shape's selection handles and its hit-testing
  moved while the geometry itself stayed painted where it was; in-place edits and animations were
  invisible there for the same reason.
- **Dimensions, arrows, grids and construction lines appear on the GPU renderer.** They were silently
  dropped.
- **SVG exports have usable stroke widths.** Widths were written in drawing units rather than pixels,
  so a large drawing exported with strokes too thin to see and a small one with strokes like blobs.
  Line types now export as dashes too, instead of everything coming out solid.

## [2026.8.3] - 2026-08-18

### Added
- **Dockable panels.** The Canvas, Console, Find Results, Timeline, Explorer, Outliner, Properties and
  Global Parameters are now tool windows you can drag anywhere: to another edge, into a tab group with
  another panel, or out onto a second monitor. Guide diamonds appear while you drag to show where a
  panel will land. Code and Settings became document tabs. The intended arrangement for two screens —
  code on one, canvas and console on the other — is now just a drag.

  Your arrangement is remembered between sessions, including which monitor a floating panel was on,
  and **View ▸ Reset Layout (Ctrl+R)** puts everything back. A panel left on a monitor that is no
  longer attached is brought back onto the desktop rather than stranded off-screen. If a layout ever
  misbehaves, deleting `%APPDATA%\DoodleSharp\layout.xml` restores the default.

  Reset Layout used to reset only the canvas/console split; it now restores the whole window.
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

### Changed
- **Format Code is now `Alt+Shift+F`**, matching VS Code and Visual Studio. It was `Ctrl+Shift+F`,
  which this window also uses for **Find in Files** — the two were bound to the same keys and Format
  always won, so Find in Files could not be reached by the shortcut its own menu advertised.
- **Code snippets now sit at the top of the IntelliSense list and are pre-selected**, so typing `fo`
  and pressing **Tab** writes a `for` loop. They used to be appended below every matching type, several
  scrolls down, and a snippet lost the selection to any keyword or type it tied with — typing `for`
  highlighted the bare `for` keyword instead of the loop. Keywords that a snippet already spells are
  no longer listed twice.

  **Tab is the only key that expands a snippet.** Enter ends the line as usual and commit characters
  like `(` are typed as usual, so `x = null` followed by Enter, or a hand-written `for(`, are left
  exactly as you typed them.

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
- **Dragging the console divider no longer ignores the timeline**, so the console can no longer be
  dragged over the canvas's minimum height while the timeline is showing.
- The floating Properties window used to contain **two overlapping copies** of the panel, one of them
  permanently blank. It is now an ordinary dockable panel and the duplicate is gone.
- **Canvas text stays crisp when the canvas moves between monitors with different scaling.** The
  pixel density was cached once and never refreshed, which only became reachable now that the canvas
  can be floated onto a second screen by itself.
- **Exporting works with the canvas hidden or behind another tab.** Every export reads the canvas's
  on-screen size, so a hidden panel produced an "Invalid Canvas Dimensions" error; the canvas is now
  brought forward first.
- **F1 Help now lists events.** Every public event was missing from its page and from Help search —
  including `Frame.CallbackFailed` and `GlobalParameters.Changed`, which had written descriptions no
  reader could reach.
- **The Code and Settings tabs are no longer empty after the first run.** A saved window layout stores
  the arrangement and each panel's identity but never the panels' contents, and the two document tabs
  were the only ones not registered to be filled back in — so from the second launch onward they came
  back correctly placed, correctly titled and completely blank, while every other panel restored fine.
- **Settings you change now survive a restart.** *Auto Draw Shapes* could not be turned off: the
  checkbox declared itself as ticked in the window's markup, and that initial value was written over
  your saved setting and persisted while the window was still being built — before the settings file
  had even been read. *Zoom to Fit on Run* and *Auto Update Canvas* were exposed to the same fault.
- **Application settings no longer load only when a project is open.** With no project, the Settings
  tab showed built-in defaults for every global setting — snap, highlight, export background, default
  shape colours — and because **Save Settings** writes all of them back from what the tab is showing,
  pressing it replaced your saved values with those defaults.

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
