# Incident Report: MVP Trumpet, Elevation & UI Issues

**Logged At**: 2026-11-04  
**Scope**: 3-Way Trumpet MVP, Bridge Elevation Profiles, Portal Connections & UI Crash  

---

## 1. Observable Incidents & Field Evidence

### Incident 1: Frontend UI Crash on Engineering Standards Tab
- **Symptom**: When switching to or clicking the "Engineering Standards" tab in the in-game studio panel, the Gameface UI crashes with a React render error referencing property reading `"length"` (e.g., `Cannot read properties of undefined (reading 'length')`).
- **Location**: [`ui/src/components/InterchangeStudioPanel.tsx`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/ui/src/components/InterchangeStudioPanel.tsx) / `FineTuningInspector.tsx`.

### Incident 2: Underground Lanes Fail to Form / Tunnel Portal Clashes (Pic 1)
- **Symptom**: In Pic 1, roads set to underground attempt to dive abruptly right at surface lane splits, generating invalid grade transitions, clipping into terrain surfaces, and throwing collision/overlap errors.
- **User Instruction**: Disable underground interchanges/mode for now to de-risk the MVP.

### Incident 3: Disconnected Host Road Attachments & Floating Nodes (Pic 2 & Pic 4)
- **Symptom**:
  - In Pic 2, one leg entrance/exit does not connect to the host highway, displaying "Road Disconnected" / broken path warning icons above the stub.
  - In Pic 4, even where connections technically form, the lane transitions are visually mangled: lines overlap diagonally, lane arrows abruptly terminate, and carriageway tapers buckle unnaturally across the attachment node.

### Incident 4: Bridge Elevation Profile Wonkiness & Excessive Overpass Height (Pic 3)
- **Symptom**:
  - In Pic 3, the bridge overpass is extremely wonky: it undulates up and down across piers like a rollercoaster instead of maintaining a level bridge deck.
  - The deck height reaches $12\text{m}–18\text{m}$ in mid-air, far exceeding necessary clearance.
  - The user has placed a reference bridge segment in Pic 3 confirming the in-game clearance minimum: exactly **$7.5\text{m}$**.
  - **Requirement**: Overpass clearance must default to **$7.5\text{m}$**, staying as low to the ground as possible while clearing the lower carriageway, with a flat deck over the crossing spans.

### Incident 5: Massive Footprint & Asymmetric Geometry Compared to Standard Trumpet (Pic 5)
- **Symptom**:
  - In Pic 5, looking from above, our generated trumpet interchange is disproportionately massive compared to the standard, compact, visually pleasing trumpet asset placed above it.
  - The loop ramp shoots out far into the distance, turning a simple $270^\circ$ loop into an enormous, sweeping balloon.
  - The flyover ramp diverges at a bizarre wide angle instead of following the natural curve of the highway.
  - **Requirement**: The default size must not be the maximum possible footprint; it should default to a balanced medium size (between minimum and maximum), visually compact and geometric, with a straightforward slider to scale the footprint radius in the studio menu.

---

## 2. Technical Summary of Required Changes

1. **Fix UI "length" Render Exception**:
   - Check array accesses in the engineering standards slider/input renderer (`InterchangeStudioPanel.tsx`) where undefined telemetry or leg collections are accessed without null checks.
2. **Deactivate Underground Toggle for MVP**:
   - Hide or disable `UndergroundMode` options in UI and set default to `UndergroundMode.None`.
3. **Overpass Elevation & Clearance Hard-Floor**:
   - Default clearance: **$7.5\text{m}$**.
   - Bridge decks spanning lower carriageways must remain flat at $+7.5\text{m}$ across crossing spans, with gentle AASHTO approach ramps ascending and descending at $\le 6\%–8\%$.
4. **Trumpet Generator Geometry Restructure**:
   - Recalculate loop center and radius based on compact footprint: $R_{\text{loop}} \approx 35\text{m}–45\text{m}$.
   - Position the $270^\circ$ loop directly adjacent to the crossing overpass instead of projecting it out into an oversized balloon.
   - Anchor the flyover ramp so it hugs the main highway corridor before curving gently over the mainline.
5. **Leg Portal Attachment Alignment**:
   - Ensure `CreationDefinition` uses correct `StartAttachEntity` / `EndAttachEntity` collinear handles so lane markings and carriageways connect flush without diagonal kinks.
