# Bug Tracker & Triage Log

This file tracks active bugs, engine validation errors, and to-fix items discovered during gameplay and development.

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

---

## [BUG-006] Terminal Courses Spawn as 6-Lane Two-Way Road ("Golden Gate Bridge") at Highway Portals
- **Date & Build**: 2026-11-03 02:01:12 (v0.0.4 build)
- **Raw Symptom**: In a 3-way Trumpet interchange, the three outer connection stubs where the interchange connects to the highways spawned as 6-lane two-way roads with crosswalks and were labeled "Golden Gate Bridge" (while interior interchange loops spawned as "Overbrook Highway").
- **Raw Evidence & Logs**:
  - User uploaded Photo 1 (`media_1793689955149_583e1f78.jpg`): Shows 3-way Trumpet with wide 6-lane two-way road segments labeled "Golden Gate Bridge" containing crosswalks at each of the 3 portals.
- **Reproduction Steps**:
  1. Open tool on a map with highways.
  2. Click 3 highway ends with a 3-way Trumpet selected.
  3. Commit placement.
  4. Inspect the 3 portal segments connecting to the highways.

---

## [BUG-007] Cannot Add 4th Road After Undo; 4th Click Rejected
- **Date & Build**: 2026-11-03 02:01:12 (v0.0.4 build)
- **Raw Symptom**: After placing a 3-way interchange and clicking Undo, attempting to add a 4th highway end to create a 4-way interchange does nothing. Clicking on a 4th road does not add it and the tool immediately returns to the 3-leg overview.
- **Raw Evidence & Logs**:
  - User test report: After undoing a 3-way interchange, returning to road selection and clicking on a 4th road did not add it; the tool skipped directly back to the overview before construction.
- **Reproduction Steps**:
  1. Place a 3-way Trumpet interchange.
  2. Press Undo in GUI.
  3. Return to road selection mode.
  4. Attempt to click a 4th highway endpoint in the game world.

---

## [BUG-008] UI Disappears on Clicking 'Fine-Tuning' Button
- **Date & Build**: 2026-11-03 02:20:50 (v0.0.4 build)
- **Raw Symptom**: Clicking the "Fine-Tuning" tab button causes the entire mod studio panel to disappear from the screen. Game world rendering and camera navigation remain functional, but the GUI is gone.
- **Raw Evidence & Logs**:
  - User uploaded Photo 2 (`media_1793690489597_13959e80.jpg`): Screen capture showing 3D interchange preview overlay with no UI panel visible.
  - `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Logs\UI.log`:
    ```
    [2026-11-03 02:20:50,631] [ERROR] TypeError: Cannot read properties of undefined (reading 'length') 
     While executing JS :15:23
    Colossal.UI.CoLogHandler:WriteLog(Severity, String, UInt64)
    cohtml.Net.LogHandler:SwigDirectorWriteLog(Int32, String, UInt64, IntPtr)
    cohtml.Net.cohtmlNativePINVOKE:View_MouseEvent(HandleRef, HandleRef)
    cohtml.Net.View:MouseEvent(IMouseEventData)
    Colossal.UI.UIInputSystem:DispatchInputEvents(UIView, Boolean, Boolean, InputEventType)
    Colossal.UI.UIInputSystem:DispatchInputEvents(Boolean)
    Game.SceneFlow.GameManager:Update()
    ```
- **Reproduction Steps**:
  1. Open the Procedural Interchanges panel.
  2. Click the "Fine-Tuning" tab button.



1. **Default to Right-Hand Traffic (RHT)**:
   - Mod must default to right-hand driving (`LeftHandTraffic = false` / $\sigma = +1$), matching standard game maps unless explicitly overridden.
2. **Eliminate Ring / Footprint Circle Architecture**:
   - Completely discard the circular ring radius concept.
   - Replace with direct **endpoint-to-endpoint highway selection**: user clicks on the exact ends/cut-points of the highways they want joined together.
   - The interchange generates directly between the selected highway endpoints, welding directly to their native nodes/centerlines with zero stubs.
3. **True Procedural Generation for Any Workable Angle (Not Just a Static Asset Stamp)**:
   - This must **not** be an overcomplicated mod just to paste in a rigid static asset.
   - It must remain **truly procedural**:
     - Adapt dynamically to **any workable non-90° skew angle** (e.g., $45^\circ$, $60^\circ$, $120^\circ$, $135^\circ$, or curved approaches).
     - Morph and warp the interchange geometry from the workshop template skeletons (`C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1`) while preserving proven lane topology, viaduct clearances, and weaving lengths.
     - **Warning on Severe/Tight Angles**: When a user selects severe non-orthogonal angles (e.g. a T-interchange where the stem is heavily angled to one side), display a clear in-game warning to the user that the geometry may look visually distorted at extreme skew angles. We will fine-tune and optimize these extreme angle aesthetics in later iterations.
4. **Flat Ground Testing Scope (Topography Deferred)**:
   - For this testing phase, assume flat ground ($Y = \text{constant}$). Keep the code focused on flawless 2D/3D topological connectivity, zero collision overlaps, and true road welding on flat test maps before introducing complex contour elevation sampling.
5. **Rigid Coordinate Snapping & True Road-to-Road Connectivity**:
   - Ensure all generated courses and existing highway terminations share exact bit-identical `float3` coordinates and properly merge into single nodes without gaps.
6. **State Machine Consecutive Undo Guard**:
   - Guard `UndoLastInterchange` so consecutive clicks cannot execute concurrently or operate on invalidated entity handles, preventing `lib_burst_generated.dll` access violations.
