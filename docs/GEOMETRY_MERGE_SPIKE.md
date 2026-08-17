# Geometry Merge Spike & Migration Plan

Companion to [`GEOMETRY_STRATEGY.md`](GEOMETRY_STRATEGY.md). That doc records *why*
two geometry namespaces once existed and the parity-test guardrail. **This** doc records
the plan and the measured results of the spike that **collapsed them into one**. It is a
historical record of completed work, kept for the reasoning rather than as live guidance.

Status: **DONE — migration complete on `feature/geometry-unification`.** `DoodleSharp.Geometry`
is deleted; the app runs on `C2VGeometry`. Builds clean (app + Tests), suite
green, render visually verified. Increments: (1) RayCaster port, (2) ShapeDefaults reconcile,
(3) repoint + delete. The repoint exposed a polygon-edge `VLine` auto-registration bug (caught
by the visual render smoke test, not unit tests) — fixed to `VLine.Internal`, guarded by
`Tests/GeometryRegistryPollutionTests.cs`. See `GEOMETRY_STRATEGY.md` for the end state.

---

## 1. The problem (the real DRY pain)

| Layer | Status |
|---|---|
| **Editor** (`Editor/`, ~13K LOC) | ✅ Single implementation. No duplication. |
| **Geometry** | ❌ **The big one.** `DoodleSharp.Geometry` (~16K LOC, WPF-coupled, `VPoint` coords, hard-wired to `CanvasRenderer.Instance`) vs `C2VGeometry` (~15K LOC, WPF-free library, `VXYZ` coords, pluggable `IShapeRegistry`). ~99% identical logic. **Every shape/curve/fix is written twice.** |
| **Sketch runtime** | ❌ Two near-identical `Sketching` namespaces (~470 LOC). |
| **Canvas drawing** | ❌ `RenderCanvas` (full editor) vs `AnimCanvas` (pan-only) draw the same primitives. |
| **Console** | ❌ Two near-identical `ConsoleOutput`. |

Everything below the editor is duplicated **because** the two canvases speak different
geometry namespaces. Unify geometry → the rest collapses. Note: DoodleSharp *already*
references `C2VGeometry.csproj` and already runs sketches (`ModuleCompiler` detects a
`Sketch` subclass → `DoodleSharp.Sketching.SketchRuntime`, converting shapes onto its canvas
via `Sketch/C2VGeometryAdapter.cs`).

**Target:** retire `DoodleSharp.Geometry`; make DoodleSharp consume `C2VGeometry` directly, with
`CanvasRenderer` implementing `C2VGeometry.IShapeRegistry`. The adapter then disappears.

---

## 2. Measured blast radius (read-only)

- **42 files** reference the `DoodleSharp.Geometry` namespace (the migration surface).
- **133 shape-type `switch` arms** across 7 files (`RenderCanvas` has 76).
  - **Key finding:** both namespaces use **identical type names** (`VCircle`, `VPoint`, …),
    so swapping `using DoodleSharp.Geometry;` → `using C2VGeometry;` **rebinds most
    `case VCircle:` arms with no textual edit.** The breakage is *not* the switch arms.
- Real edit-points concentrate in: the **coordinate ripple** (`VPoint` → `VXYZ`),
  `ShapeDefaults` (DoodleSharp-only), `RayCaster` (DoodleSharp-only, ~920 LOC, must be ported),
  and the canvas registry/collection seam.

---

## 3. The VCircle spike — what was done & what it proved

Done in worktree `worktree-vcircle-geometry-spike` (~211 LOC across 3 files):

1. **Registry seam** — `CanvasRenderer : C2VGeometry.IShapeRegistry`
   (`Register`/`Unregister`/`MoveAbove`/`MoveBehind` + a parallel `_c2vShapes` list;
   in the real migration this list *replaces* `_shapes`, not sits beside it).
2. **Render seam** — a draw pass over `GetC2VShapes()` + `DrawCircleC2V` (a **verbatim**
   copy of `DrawCircle` with the parameter retyped to `C2VGeometry.VCircle`).
3. **Test** — `Tests/VCircleRegistrySpikeTests.cs`, 4 facts in the `CanvasState` collection.

