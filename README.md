# Procedural Interchanges

**Procedural Interchanges** is a code mod for **Cities: Skylines II** that procedurally generates highway interchanges and integrates them into existing road networks in real time.

---

## Features

- **Procedural Archetype Generation**:
  - **Trumpet**: 3-way freeway junction using 270° loops and directional overpasses.
  - **Directional-T**: High-speed 3-way multi-level flyovers.
  - **3-Level Y-Stack**: Fully directional 3-level interchange for Y-angle highways.
  - **Diamond**: Standard highway-to-arterial service interchange with diagonal ramps.
  - **Parclo A4**: Partial cloverleaf with loop ramps in opposite quadrants.
  - **Cloverleaf**: 4-way freeway interchange with collector-distributor (C-D) weaving isolation.
  - **4-Level Stack**: High-speed directional flyovers across 4 grade-separated levels.
  - **Turbine**: Sweeping orbital left-turn ramps around a central vortex.
  - **Grade-Separated Roundabout**: 3 to 6-leg rotary junction with elevated circulatory ring and mainline bypass.
- **Topography & Terrain Conformance**:
  - Adapts to uneven terrain using native height sampling and Hermite vertical profile transitions.
  - Enforces CS2 AASHTO standards: maximum slopes ($\le 18\%$, typically $4\%\dots 6\%$), bridge deck vertical clearances ($\ge 9.5\text{ m}$), and natural viaduct pier span spacing ($\sim 38\text{ m}$).
- **Network Splicing & Continuity**:
  - $G^1$ collinear tangent splicing at boundary cuts with existing highways at any skew angle.
  - Supports both separated dual-carriageway highways and single two-way trunk roads with smooth bifurcation flares.
  - Automatic through-corridor edge trimming and restoration.
- **Interactive Studio UI**:
  - Custom React / Coherent Gameface user interface.
  - 3-step workflow: **1. Pick Center**, **2. Select Connected Highways**, **3. Design & Tune**.
  - Dynamic footprint circle overlay with `[` / `]` hotkeys, `Alt` + scroll-wheel resizing, and one-click **"Auto-Fit Size"**.
  - Real-time 2D CAD plan visualizer, vertical elevation profile chart, and engineering telemetry.
  - Full undo / redo support with `Ctrl+Z` and persistent city receipts.

---

## Installation & Usage

### Keybindings
- **`Ctrl + Shift + I`**: Open / Close Interchange Studio.
- **`Ctrl + Z`**: Undo last built interchange and restore original highways.
- **`[` / `]`**: Decrease / Increase footprint circle radius by $15\text{ m}$.
- **`Alt + Mouse Scroll`**: Resize footprint circle dynamically.
- **`Enter`**: Proceed to next step or build the interchange.
- **`Right-Click`**: Step back / cancel.

### Development Build
1. Build the React frontend:
   ```bash
   cd ui
   npm install
   npm run build
   ```
2. Build the C# assembly:
   ```bash
   dotnet build ProceduralInterchanges.csproj -c Release
   ```
The mod outputs directly to `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Mods\ProceduralInterchanges`.

---

## Version History

### v0.0.3
- **Thread-Safe UI Actions & Undo Engine**: Marshaled all UI triggers into a main-thread action queue; fixed crash when pressing GUI Undo.
- **Doubled Highways Eliminated**: Fixed ghost entity double-commit state machine and through-corridor multi-cut trimming.
- **Collinear $G^1$ Boundary Splicing**: Fixed outbound flare tangent sign inversion and highway approach kinks at arbitrary skew angles.
- **Hollow Circle & Top-Level Size Controls**: Projected hollow outline overlay, increased default radius to $260\text{ m}$, unbounded highway selection, and added top-level size slider + Auto-Fit button.
- **Smooth Overpass Ramps & Pier Spacing**: Solved end-to-end ramp chain elevation profiles ($< 7\%$ slope, $\ge 9.5\text{ m}$ clearance) and increased bridge pier spacing to natural $38\text{ m}$ spans.
