# Changelog

All notable user-facing changes to DoodleSharp are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses calendar
versioning (`YEAR.MONTH.PATCH`).

Each GitHub release also carries auto-generated notes built from the commit log between
tags; this file is the curated, human-friendly summary.

## [Unreleased]

### Added
- **DoodleSharp**, a WPF environment for drawing 2D geometry by writing C#. It began as a
  fork of Code2Viz 2026.8.7 and keeps that application whole: the Roslyn-powered editor
  with IntelliSense and refactoring, the interactive canvas with drawing and measuring
  tools, the `C2VGeometry` shape library, charts, boolean operations and regions, the
  animation timeline, Global Parameters, the properties panel, undo/redo, PNG/GIF/MP4/DXF/
  PDF/SVG export, NuGet integration, F1 help, and the crash journals.

### Changed
- Renamed throughout: the application, its assembly, its solution and its `DoodleSharp.*`
  namespaces. **The geometry library is unchanged** — it is still `C2VGeometry`, with the
  same namespace and the same public API, so an existing sketch or `.vizproj` compiles as
  it did before.
- The installer registers a new application id, so DoodleSharp installs alongside an
  existing Code2Viz rather than upgrading over it.

### Removed
- **The Animator sub-application** (`Animator.exe`) and its `SketchHost` process-isolation
  child. With it go the *Switch to Animator* button, the welcome screen's Code/Animate mode
  toggle, and the recent-animations list. Sketch mode inside the main app is untouched.
- **The Blazor web app** and its Cloudflare Pages deployment workflow.
- **The MCP server and bridge** — the named-pipe listener that let an external agent drive
  the application is gone, along with its skill and API-reference documents.
