# Procedural Interchanges

A procedural, terrain-adaptive highway interchange generator mod for **Cities: Skylines II**.

## Overview

**Procedural Interchanges** is an engine-level mod for Cities: Skylines II designed to generate parametric, mathematically sound, and geometrically clean road interchanges directly in-game. 

Instead of forcing players to spend hours manually placing ramps, fighting terrain cliffing, and fine-tuning Bézier control handles, this mod allows the player to select existing road corridors, pick an interchange archetype, and procedurally generate a fully connected, terrain-adaptive junction adhering to real-world civil engineering tolerances.

---

## In-Progress Design & Architecture

### Core Pipeline
1. **Corridor Vector Snapping (UX & Tool)**:
   - Detects incoming/terminating highway edges and nodes.
   - Extracts tangent vectors, approach bearings, skew angles, and lane counts.
2. **3D Spline & Clearance Solver**:
   - **Horizontal Alignment**: Uses clothoid-approximated Bézier transition curves for smooth continuous curvature ($\kappa$) instead of abrupt radius snapping.
   - **Terrain Sampling**: Samples native heightfields via `TerrainUtils.SampleHeight(ref TerrainHeightData, float3)` to adapt grade profiles to local topography.
   - **Vertical Alignment**: Implements parabolic vertical curves (crest and sag) enforcing maximum highway gradients ($\le 4\text{–}6\%$) and vertical clearance envelopes ($\ge 8.0\text{–}9.5\text{m}$).
   - **Pillar Clearance**: Calculates overpass span lengths and pier placement zones to prevent bridge pillars from dropping into lower carriageways.
3. **Interactive Visual Preview**:
   - Renders tool overlay guides and ghost paths using the tool overlay system prior to committing changes to the simulation.
   - Exposes parametric adjustments (elevation swap, ramp radii, scale, road prefabs).
4. **ECS Graph Generation & Lane Math**:
   - Sequences edge splits to prevent entity ID invalidation.
   - Upgrades/downgrades mainline segments across diverge and merge points to maintain proper **Lane Balance** ($N \rightarrow (N-1) + 1 \rightarrow N$).
   - Emits native `CreationDefinition` and `NetCourse` entities processed through `CourseSplitSystem` and `NetToolSystem`.

---

## Technical Feasibility & Engineering Invariants

Based on technical evaluations and ground-truth code patterns from references (`Placemaker`, `ParkingLotTool`, and vanilla `GridRoadGenerator`):

* **Direct NetCourse Generation**: Road generation bypasses manual GameObject manipulation. Entities are defined with `CreationDefinition`, `NetCourse`, `CoursePos`, and `Updated` tags.
* **Support Pillar Avoidance**: Bridge spans must be explicitly partitioned so node positions and segment lengths avoid spawning piers within lower road bounding boxes.
* **Sequential Edge Splitting**: Attaching multiple ramps to an existing highway splits the edge into new entities. Splits must be cleanly sequenced across frame barriers to prevent targeting invalidated entities.
* **Native Compatibility**: Generates native ECS nodes and edges that remain 100% editable post-placement using tools like *Move It* and *Traffic*.

---

## Development Roadmap

### Phase 1: Core Foundation & Tool UX
- [ ] Implement `InterchangeToolSystem` extending CS2 tool bases.
- [ ] Implement corridor selection and snap-detection on existing road networks.
- [ ] Heightfield sampling pipeline using `TerrainHeightData`.
- [ ] Wireframe/overlay rendering for real-time interchange geometry preview.

### Phase 2: Tier 1 Archetypes (Service Interchanges)
- [ ] **Diamond Interchange**:
  - 4 slip ramps connecting freeway to surface arterial.
  - Grade-separated crossroad (overpass or underpass toggle).
  - Basic terrain adaptation and retaining wall profile.
- [ ] **Partial Cloverleaf (Parclo A2/B2)**:
  - 2 loop ramps and 2 slip ramps.
  - Loop radius adjustment based on terrain contours.

### Phase 3: Tier 2 Archetypes (3-Way System Interchanges)
- [ ] **Trumpet Interchange**:
  - High-speed directional flyover and directional ramp.
  - Multi-level vertical clearance calculation over mainline.
  - Clothoid spiral curve fitting for the semi-direct loop ramp ($R \ge 45\text{m}$).
- [ ] **Y-Stack (Directional T-Junction)**.

### Phase 4: Lane Mathematics & Mainline Integration
- [ ] Automated mainline lane-swapping between off-ramps and on-ramps.
- [ ] Auxiliary lane generation and smooth taper merges.
- [ ] Conflict-free pathfinding lane connections.

### Phase 5: Tier 3 & 4 Complex Interchanges
- [ ] **Single-Point Urban Interchange (SPUI)** (converging turn radii under focal bridge).
- [ ] **Full Cloverleaf** (with collector-distributor roads to resolve weaving).
- [ ] **Four-Level Stack & Turbine** (multi-tier clearance envelopes).

---

## Project Structure

```
procedural-interchanges/
├── Mod.cs                       # Mod entry point implementing IMod
├── Setting.cs                   # In-game settings and keybindings
├── ProceduralInterchanges.csproj # C# project definition importing CS2 Mod toolchain
├── Properties/
│   ├── PublishConfiguration.xml # Paradox Mods metadata
│   └── PublishProfiles/         # Deployment & publishing profiles
└── ui/                          # Coherent Gameface / React UI module
    ├── src/                     # TypeScript / TSX components
    ├── mod.json                 # UI module manifest (ID: ProceduralInterchanges)
    ├── package.json
    └── webpack.config.js
```

---

## Local Ground Truth & References

Development is informed by:
- **Reference Mod Corpus**: `C:\Users\liand\.cs2-modding\reference-mods\`
  - `ParkingLotTool`: Network overlay rendering, custom tool state machines, and object definitions.
  - `placemaker`: CreationDefinition/NetCourse emission, terrain heightfield sampling, and test suites.
- **Decompiled Source**: Ground truth game simulation systems verified against CS2 `1.6.2f1`.

---

## Development & Building

### Prerequisites
- Cities: Skylines II (version 1.6.2f1+)
- CS2 Modding Toolchain (installed via *Options → Modding* in-game)
- .NET 8 SDK
- Node.js 18+ and npm

### Building the Mod

1. **Build the C# assembly**:
   ```powershell
   dotnet build
   ```
   Compiles the mod and deploys the `.dll` and assets to `%CSII_LOCALMODSPATH%\ProceduralInterchanges`.

2. **Build the UI module**:
   ```powershell
   cd ui
   npm install
   npm run build
   ```
   Bundles the React frontend and deploys it to `%CSII_USERDATAPATH%\Mods\ProceduralInterchanges`.

---

## License

MIT License.
