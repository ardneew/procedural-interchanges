# Procedural Interchanges — Agent Guidelines

These guidelines apply specifically to this repository and build on top of the global Cities: Skylines II modding rules.

---

## 1. Project Architecture & File Overview
Any agent working on this codebase should orient themselves using this layout:

- **`Core/` (Pure C# Mathematical & Algorithmic Domain — No Direct Unity UI Dependencies)**:
  - `Core/Math/`: 3D cubic Bézier evaluators (`CubicBezier3D.cs`), topography/heightfield fitters (`TopographySolver.cs`), bi-arc curve fitters (`BiArcFit.cs`).
  - `Core/Models/`: Data structures (`InterchangePlan.cs`, `PlannedCourse.cs`, `HighwayLeg.cs`, `InterchangeConfig.cs`).
  - `Core/Generators/`: Parametric archetype generators (`DiamondGenerator.cs`, `TrumpetGenerator.cs`, `ParcloA4Generator.cs`, `CloverleafGenerator.cs`, `RoundaboutInterchangeGenerator.cs`, `LegPortalGeometry.cs`).
  - `Core/Integration/`: Highway pairing (`HighwayPairingSolver.cs`) and through-corridor boundary cutting/splicing (`TrimAndSpliceSolver.cs`).
  - `Core/Templates/`: Workshop asset library definitions (`WorkshopTemplateLibrary.cs`) and non-linear diffeomorphism coordinate deformation (`TemplateMorphSolver.cs`).

- **`Systems/` (Unity DOTS / CS2 Simulation & Tool Systems)**:
  - `InterchangeToolSystem.cs` & `InterchangeToolSystem.Overlay.cs`: Main tool state machine, raycasting, hover/footprint outline rendering.
  - `InterchangeBuildService.cs`: ECS execution barrier emitting `CreationDefinition` + `NetCourse` with sequential edge-split stages and undo/receipt management.
  - `InterchangeUISystem.cs`: C#-to-Gameface bridge registering `Colossal.UI.Binding` triggers and values.
  - `HighwayPrefabCatalogSystem.cs`: Queries and caches unlocked highway prefabs, lane counts, and directional flags.

- **`ui/` (Coherent Gameface / React 18 Frontend)**:
  - `ui/src/components/`: Studio panel (`InterchangeStudioPanel.tsx`), fine-tuning controls (`FineTuningInspector.tsx`), dropdowns, and SVG preview canvas.
  - `ui/src/bindings/`: Type-safe bindings connecting React frontend to `InterchangeUISystem.cs`.

- **`Tests/` (Automated Testing & Visual Verification)**:
  - `Tests/ProceduralInterchanges.Tests/`: xUnit unit test suite for spline math, clearance physics, and archetype geometric regression (`dotnet test`).
  - `Tests/ProceduralInterchanges.Lab/`: Headless SVG plan visualizer generating standalone test graphics to `docs/lab-gallery/`.

- **`docs/` (Handoffs, Logs & Records)**:
  - `docs/BUGS.md`: Incident tracker for observable bug reports.
  - `docs/HANDOFF.md`: Bridge between triage and architectural planning.
  - `docs/PLAN.md`: Active architectural blueprint.
  - `docs/FUTURE_IDEAS.md`: Long-term feature roadmap.

---

## 2. Development & Quality Protocols
- **4-Stage Development Lifecycle**: When handling multi-step tasks or end-to-end debugging, defer to the [pipeline skill](file:///.agents/skills/pipeline/SKILL.md) (`Triage` -> `Planning` -> `Execution` -> `QA`).
- **Incident & Bug Tracking**:
  - Raw evidence and observable incident facts are logged in [`docs/BUGS.md`](file:///docs/BUGS.md).
  - Triage is strictly an objective incident recorder: record verifiable logs and symptoms without speculative root-cause hypotheses to prevent premature bias during architecture analysis.
- **Handoff & Planning Artifacts**:
  - Handoffs from triage to architecture analysis are written to [`docs/HANDOFF.md`](file:///docs/HANDOFF.md).
  - High-level architectural blueprints and deterministic execution plans are written to [`docs/PLAN.md`](file:///docs/PLAN.md).

---

## 3. Engineering Invariants & Roles
- **Principal Architect Role**: Owns 100% of mathematical formulation (clothoid/Bézier splines, elevation profiles), system contracts, and ECS lifecycle design from first principles. Blueprints must be granular, deterministic, and unambiguous.
- **Execution Role**: Strictly implements the blueprint with a minimal deviation policy (fixing minor typos/syntax allowed; no ad-hoc architectural redesigns). Always report completed items, fixes, and any blocked items.
- **Verification & QA**: All geometry, spline, and archetype changes must verify against the unit test suite (`dotnet test Tests/ProceduralInterchanges.Tests/ProceduralInterchanges.Tests.csproj`) with 0 regressions.
