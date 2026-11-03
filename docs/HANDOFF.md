# Triage Handoff to Argon Implementor / Principal Architect

**Date**: 2026-11-03 03:18:29  
**Session Context**: Post-playtest triage of v0.0.5 build (`ProceduralInterchanges.dll` timestamp: 2026-11-03 02:58:57).  
**Source Incident Log**: [docs/BUGS.md](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/docs/BUGS.md)

---

## 1. Executive Summary & Observed Symptoms

During manual in-game testing of interchange generation and undo workflows:
1. **Host Road Segment Deletion on Undo (`BUG-009`)**: Placing interchanges and pressing Undo resulted in two 1-segment host road sections being deleted from the user's highway network.
2. **Parclo A4 Layout Non-Conformity (`BUG-010`)**: As captured in Photo 2 and Photo 3, the Parclo A4 archetype generated sharp acute corners and dogleg turns onto intersecting roads, diverging from standard Parclo loop/directional configurations.
3. **Disproportionate Footprint & Discontinuous Portal Curves (`BUG-011`)**: Generated interchanges balloon excessively in overall footprint (Photo 3). The transition segments connecting incoming highways to the interchange (highlighted in red in Photos 1 & 3) feature sharp, tight curvature and render as discontinuous straight chords rather than smooth tangential geometric curves.
4. **GUI Total Disappearance on "Fine-Tuning" Click (`BUG-008` Re-opened)**: Clicking the "Fine-Tuning" button throws an unhandled JS exception (`TypeError: Cannot read properties of undefined (reading 'length')` in `UI.log`), crashing the Cohtml frontend and hiding all game and mod UI.

---

## 2. Visual Evidence & Artifacts

| Reference | Image Path / Upload ID | Description | Key Visible Defect |
| :--- | :--- | :--- | :--- |
| **Photo 1** | `media_1793693640280_14c3e8df.jpg` | 4-way Turbine / Directional Interchange | Red portal transitions show tight curvature with abrupt straight line kinks entering the interchange. |
| **Photo 2** | `media_1793693659381_e36fcfa5.jpg` | Parclo Ramp & Cross-Street Intersection | Acute angle / sharp dogleg ramp connection colliding into crossway with overlapping lanes instead of standard diverge geometry. |
| **Photo 3** | `media_1793693669062_40d3a0d6.jpg` | 4-way Parclo A4 Full Footprint | Oversized footprint bounding box; ramp loops deviate from standard Parclo quadrants; sharp kinks at all 4 red portal approach curves. |

---

## 3. Log Evidence & Traces

### `UI.log` (Cohtml Gameface Error)
```
[2026-11-03 03:10:38,423] [ERROR] TypeError: Cannot read properties of undefined (reading 'length')
 While executing JS :15:23
cohtml.Net.LogHandler:SwigDirectorWriteLog(Int32, String, UInt64, IntPtr)
cohtml.Net.cohtmlNativePINVOKE:View_MouseEvent(HandleRef, HandleRef)
cohtml.Net.View:MouseEvent(IMouseEventData)
Colossal.UI.UIInputSystem:DispatchInputEvents(UIView, Boolean, Boolean, InputEventType)
Colossal.UI.UIInputSystem:DispatchInputEvents(Boolean)
Game.SceneFlow.GameManager:Update()
```

### `ProceduralInterchanges.log`
```
[2026-11-03 03:00:38,158] [INFO]  OnLoad
[2026-11-03 03:00:38,158] [INFO]  Current mod asset at C:/Users/liand/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods/ProceduralInterchanges/ProceduralInterchanges.dll
```

---

## 4. Work Separation: Technical Code Implementation vs. Test Suite

> **Notice**: As instructed by the user, **all testing suite additions and verification tests are handled in a separate chat session**. The items below are split strictly between technical engine/mod implementation tasks for Argon and testing specifications for the test suite chat.

### Part A: Core Technical Implementation (Argon Implementor Scope)
1. **Non-Destructive Undo Isolation (`BUG-009`)**:
   - Audit undo snapshot buffer to ensure only entities instantiated with `CreationDefinition` during the current interchange session are tracked and destroyed.
   - Guard against capturing or invalidating existing host road segments or split-parent entities during undo rollback.
2. **Parclo A4 Archetype Realignment (`BUG-010`)**:
   - Re-derive the Parclo A4 template morphology:
     - Standard loop ramps situated in two quadrants on the exit approach side of the arterial.
     - Directional/outer ramps with smooth merges/diverges.
     - Eliminate acute perpendicular dogleg junctions on cross-streets.
3. **Continuous Tangential Portals ($G^1$ / $C^1$ Continuity) (`BUG-011`)**:
   - Formulate boundary connection curves (red portal segments) that match incoming highway tangent vectors continuously ($\vec{T}_{\text{in}} \parallel \vec{T}_{\text{ramp}}$).
   - Eliminate straight chord insertions; enforce smooth Bézier/clothoid curvature alignment.
4. **Footprint Scale & Bounds Constraints (`BUG-011`)**:
   - Scale down and constrain the template morph bounding boxes so generated archetypes remain proportional to standard right-of-ways.
5. **Fine-Tuning Cohtml Tab Crash Fix (`BUG-008`)**:
   - Address the `TypeError: Cannot read properties of undefined (reading 'length')` in frontend bundle to ensure clicking "Fine-Tuning" renders without crashing the Gameface UI.

### Part B: Test Suite (Separate Chat Scope)
1. **Ramp Curvature / Sharp Turn Detection Metric**:
   - Automated tests calculating maximum Bézier curvature $\kappa_{\max}$ along ramp trajectories.
   - Assert minimum turning radius $R_{\min} \ge 35\text{ m}$ for loops and $\ge 60\text{ m}$ for directional ramps.
2. **Portal Tangent Continuity Metric**:
   - Automated check asserting $\Delta \theta \le 5^\circ$ between the incoming host highway centerline tangent and the generated interchange portal segment.
3. **Bounding Box Envelope Check**:
   - Assert interchange bounding box dimensions (width, height, footprint area) remain within predefined maximums per archetype.
4. **Pre-Existing Entity Non-Destruction Check**:
   - Integration test verifying that host network entities existing prior to generation retain identical entity IDs and component states following an Undo call.
