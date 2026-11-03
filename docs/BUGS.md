# Bug Tracker & Triage Log

This file tracks active bugs, engine validation errors, and to-fix items discovered during gameplay and development.

---

## Active Issues

### [BUG-004] Crash on Consecutive GUI Undo in `lib_burst_generated.dll`
- **Status**: Triaged
- **Date**: 2026-11-03 00:49:03
- **Severity**: Crash to Desktop
- **Observed Symptom**: User clicked the "Undo" button once in the React GUI; it undid part of the interchange. User clicked "Undo" a second time, and the game immediately crashed to desktop.
- **Context & Environment**:
  - Tool Step: Consecutive Undo in GUI
  - Active Version: v0.0.3 (`ProceduralInterchanges.dll`)
- **Raw Evidence & Logs**:
  - Minidump: `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\.cache\backtrace\crashpad\reports\8bfbd13c-7ba5-47c5-aa0e-1f338208c0c0.dmp`
  - Timestamp: 2026-11-03 00:49:03
  - Exception Code: `0xc0000005` (Access Violation / Null Pointer Dereference)
  - Crashing Thread: 87468
  - Crashing Module: `lib_burst_generated.dll` at base `0x7ff902220000` + offset `0x1583fa9` (`0x7ff9037a3fa9`)
- **Reproduction Steps**:
  1. Build an interchange using the mod.
  2. Click the GUI "Undo" button once (partially removes interchange).
  3. Click the GUI "Undo" button a second time while the first undo transaction is settling or with empty/invalidated snapshot entities.
  4. Game crashes to desktop with `0xc0000005` in Burst compiler job.

---

### [BUG-005] Non-Connected Disjoint Segments, Highway Exit Overlap Collision, and Disconnected Stubs
- **Status**: Triaged
- **Date**: 2026-11-03
- **Severity**: Placement & Geometric Integration Failure
- **Observed Symptom**:
  1. Roads placed by the mod do not connect to each other — they generate as individual disjoint segments that fail to weld/join at intersections (Photo 2).
  2. Interchange exit ramp completely overlaps and collides directly onto an existing straight through highway (Photo 2).
  3. Boundary approaches do not connect directly to existing road centerlines; they only drop nodes at arbitrary ring coordinates, leaving disconnected U-turn stubs and dead-end cul-de-sacs (Photo 1).
- **Raw Evidence & Logs**:
  - User uploaded Photo 1: `media_1793684614782_c4e56146.jpg` (shows disconnected dual-carriageway U-turn loop stub and displaced split)
  - User uploaded Photo 2: `media_1793684703702_b4fa46bc.jpg` (shows overpass ramp clipping into lower abutment and crossing at zero-angle overlap)
- **Reproduction Steps**:
  1. Pick center and select existing highways.
  2. Commit placement.
  3. Inspect boundary nodes and interior merge/diverge points.

---

### [BUG-008] UI Disappears on Clicking 'Fine-Tuning' Button [RE-OPENED]
- **Status**: Triaged (Re-opened)
- **Date & Build**: 2026-11-03 02:20:50 (v0.0.4) & 2026-11-03 03:10:38 (v0.0.5)
- **Severity**: UI Crash / Total Frontend Disappearance
- **Observed Symptom**:
  - Clicking the "Fine-Tuning" tab button causes the entire game UI (both mod panel and native game UI) to disappear. Game camera navigation and 3D world rendering remain active.
