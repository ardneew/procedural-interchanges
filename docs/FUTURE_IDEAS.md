# Procedural Interchanges — Feature Ideas & Future Roadmap

This document catalogs advanced design concepts, ecosystem inspirations (especially from Placemaker), and planned features to be analyzed and prioritized by the Principal Architect.

---

## 1. Obstacle, Building & Network Collision Avoidance
* **Current Status**: The current implementation does **not** perform obstacle queries against existing zoned buildings, props, or nearby uninvolved road networks. It relies on CS2's native `ValidationSystem` / `OverrideSystem` or user clearing.
* **Future Capability**:
  - **Spatial Bounding Query**: Query `Game.Buildings.Building` and `Game.Net.Edge` within the interchange envelope using native spatial indexing (`SearchSystem` / `QuadTree`).
  - **Dynamic Clearance / Retargeting**:
    - Automatically nudge ramp control points ($P_1, P_2$) away from existing high-density buildings or water bodies.
    - Warn the user or highlight encroached structures in red during the hover preview stage.
  - **Auto-Bulldoze / Demolition Option**: Allow the user to toggle whether colliding structures are automatically condemned/refunded upon placement.

---

## 2. Placemaker-Inspired Automatic Interchange Recommendation
* **Concept**: Instead of forcing the player to manually guess which interchange belongs in an area, the mod analyzes the selected junction context and suggests the optimal archetype.
* **Contextual Heuristics**:
  - **Freeway-to-Local / Neighborhood Ingress**:
    - Detects a high-capacity freeway meeting a low-speed 2-lane or 4-lane collector/arterial.
    - Recommends: **Compact Diamond**, **SPUI** (Single-Point Urban Interchange), or **Folded Diamond** to preserve neighborhood zoning space.
  - **Ring Road / Orbital Highway Transitions**:
    - Detects circumferential arterial or bypass corridors.
    - Recommends: **Roundabout Interchange** or **Parclo (Partial Cloverleaf)** to prioritize continuous arterial flow without cross-traffic left-turn conflicts.
  - **System-to-System (Freeway-to-Freeway)**:
    - Detects two intersecting multi-lane highways.
    - Recommends: **Directional Y-Stack**, **Trumpet** (for T-terminations), or **Turbine/Stack** for high-volume free-flow.
  - **Topographical Trigger**:
    - If steep slopes ($\ge 10\%$) are detected adjacent to the corridor, automatically disqualifies wide-footprint Cloverleafs and recommends an asymmetric contour-hugging **Trumpet** or tight **Diamond**.

---

## 3. Asymmetric Directional Priority & Traffic Weighting
* **The Problem**: Real-world interchanges rarely have equal traffic demand on all four quadrants. Often 70% of morning commuters want to take the westbound-to-northbound off-ramp, while other turns see minimal traffic.
* **Future Capability**:
  - **Directional Lane Allocation**:
    - Allow the player (or automated traffic analysis) to designate a "Primary Movement" (e.g. Westbound Freeway $\rightarrow$ Northbound City Center).
    - Give the primary movement a **2-lane high-speed directional flyover** ($R \ge 120\text{m}$) while subordinate movements use a tight 1-lane loop ramp ($R \approx 45\text{m}$).
  - **Mainline Priority Biasing**:
    - Maintain higher mainline design speeds and uninterrupted through-lanes on the prioritized highway, while adding dedicated deceleration/auxiliary lanes to the secondary corridor.

---

## 4. Placemaker-Style Procedural Landscaping & Aesthetics
* **Concept**: An interchange should not just be bare asphalt on dirt—it should look like a complete, finished public works project.
* **Automated Embellishments**:
  - **Slope & Retention Wall Dressing**: Automatically paint grass, gravel, or decorative concrete textures on the earthwork embankments between ramps.
  - **Guardrails & Sound Barriers**: Procedurally generate highway sound barrier fences along ramps abutting residential zones.
  - **Flora & Tree Infill**: Populate empty interchange infields (the open spaces inside loops and ramps) with natural forest clusters, wild shrubs, or manicured park grass, matching the local biome.
  - **Highway Signage & Lighting**: Place overhead direction gantries, warning arrows on sharp loop curves, and high-mast floodlights at merge forks.

---

## 5. Fully Custom Parametric Interchanges
* **Concept**: Beyond fixed archetype templates, provide a node-and-wire graph builder.
* **Capabilities**:
  - Drag-and-drop Bézier ramp links between any two arbitrary highway points.
  - Parametric slider controls for individual ramp banking, radius easing, and clearance heights.
  - Save custom player-built interchanges as reusable parametric presets that can adapt to new terrain elsewhere in the city.
