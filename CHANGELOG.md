# Changelog

All notable user-facing changes to DoodleSharp are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses calendar
versioning (`YEAR.MONTH.PATCH`).

Each GitHub release also carries auto-generated notes built from the commit log between
tags; this file is the curated, human-friendly summary.

## [Unreleased]

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

### Known limitations
- The rasterizer draws geometry beneath text, dimensions and annotation rather than in
  strict creation order. On technical drawings this is what you want; if you need exact
  ordering, set `RenderBackend` to `Legacy` in settings.
- A GPU backend was planned and is **not** included. The adaptive renderer reaches 21 fps
  on the heaviest benchmark frame; going further needs Direct3D, which cannot be exercised
  on the GPU-less CI runners that build these releases. Shipping an untestable device path
  was judged the wrong trade for a first release.