- **Raw Evidence & Logs**:
  - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\UI.log` (Timestamp: 2026-11-03 03:10:38):
    ``
    [2026-11-03 03:10:38,423] [ERROR] TypeError: Cannot read properties of undefined (reading 'length')
     While executing JS :15:23
    cohtml.Net.LogHandler:SwigDirectorWriteLog(Int32, String, UInt64, IntPtr)
    cohtml.Net.cohtmlNativePINVOKE:View_MouseEvent(HandleRef, HandleRef)
    cohtml.Net.View:MouseEvent(IMouseEventData)
    Colossal.UI.UIInputSystem:DispatchInputEvents(UIView, Boolean, Boolean, InputEventType)
    Colossal.UI.UIInputSystem:DispatchInputEvents(Boolean)
    Game.SceneFlow.GameManager:Update()
    ``
- **Reproduction Steps**:
  1. Open mod tool in game.
  2. Click the "Fine-Tuning" button/tab.
  3. UI crashes immediately with `TypeError: Cannot read properties of undefined (reading 'length')`.

---

### [BUG-009] Undo Operation Deletes Pre-Existing Host Highway Segments
- **Status**: Triaged
- **Date**: 2026-11-03 03:14:36
- **Severity**: Data Loss / Network Entity Invalidation
- **Observed Symptom**:
  - After placing and undoing several interchanges, the undo action deleted two pre-existing road segments (each 1 segment long) that belonged to the user's highway network rather than just the mod-created interchange entities.
- **Context & Environment**:
  - Archetype: Interchanges placed and undone repeatedly.
  - Tool Step: GUI Undo.
  - Active Version: v0.0.5 (`ProceduralInterchanges.dll`).
- **Raw Evidence & Logs**:
  - User gameplay observation: 2 host road segments deleted upon undo.
- **Reproduction Steps**:
  1. Have pre-existing highway segments.
  2. Place interchange connecting to them.
  3. Execute Undo.
  4. Pre-existing road segments are destroyed along with the interchange.

---

### [BUG-010] Parclo A4 Archetype Geometry Deviation (Non-Standard Ramp Configuration)
- **Status**: Triaged
- **Date**: 2026-11-03 03:14:36
- **Severity**: Geometric / Archetype Design Flaw
- **Observed Symptom**:
  - Parclo A4 generation does not reflect the standard Parclo A4 interchange layout (Photo 2 and Photo 3).
  - Ramps create sharp, acute angles and non-smooth turns at intersections rather than gradual highway diverges/merges.
- **Raw Evidence & Logs**:
  - User uploaded Photo 2 (`media_1793693659381_e36fcfa5.jpg`): Shows Parclo ramp intersecting perpendicular cross-street at sharp corner with overlapping carriageway and abrupt right angles.
  - User uploaded Photo 3 (`media_1793693669062_40d3a0d6.jpg`): Shows overall 4-way layout with distorted ramp geometry and excessive footprint.
- **Reproduction Steps**:
  1. Select Parclo A4 archetype.
  2. Generate interchange across intersecting highways.
  3. Inspect ramp layout and turning angles.

---

### [BUG-011] Oversized Interchange Footprint and Tight/Discontinuous Portal Connection Curves
- **Status**: Triaged
- **Date**: 2026-11-03 03:14:36
- **Severity**: Geometric / Aesthetic Defect
- **Observed Symptom**:
  - Many generated interchanges are built excessively large in overall footprint (Photo 3).
  - The boundary connection areas (red highlight in Photo 1 and Photo 3) connecting the start of the interchange to the incoming host highway curve too tightly.
  - The connection does not form a continuous, smooth geometric curve following previous tangents; instead it renders as an abrupt, random straight line segment inserted between curves.
- **Raw Evidence & Logs**:
  - User uploaded Photo 1 (`media_1793693640280_14c3e8df.jpg`): Highlights red portal connections with sharp kinks and non-tangential transitions.
  - User uploaded Photo 3 (`media_1793693669062_40d3a0d6.jpg`): Displays disproportionate scale across the quad junction and sharp kinks at all four highway portal transitions.
- **Reproduction Steps**:
  1. Select 4-leg highway junction.
  2. Place interchange (e.g. Parclo A4 or Turbine/Directional).
  3. Observe transition curve at the four portal approaches (red highlighted zones).

---

## Past & Resolved Issues (Historical Reference)

### [BUG-006] Terminal Courses Spawn as 6-Lane Two-Way Road ("Golden Gate Bridge") at Highway Portals [RESOLVED - v0.0.5]
- **Status**: Resolved (v0.0.5)
- **Date & Build**: 2026-11-03 02:01:12 (v0.0.4 build) -> Fixed in v0.0.5 (02:58:57)
- **Raw Symptom**: In a 3-way Trumpet interchange, the three outer connection stubs where the interchange connects to the highways spawned as 6-lane two-way roads with crosswalks and were labeled "Golden Gate Bridge" (while interior interchange loops spawned as "Overbrook Highway").
- **Raw Evidence & Logs**:
  - User uploaded Photo 1 (`media_1793689955149_583e1f78.jpg`): Shows 3-way Trumpet with wide 6-lane two-way road segments labeled "Golden Gate Bridge" containing crosswalks at each of the 3 portals.
- **Resolution**:
  - Filtered out `SignatureBuildingData`, `PlacedSignatureBuildingData`, zoning-enabled networks, and networks with pedestrian sidewalks/crosswalks from `HighwayPrefabCatalogSystem.BuildCatalog()`.
  - Added dedicated fallback for two-way highways (`Two-Lane Two-Way Highway`).
  - Updated `TemplateMorphSolver.EmitTwoWayTrunk` to inherit the clicked road's exact prefab and match lane count.

---

### [BUG-007] Cannot Add 4th Road After Undo; 4th Click Rejected [RESOLVED - v0.0.5]
- **Status**: Resolved (v0.0.5)
- **Date & Build**: 2026-11-03 02:01:12 (v0.0.4 build) -> Fixed in v0.0.5 (02:58:57)
- **Raw Symptom**: After placing a 3-way interchange and clicking Undo, attempting to add a 4th highway end to create a 4-way interchange does nothing. Clicking on a 4th road does not add it and the tool immediately returns to the 3-leg overview.
- **Raw Evidence & Logs**:
  - User test report: After undoing a 3-way interchange, returning to road selection and clicking on a 4th road did not add it; the tool skipped directly back to the overview before construction.
- **Resolution**:
  - In `InterchangeToolSystem.cs`, allowed selecting up to 4 legs (`CurrentLegs.Count < 4`).
  - Added automatic promotion to 4-leg archetype (`Diamond`) when a 4th leg is added, and automatic adjustment to 3-leg archetype (`Trumpet`) when down to 3 legs.

---

## [REQ-001] Separation of Requirements: Technical Implementation vs. Test Suite

> **Note**: As requested, testing-related issues and verification suites are maintained and fixed in a separate chat session. The requirements are segregated below into core technical implementation vs. test suite.

### Category A: Core Technical Implementation (Mod Codebase)
1. **Host Entity Snapshot Isolation during Undo**:
   - Ensure the undo history record tracks strictly entities instantiated by `ProceduralInterchanges` (`CreationDefinition` / `NetCourse`) and never tracks or deletes pre-existing host road segments or split-parent entities when reversing operations.
2. **Parclo A4 Archetype Realignment**:
   - Re-architect the Parclo A4 template morphology: standard loop ramps in two quadrants (on the exit approach side of the arterial) and directional/outer ramps to avoid sharp perpendicular doglegs.
3. **Continuous Tangential Portal Transitions ($G^1$ / $C^1$ Continuity)**:
   - Ensure the boundary connection segments (red portal areas) match both the incoming highway position and tangent vector continuously.
   - Eliminate straight disjoint chord insertions; replace with smooth Bézier/clothoid alignment matching previous curves.
4. **Footprint Scaling / Sizing Control**:
   - Constrain the global bounding footprint of the archetypes so they do not expand excessively relative to the highway right-of-way.
5. **Fine-Tuning GUI Cohtml / React Null-Safety**:
   - Prevent unhandled JS `TypeError: Cannot read properties of undefined (reading 'length')` inside `FineTuningInspector` or state bindings when switching tabs.

### Category B: Test Suite (Handled in Separate Chat)
1. **Ramp Curvature / Sharp Turn Detection Metric**:
   - Add automated test assertions verifying that no generated ramp Bézier segment exceeds a maximum curvature threshold $\kappa_{\max}$ (or minimum turning radius {\min} \ge 35\text{ m}$ for highway loops and $\ge 60\text{ m}$ for directional ramps).
2. **Tangent Continuity (^1$) Verification at Portals**:
   - Add unit/integration tests to assert that angle delta $\Delta \theta$ between incoming road end-tangent and portal ramp start-tangent is within $\le 5^\circ$.
3. **Footprint Bounding Box Assertions**:
   - Add automated boundary tests asserting maximum interchange envelope dimensions (width/height/area) for each archetype.
4. **Non-Destructive Undo Verification**:
   - Add test scenario verifying that pre-existing entities present prior to generation retain identical entity IDs and component values after an Undo transaction completes.