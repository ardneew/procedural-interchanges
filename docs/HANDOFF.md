# Stage 1 Triage Handoff: Trumpet MVP, Bridge Clearance & UI Telemetry

**Handoff Date**: 2026-11-04  
**Target Next Stage**: Stage 2 (Implementation Planning / Principal Architect)  
**Status**: Ready for Handoff  

---

## 1. Observable Incidents & Field Evidence Summary

| # | Incident | Source Evidence | Observable Symptoms |
|---|---|---|---|
| **1** | **UI Crash on Engineering Standards Tab** | Field Bug Report | Clicking "Engineering" tab throws React render exception: `Cannot read properties of undefined (reading 'length')`. UI panel becomes unresponsive. |
| **2** | **Underground Lanes Failure / Portal Clashes** | Pic 1 (`media_1793774190862_181a8dc7.png`) | Depressed/underground ramps dive abruptly at surface lane splits, clipping into terrain surfaces and creating broken portal geometries. (Scope decision: Deactivate underground mode for MVP). |
| **3** | **Leg Attachment Broken Path Icons & Mangled Transitions** | Pic 2 (`media_1793774190873_93cb07be.jpg`) & Pic 4 (`media_1793774190874_04aae838.png`) | In Pic 2, one leg entrance/exit fails to connect to host highway, displaying "Road Disconnected" icon above stub. In Pic 4, lane markings overlap diagonally, lane arrows terminate into curbs, and carriageway tapers buckle. |
| **4** | **Bridge Elevation Profile Wonkiness & Excessive Clearance** | Pic 3 (`media_1793774190874_26c456c5.png`) | Overpass deck undulates vertically across piers like a rollercoaster, reaching $12\text{m}–18\text{m}$ in mid-air. User placed a reference bridge confirming in-game minimum clearance is exactly **$7.5\text{m}$**. |
| **5** | **Massive Footprint & Distorted Trumpet Geometry** | Pic 5 (`media_1793774190876_72aada7d.png`) | Top-down comparison shows generated trumpet is disproportionately massive compared to standard trumpet asset. Loop ramp extends far into the distance ($R_{\text{loop}} > 60\text{m}$), and flyover ramp departs at an unnatural wide angle. |

---

## 2. Codebase Touchpoints & Data Compilations

### A. Frontend UI (`ui/src/`)
- [`ui/src/components/FineTuningInspector.tsx`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/ui/src/components/FineTuningInspector.tsx):
  - Line 21–32: Reads `availablePrefabs` and `legs` arrays.
  - Line 153–166: Contains `<select>` dropdown for `undergroundMode` (values 0–3).
  - Line 44–99: Engineering standard sliders (`footprintRadius`, `rotationDeg`, `maxMainlineGrade`, `maxRampGrade`, `minClearance`, `designSpeed`).
- [`ui/src/components/CustomDropdown.tsx`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/ui/src/components/CustomDropdown.tsx):
  - Line 27: `options.find((o) => o.value === value)`
  - Line 58: `options.map((opt) => ...)`
- [`ui/src/components/InterchangeStudioPanel.tsx`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/ui/src/components/InterchangeStudioPanel.tsx):
  - Line 119, 158, 186, 328: Multiple `legs.length` and array evaluations.

### B. Procedural Geometry & Archetype Engine (`Core/Generators/` & `Core/Models/`)
- [`Core/Generators/TrumpetGenerator.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Core/Generators/TrumpetGenerator.cs):
  - Line 31: Uses `config.FootprintRadius` directly (defaulting to 260m).
  - Line 60–62: Fixed stem offsets ($26\text{m}$) projecting overpass and slip nodes outward.
  - Line 150: `loopRadius = math.clamp(R * 0.26f, 44.0f, 62.0f)`.
  - Line 155: `BiArcFit.FitConnectedLoop270(dB, loopStartDir, mS_loop, loopEndDir, loopRadius, (int)sigma, center.y, center.y + config.MinBridgeClearanceMeters, center.y, loopArcs)`.
- [`Core/Models/InterchangeConfig.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Core/Models/InterchangeConfig.cs):
  - Line 31: `FootprintRadius = 260f;` (Range 120m–500m).
  - Line 38: `MinBridgeClearanceMeters = 7.5f;`.
  - Line 46: `UndergroundMode Underground = UndergroundMode.None;`.

### C. Elevation & Vertical Clearance Solver (`Core/Math/ClearanceAndPillarSolver.cs`)
- [`Core/Math/ClearanceAndPillarSolver.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Core/Math/ClearanceAndPillarSolver.cs):
  - Line 37: `float effectiveClearance = math.max(8.8f, config.MinBridgeClearanceMeters + 0.8f);` (Overrides config min to $\ge 8.8\text{m}$).
  - Line 845–875: Elevated chain waypoint calculations, peak elevation plateau positioning (`minCrossSElev`, `maxCrossSElev`), and slope clamping passes (Line 920–930).
  - Line 1134: `bool clearanceViolation = c.StackLevel > 0 && c.MinClearanceMeters < 7.8f;`.

### D. Highway Leg Splicing & Road Attachment (`Core/Generators/LegPortalGeometry.cs` & `Systems/InterchangeBuildService.cs`)
- [`Core/Generators/LegPortalGeometry.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Core/Generators/LegPortalGeometry.cs):
  - Line 70–73: Dual carriageway runout distance: `math.clamp(footprintRadius * splitFraction, 12f, 45f)`.
  - Line 95–128: Terminal course generation attaching `inAttach` and `outAttach` entities with `StartSplitPosition` and `EndSplitPosition`.
- [`Systems/InterchangeBuildService.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Systems/InterchangeBuildService.cs):
  - Line 450–468: Verification of `validStartAttach` / `validEndAttach` entities against deleted components.
  - Line 553–575: Construction of `CoursePos` with `NetUtils.GetNodeRotation`, `m_SplitPosition`, and `CoursePosFlags`.

---

## 3. Readiness Checklist for Stage 2 (Architect Blueprint)

- [x] Raw incident facts objectively logged in [`docs/BUGS.md`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/docs/BUGS.md) without speculative solution bias.
- [x] All 5 user-provided visual and UI issues mapped to exact source files and line ranges.
- [x] Verified CS2 engine ground truth: minimum vertical deck clearance is $7.5\text{m}$.
- [x] Scope boundary confirmed: Underground mode disabled for MVP; focus strictly on 3-way Trumpet interchange.
- [x] Handoff contract written to [`docs/HANDOFF.md`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/docs/HANDOFF.md).

**Decision**: Ready for Principal Architect handoff.