### Results
- ✅ Builds clean (0 errors).
- ✅ **4/4 spike tests pass**, **152/152 full suite** (no regressions from the `Clear()` change).
- ✅ A `C2VGeometry.VCircle` auto-registers onto the DoodleSharp canvas, reports correct
  `GetBounds()`, `Clear()` unregisters + resets the id counter, and `SendBehind` reorders
  draw order — **all with no `C2VGeometryAdapter`.**

### Measured friction (the whole point of the spike)
| Friction | Severity | Notes |
|---|---|---|
| **Registry seam** | 🟢 None | `C2VGeometry.Shape` already routes registration through `IShapeRegistry` and already has full property parity (Id, Name, IsPlaced, IsSelected, IsVisible, all styling + animation props). `CanvasRenderer` became a registry in ~50 LOC. |
| **Per-shape render port** | 🟢 Mechanical | `DrawCircle`'s body reads only `.Center.X/.Y`, `.Radius`, and shared style/animation props → coordinate-type-agnostic. Each `DrawX` is a retype, not a rewrite. |
| **Enum duplication** | 🟡 Small, pervasive | `circle.LineType` is `C2VGeometry.LineType`; `GetCachedPen` wants `DoodleSharp.Geometry.LineType` → needed `(LineType)(int)` cast. Same applies to `ControlPoint`, `BoundingBox`, `ControlPointType` — all duplicated across namespaces. The migration **deletes** the DoodleSharp copies, so these casts vanish (they're an artifact of the *parallel* spike, not the end state). |
| **Coordinate ripple** | 🟠 The real cost | `VPoint` → `VXYZ`. `DrawCircle` dodged it (only `.X/.Y`), but `MoveControlPoint(int, VPoint)` signatures, `SnapEngine`, `SelectionTool`, `MeasuringTool`, `PropertiesPanel`, exporters, and `CodeGenerator` all traffic in `VPoint`. This is the bulk of the manual work. |
| **`RayCaster` / `ShapeDefaults`** | 🟠 Port required | Exist only in `DoodleSharp.Geometry`. Must be ported into `C2VGeometry` (or kept app-side against `C2VGeometry` types). |
| **Snap coupling** | 🟠 Verify | `SnapEngine` is `DoodleSharp.Geometry`-typed; snapping to a native C2V shape needs it retyped. (Flagged in the spike; not yet exercised end-to-end.) |

**Bottom line:** the *seam* is free; the *cost* is the `VPoint`→`VXYZ` coordinate ripple
across ~42 files plus porting `RayCaster`/`ShapeDefaults`. Because type names are shared,
much of it is a find/replace of `using` directives, not logic edits.

---

## 4. Full migration plan (phased)

1. **Coordinate unification first.** Decide `VXYZ` is the canonical coordinate (it is — it's
   a plain value type, not a `Shape`, unlike DoodleSharp's overloaded `VPoint`). Port
   `RayCaster` + `ShapeDefaults` into/against `C2VGeometry`. Add `Shape.Default*` ↔
   `ShapeDefaults` reconciliation.
2. **Registry seam (done in spike).** `CanvasRenderer : IShapeRegistry`, `_shapes` becomes
   `List<C2VGeometry.Shape>`, set `C2VGeometry.Shape.DefaultRegistry = CanvasRenderer.Instance`
   on the `Main()` path. Delete `C2VGeometryAdapter` + `C2VGeometryRegistry`.
3. **Repoint the app.** Swap `using DoodleSharp.Geometry;` → `using C2VGeometry;` across the 42
   files; fix the `VPoint`→`VXYZ` signature mismatches the compiler flags; delete the
   `DoodleSharp.Geometry` folder.
4. **Render port.** `RenderCanvas` `DrawX` methods retype to `C2VGeometry` shapes (switch
   arms rebind for free). Retire `GeometryParityTests` (one library now).
5. **Then** unify the sketch runtime + console, and either share `RenderCanvas`/extract a
   `ShapeRenderer` so `AnimCanvas` and `RenderCanvas` stop duplicating draw code.

Sizing: the spike confirms each shape's render+register port is ~mechanical; the schedule
is dominated by the coordinate ripple and re-testing, not by novel design. Recommend doing
it shape-family by shape-family behind the parity tests until the last `DoodleSharp.Geometry`
reference is gone.

---
