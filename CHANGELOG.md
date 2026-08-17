# Changelog

All notable user-facing changes to DoodleSharp are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses calendar
versioning (`YEAR.MONTH.PATCH`).

Each GitHub release also carries auto-generated notes built from the commit log between
tags; this file is the curated, human-friendly summary.

## [Unreleased]

## [2026.8.0] - 2026-08-17

### Added
- **DoodleSharp**, a WPF environment for drawing 2D geometry by writing C#. It began as a
  fork of Code2Viz 2026.8.7 and keeps that application whole: the Roslyn-powered editor
  with IntelliSense and refactoring, the interactive canvas with drawing and measuring
  tools, the `C2VGeometry` shape library, charts, boolean operations and regions, the
  animation timeline, Global Parameters, the properties panel, undo/redo, PNG/GIF/MP4/DXF/
  PDF/SVG export, NuGet integration, F1 help, and the crash journals.
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

### Changed
- Renamed throughout: the application, its assembly, its solution and its `DoodleSharp.*`
  namespaces. **The geometry library is unchanged** — it is still `C2VGeometry`, with the
  same namespace and the same public API, so an existing sketch or `.vizproj` compiles as
  it did before.
- The installer registers a new application id, so DoodleSharp installs alongside an
  existing Code2Viz rather than upgrading over it.
- `Shape` gained a `Revision` counter and an `Invalidate()` method, used to keep cached
  geometry in step. If you modify a shape's point list in place — rather than assigning a
  new one — call `Invalidate()` so the cache is rebuilt.

### Removed
- **The Animator sub-application** (`Animator.exe`) and its `SketchHost` process-isolation
  child. With it go the *Switch to Animator* button, the welcome screen's Code/Animate mode
  toggle, and the recent-animations list. Sketch mode inside the main app is untouched.
- **The Blazor web app** and its Cloudflare Pages deployment workflow.
- **The MCP server and bridge** — the named-pipe listener that let an external agent drive
  the application is gone, along with its skill and API-reference documents.

### Known limitations
- The rasterizer draws geometry beneath text, dimensions and annotation rather than in
  strict creation order. On technical drawings this is what you want; if you need exact
  ordering, set `RenderBackend` to `Legacy` in settings.
- A GPU backend was planned and is **not** included. The adaptive renderer reaches 21 fps
  on the heaviest benchmark frame; going further needs Direct3D, which cannot be exercised
  on the GPU-less CI runners that build these releases. Shipping an untestable device path
  was judged the wrong trade for a first release.
