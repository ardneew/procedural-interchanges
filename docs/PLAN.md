# Principal Architect Blueprint: RLHF Studio 3D WebGL Viewport Repair & C#-to-JS Serialization Contract

**Author**: Principal Architect
**Status**: Ready for Execution (Stage 3 — Flash Execution Agent)
**Scope**: Deterministic repair of the Three.js 3D WebGL mode in the RLHF Studio (`SandboxServer.cs` and `SandboxHtmlGenerator.cs`), elimination of C#-to-JS JSON property mismatches, proper hidden-to-visible WebGL canvas lifecycle/resize handling, 3D extruded box-girder road decks with side-skirts/parapets/pillars/host-leg stubs, 4-leg angle sorting fix in `SandboxServer.cs`, and automated contract verification tests.

---

## 0. Root-Cause Diagnosis from First Principles

Inspection of [`SandboxServer.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Tests/ProceduralInterchanges.Lab/Server/SandboxServer.cs) and [`SandboxHtmlGenerator.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Tests/ProceduralInterchanges.Lab/Server/SandboxHtmlGenerator.cs) revealed **five compounding defects** that caused 3D mode (`Split 2D + 3D` and `3D Perspective`) to render an empty/broken scene:

### Defect 1: C#-to-JS JSON Property Name & Structure Mismatch (`SerializePlan3D` vs `renderPlan3D`)
1. **Top-Level Radius Key Mismatch**:
   - `SandboxServer.SerializePlan3D` (line 415) serialized `radius`, whereas `SandboxHtmlGenerator.renderPlan3D` (line 325) read `plan3D.footprintRadius`.
   - Because `plan3D.footprintRadius` evaluated to `undefined`, `new THREE.RingGeometry(plan3D.footprintRadius - 0.6, plan3D.footprintRadius + 0.6, 64)` received `NaN`, producing degenerate geometry.
2. **Course Sample Array Key & Element Type Mismatch**:
   - `SandboxServer.SerializePlan3D` (lines 382–398) serialized each course's 3D points under the key `points` as `float[]` arrays (`[x, y, z]`).
   - `SandboxHtmlGenerator.renderPlan3D` (line 343) read `const pts = course.samples;` and accessed `cur.x`, `cur.y`, `cur.z` (object properties).
   - Because `course.samples` was `undefined`, line 344 (`if (!pts || pts.length < 2) return;`) **immediately exited on every single course**, rendering zero roads in 3D!
3. **Missing Tier Color & Elevation Metadata**:
   - `SerializePlan3D` did not serialize `color` or `midY` on each course, nor did it map CS2's North axis ($+Z$) to Three.js's screen-up axis ($-Z$) to match the 2D SVG viewport (`SvgRenderer.cs` lines 56–65 where `ay = -course.Curve.a.z`).

### Defect 2: WebGL Initialization While Container Is `display: none` (`0x0` Viewport)
- On initial page load, `currentViewMode` is `'2D'`, so `#card3D` has `style="display:none;"`.
- When `fetchServerEvaluation()` completes (line 495), it calls `initThreeJs()` while `#card3D` is still hidden (`display: none`), where `container.clientWidth === 0` and `container.clientHeight === 0`.
- When the user clicks **`Split 2D + 3D`** or **`3D Perspective`**, `setViewMode(mode)` sets `card3D.style.display = 'flex'` and calls `onResize3D()` synchronously before browser layout reflow completes, and never re-invokes `initThreeJs()` or `renderPlan3D(lastPayload.plan3DA)` after layout settles.

### Defect 3: Stale Static `docs/lab-gallery/sandbox.html` File
- Whenever `SandboxHtmlGenerator.cs` is updated, `SandboxHtmlGenerator.WriteToFile("docs/lab-gallery/sandbox.html")` must be executed (via running the Lab generator) so that opening `docs/lab-gallery/sandbox.html` or serving `/` always reflects the latest HTML/JS bundle, and offline fallback mode must also synthesize a valid 3D scene if the server is not running.

### Defect 4: Flat Zero-Thickness Ribbons & Missing Host Highway Approaches in 3D
- Even with valid points, a zero-thickness 2D triangle strip at `y + 0.1` lacks 3D depth cues. Real 3D bridge visualization requires:
  - Extruded 3D road deck volume (top surface + vertical box-girder side skirts of thickness $d_{\text{deck}} = 0.85\text{m}$ + raised edge parapets/curbs + dashed white centerline stripe).
  - Outer host highway stub approaches outside `footprintRadius` (`[R_footprint, R_footprint + 50m]`) with `Leg 0..N` markers so the user can see how setback curves connect into the host corridors in 3D.

### Defect 5: Unwrapped 4-Leg Angle in `SandboxServer.cs`
- In `SandboxServer.cs` line 143: `angles = new[] { 0f, rad, math.PI, math.PI + rad };`. Since `math.PI + rad > math.PI`, `Array.Sort(angles)` places the South-West leg at index 3 instead of index 0 (`rad - math.PI`), violating the $(-\pi, \pi]$ counter-clockwise angle invariant.

---

## Phase 1: Backend C# Fixes in [`Tests/ProceduralInterchanges.Lab/Server/SandboxServer.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Tests/ProceduralInterchanges.Lab/Server/SandboxServer.cs)

### 1.1 Fix 4-Leg Angle Wrapping in `HandleRequest`
In `SandboxServer.cs` (lines 135–146), replace:
```csharp
                    float[] angles;
                    if (n == 3)
                    {
                        float stemAngle = -math.clamp(rad, math.radians(35f), math.radians(145f));
                        angles = new[] { stemAngle, 0f, math.PI };
                    }
                    else
                    {
                        angles = new[] { 0f, rad, math.PI, math.PI + rad };
                    }
                    Array.Sort(angles);
```
with:
```csharp
                    float[] angles;
                    if (n == 3)
                    {
                        float stemAngle = -math.clamp(rad, math.radians(35f), math.radians(145f));
                        angles = new[] { stemAngle, 0f, math.PI };
                    }
                    else
                    {
                        float clampedRad = math.clamp(rad, math.radians(35f), math.radians(145f));
                        angles = new[] { clampedRad - math.PI, 0f, clampedRad, math.PI };
                    }
                    Array.Sort(angles);
```

### 1.2 Replace `SerializePlan3D` with Exact Schema & Coordinate Alignment
In `SandboxServer.cs` (lines 371–422), replace `SerializePlan3D` with the following exact implementation:
- Maps CS2 $(x, y, z)$ to Three.js world space $(x,\; y,\; -z)$ so North ($+Z$ in CS2, $-Y$ in 2D SVG) points toward $-Z$ in Three.js, matching the 2D SVG plan orientation 1:1.
- Emits `footprintRadius`, `coreRadius`, `courses` (with `id`, `role`, `stackLevel`, `forceElevated`, `lanes`, `width`, `color`, `samples` as `{ x, y, z }` objects), and `legs` (with `legIndex`, `azimuthRad`, `portalCenter`, `inboundStart`, `inboundEnd`, `outboundStart`, `outboundEnd`).

```csharp
        private static string GetTierColorHex(float y, int stackLevel)
        {
            if (y < -1.0f) return "#06b6d4"; // Cyan (Tunnel / Depressed)
            if (y > 18.0f || stackLevel >= 3) return "#ec4899"; // Magenta (Tier 3)
            if (y > 10.0f || stackLevel == 2) return "#f59e0b"; // Amber (Tier 2)
            if (y > 3.5f || stackLevel == 1) return "#10b981";  // Emerald (Tier 1)
            return "#64748b"; // Slate (Ground)
        }

        public static object SerializePlan3D(InterchangePlan plan, List<HighwayLeg> legs, float radius)
        {
            var courses3D = new List<object>();
            if (plan?.Courses != null)
            {
                foreach (var c in plan.Courses)
                {
                    const int sampleCount = 24;
                    var samples = new List<object>(sampleCount + 1);
                    for (int s = 0; s <= sampleCount; s++)
                    {
                        float t = (float)s / sampleCount;
                        float3 pos = CubicBezier3D.Position(c.Curve, t);
                        // Map CS2 Z -> Three.js -Z so 3D camera matches 2D SVG orientation (North = Up/Back)
                        samples.Add(new { x = pos.x, y = pos.y, z = -pos.z });
                    }

                    float midY = CubicBezier3D.Position(c.Curve, 0.5f).y;
                    int lanes = c.Lanes > 0 ? c.Lanes : 2;
                    float width = math.max(5.0f, lanes * 3.5f + 1.5f);
                    string color = GetTierColorHex(midY, c.StackLevel);

                    courses3D.Add(new
                    {
                        id = c.Id ?? "course",
                        role = c.Role.ToString(),
                        stackLevel = c.StackLevel,
                        forceElevated = c.ForceElevated || c.StackLevel > 0 || midY > 3.5f,
                        lanes = lanes,
                        width = width,
                        color = color,
                        samples = samples
                    });
                }
            }

            var legs3D = new List<object>();
            if (legs != null)
            {
                foreach (var leg in legs)
                {
                    float3 pCenter = leg.PortalCenterPosition;
                    float3 outDir = math.lengthsq(leg.PortalInwardTangent) > 1e-4f
                        ? -math.normalize(leg.PortalInwardTangent)
                        : math.normalize(leg.LegAxisDirection);

                    float3 inEnd = leg.Inbound != null ? leg.Inbound.PortalPosition : pCenter;
                    float3 inStart = inEnd + outDir * 55f;

                    float3 outStart = leg.Outbound != null ? leg.Outbound.PortalPosition : pCenter;
                    float3 outEnd = outStart + outDir * 55f;

                    float3 labelPos = pCenter + outDir * 62f;

                    legs3D.Add(new
                    {
                        legIndex = leg.LegIndex,
                        azimuthRad = leg.AzimuthRad,
                        portalCenter = new { x = pCenter.x, y = pCenter.y, z = -pCenter.z },
                        labelPos = new { x = labelPos.x, y = labelPos.y + 4f, z = -labelPos.z },
                        inboundStart = new { x = inStart.x, y = inStart.y, z = -inStart.z },
                        inboundEnd = new { x = inEnd.x, y = inEnd.y, z = -inEnd.z },
                        outboundStart = new { x = outStart.x, y = outStart.y, z = -outStart.z },
                        outboundEnd = new { x = outEnd.x, y = outEnd.y, z = -outEnd.z }
                    });
                }
            }

            return new
            {
                footprintRadius = radius,
                coreRadius = radius * 0.62f,
                courses = courses3D,
                legs = legs3D
            };
        }
```

---

## Phase 2: Frontend Three.js Viewport & Lifecycle Repair in [`Tests/ProceduralInterchanges.Lab/Server/SandboxHtmlGenerator.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Tests/ProceduralInterchanges.Lab/Server/SandboxHtmlGenerator.cs)

### 2.1 Candidate Toggle & Camera Reset Controls in 3D Header
In `SandboxHtmlGenerator.cs` (lines 163–172), update `#card3D` so the user can also switch between viewing **Candidate A** and **Candidate B** in 3D and reset the camera:
```html
        <div id=""card3D"" class=""view-card"" style=""display:none; position:relative;"">
          <div class=""view-header"" style=""pointer-events:auto; display:flex; align-items:center; gap:10px;"">
            <span id=""header3DTitle"">3D Perspective (Candidate A)</span>
            <button onclick=""set3DCandidate('A')"" id=""btn3DCandA"" style=""padding:3px 8px; font-size:0.75rem; cursor:pointer; background:var(--accent); color:white; border:none; border-radius:4px;"">Cand A</button>
            <button onclick=""set3DCandidate('B')"" id=""btn3DCandB"" style=""padding:3px 8px; font-size:0.75rem; cursor:pointer; background:#242933; color:var(--text-muted); border:1px solid var(--border); border-radius:4px;"">Cand B</button>
            <button onclick=""reset3DCamera()"" style=""padding:3px 8px; font-size:0.75rem; cursor:pointer; background:#242933; color:var(--text); border:1px solid var(--border); border-radius:4px;"">Reset Cam</button>
          </div>
          <div id=""webglContainer""></div>
          <div class=""webgl-hud"">
            <div class=""webgl-hud-row""><span>Orbit / Tilt</span><span class=""webgl-hud-val"">Left-Drag</span></div>
            <div class=""webgl-hud-row""><span>Pan / Zoom</span><span class=""webgl-hud-val"">Right-Drag / Scroll</span></div>
            <div class=""webgl-hud-row""><span>Ground / Setback</span><span class=""webgl-hud-val"" style=""color:#64748b"">■ 0m</span></div>
            <div class=""webgl-hud-row""><span>Elevated Decks</span><span class=""webgl-hud-val"" style=""color:#10b981"">■ +6m </span><span class=""webgl-hud-val"" style=""color:#f59e0b"">■ +12m </span><span class=""webgl-hud-val"" style=""color:#ec4899"">■ +18m</span></div>
          </div>
        </div>
```

### 2.2 Fix `setViewMode`, `initThreeJs`, `onResize3D`, and `renderPlan3D`
Replace the JavaScript 3D section in `SandboxHtmlGenerator.cs` (lines 212–414 and 494–502) with the following deterministic implementation:

1. **Deferred Initialization & Reflow-Aware Resize**:
   - `setViewMode(mode)` sets `card3D.style.display = 'flex'`, then uses `requestAnimationFrame` to call `initThreeJs()`, `onResize3D()`, and `refreshActive3DScene()`, guaranteeing `container.clientWidth > 0` and `container.clientHeight > 0`.
   - Attach a `ResizeObserver` on `#webglContainer` inside `initThreeJs()` so any window resize or flex split-pane transition automatically updates `threeCamera.aspect` and `threeRenderer.setSize(width, height)`.
2. **3D Box-Girder Deck Extrusion (`buildRoadDeckMesh`)**:
   - For every course in `plan3D.courses`, accept `course.samples` (objects `{x, y, z}`) OR `course.points` (arrays `[x, y, z]`) defensively via helper `toVec3(p)`.
   - Build a full 3D extruded road deck with:
     - **Top Roadway Surface**: Width `w = course.width`, at elevation `y + 0.12`, colored with `course.color`.
     - **Concrete Box-Girder Side Skirts & Bottom Soffit**: Thickness `deckThickness = (course.forceElevated || course.stackLevel > 0) ? 0.9f : 0.25f`, colored `#1e293b` so elevated bridges have unmistakable 3D thickness and cast/receive visual separation over lower levels.
     - **Centerline Direction Line**: `THREE.Line` slightly above deck (`y + 0.20`) in `#f8fafc` (opacity `0.55`).
     - **Support Pillars**: Cylindrical concrete piers (`radiusTop = 1.4`, `radiusBottom = 1.65`) descending from the bottom soffit (`p.y - deckThickness`) to `y = 0` whenever `p.y > 4.2`.
3. **Host Highway Corridor Approaches**:
   - Render `plan3D.legs` (`inboundStart -> inboundEnd` and `outboundStart -> outboundEnd`) as dark-slate host highway approach decks outside the footprint ring so the user clearly sees how the setback transitions join the outer host highways.
4. **Offline Fallback 3D Generator (`buildOfflinePlan3D`)**:
   - If the HTML file is opened directly via `file://` without the server running, synthesize a valid `plan3D` object in JS and call `renderPlan3D` so 3D mode still works in standalone offline mode.

Exact JavaScript replacement for lines 212–414 in `SandboxHtmlGenerator.cs`:
```javascript
    let isServerLive = false;
    let lastPayload = null;
    let lastUiParams = null;
    let currentViewMode = '2D';
    let active3DCandidate = 'A';
    let threeRenderer, threeScene, threeCamera, threeControls, roadMeshGroup, resizeObserver3D;

    function set3DCandidate(cand) {
      active3DCandidate = cand;
      const btnA = document.getElementById('btn3DCandA');
      const btnB = document.getElementById('btn3DCandB');
      const title = document.getElementById('header3DTitle');
      if (btnA && btnB && title) {
        btnA.style.background = cand === 'A' ? 'var(--accent)' : '#242933';
        btnA.style.color = cand === 'A' ? 'white' : 'var(--text-muted)';
        btnB.style.background = cand === 'B' ? 'var(--success)' : '#242933';
        btnB.style.color = cand === 'B' ? 'white' : 'var(--text-muted)';
        title.innerText = `3D Perspective (Candidate ${cand})`;
      }
      refreshActive3DScene();
    }

    function reset3DCamera() {
      if (!threeCamera || !threeControls) return;
      threeCamera.position.set(0, 240, 310);
      threeControls.target.set(0, 0, 0);
      threeControls.update();
    }

    function setViewMode(mode) {
      currentViewMode = mode;
      document.getElementById('btnView2D').className = 'view-btn' + (mode === '2D' ? ' active' : '');
      document.getElementById('btnViewSplit').className = 'view-btn' + (mode === 'Split' ? ' active' : '');
      document.getElementById('btnView3D').className = 'view-btn' + (mode === '3D' ? ' active' : '');

      const cardA = document.getElementById('cardA');
      const cardB = document.getElementById('cardB');
      const card3D = document.getElementById('card3D');

      if (mode === '2D') {
        cardA.style.display = 'flex';
        cardB.style.display = 'flex';
        card3D.style.display = 'none';
      } else if (mode === 'Split') {
        cardA.style.display = 'flex';
        cardB.style.display = 'none';
        card3D.style.display = 'flex';
      } else if (mode === '3D') {
        cardA.style.display = 'none';
        cardB.style.display = 'none';
        card3D.style.display = 'flex';
      }

      if (mode === 'Split' || mode === '3D') {
        requestAnimationFrame(() => {
          initThreeJs();
          onResize3D();
          refreshActive3DScene();
        });
      }
    }

    function refreshActive3DScene() {
      if (!threeScene) return;
      if (lastPayload) {
        const plan = (active3DCandidate === 'B' && lastPayload.plan3DB) ? lastPayload.plan3DB : lastPayload.plan3DA;
        if (plan) {
          renderPlan3D(plan);
          return;
        }
      }
      if (lastUiParams) {
        renderPlan3D(buildOfflinePlan3D(lastUiParams));
      }
    }

    function initThreeJs() {
      if (typeof THREE === 'undefined') {
        console.error('Three.js library failed to load.');
        return;
      }
      const container = document.getElementById('webglContainer');
      if (!container || threeRenderer) return;

      const width = Math.max(container.clientWidth, 400);
      const height = Math.max(container.clientHeight, 350);

      threeScene = new THREE.Scene();
      threeScene.background = new THREE.Color(0x0b0d10);
      threeScene.fog = new THREE.FogExp2(0x0b0d10, 0.0009);

      threeCamera = new THREE.PerspectiveCamera(45, width / height, 1, 3000);
      threeCamera.position.set(0, 240, 310);
      threeCamera.lookAt(0, 0, 0);

      threeRenderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
      threeRenderer.setSize(width, height);
      threeRenderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
      container.innerHTML = '';
      container.appendChild(threeRenderer.domElement);

      if (typeof THREE.OrbitControls !== 'undefined') {
        threeControls = new THREE.OrbitControls(threeCamera, threeRenderer.domElement);
        threeControls.enableDamping = true;
        threeControls.dampingFactor = 0.07;
        threeControls.maxPolarAngle = Math.PI / 2 - 0.03;
        threeControls.minDistance = 25;
        threeControls.maxDistance = 1400;
      }

      const ambLight = new THREE.HemisphereLight(0xe2e8f0, 0x1e293b, 0.85);
      threeScene.add(ambLight);

      const dirLight = new THREE.DirectionalLight(0xffffff, 0.95);
      dirLight.position.set(180, 360, 220);
      threeScene.add(dirLight);

      const backLight = new THREE.DirectionalLight(0x93c5fd, 0.35);
      backLight.position.set(-200, 180, -180);
      threeScene.add(backLight);

      const grid = new THREE.GridHelper(900, 90, 0x2a303c, 0x161a22);
      grid.position.y = -0.15;
      threeScene.add(grid);

      roadMeshGroup = new THREE.Group();
      threeScene.add(roadMeshGroup);

      window.addEventListener('resize', onResize3D);
      if (typeof ResizeObserver !== 'undefined') {
        resizeObserver3D = new ResizeObserver(() => onResize3D());
        resizeObserver3D.observe(container);
      }
      animate3D();
    }

    function onResize3D() {
      if (!threeRenderer || !threeCamera) return;
      const container = document.getElementById('webglContainer');
      if (!container) return;
      const width = container.clientWidth;
      const height = container.clientHeight;
      if (width <= 0 || height <= 0) return;
      threeCamera.aspect = width / height;
      threeCamera.updateProjectionMatrix();
      threeRenderer.setSize(width, height);
    }

    function animate3D() {
      requestAnimationFrame(animate3D);
      if (threeControls) threeControls.update();
      if (threeRenderer && threeScene && threeCamera) {
        threeRenderer.render(threeScene, threeCamera);
      }
    }

    function toVec3(pt) {
      if (!pt) return new THREE.Vector3(0, 0, 0);
      if (Array.isArray(pt)) {
        return new THREE.Vector3(pt[0] || 0, pt[1] || 0, -(pt[2] || 0));
      }
      return new THREE.Vector3(pt.x || 0, pt.y || 0, pt.z || 0);
    }

    function addExtrudedRibbon(pts, width, colorHex, deckThickness, isElevated) {
      if (!pts || pts.length < 2) return;
      const hw = width * 0.5;
      const numSegs = pts.length - 1;

      const topPositions = [];
      const topIndices = [];
      const skirtPositions = [];
      const skirtIndices = [];
      const centerLinePts = [];

      const leftTop = [];
      const rightTop = [];
      const leftBot = [];
      const rightBot = [];

      for (let i = 0; i < pts.length; i++) {
        const cur = pts[i];
        let fwd;
        if (i === 0) {
          fwd = new THREE.Vector3(pts[1].x - cur.x, 0, pts[1].z - cur.z);
        } else if (i === pts.length - 1) {
          fwd = new THREE.Vector3(cur.x - pts[i - 1].x, 0, cur.z - pts[i - 1].z);
        } else {
          fwd = new THREE.Vector3(pts[i + 1].x - pts[i - 1].x, 0, pts[i + 1].z - pts[i - 1].z);
        }
        if (fwd.lengthSq() < 1e-6) fwd.set(1, 0, 0);
        else fwd.normalize();

        const norm = new THREE.Vector3(-fwd.z, 0, fwd.x);
        const yTop = cur.y + 0.15;
        const yBot = cur.y + 0.15 - deckThickness;

        const pLT = new THREE.Vector3(cur.x + norm.x * hw, yTop, cur.z + norm.z * hw);
        const pRT = new THREE.Vector3(cur.x - norm.x * hw, yTop, cur.z - norm.z * hw);
        const pLB = new THREE.Vector3(cur.x + norm.x * hw, yBot, cur.z + norm.z * hw);
        const pRB = new THREE.Vector3(cur.x - norm.x * hw, yBot, cur.z - norm.z * hw);

        leftTop.push(pLT);
        rightTop.push(pRT);
        leftBot.push(pLB);
        rightBot.push(pRB);

        topPositions.push(pLT.x, pLT.y, pLT.z, pRT.x, pRT.y, pRT.z);
        centerLinePts.push(new THREE.Vector3(cur.x, yTop + 0.08, cur.z));

        if (i < numSegs) {
          const v0 = i * 2;
          const v1 = i * 2 + 1;
          const v2 = (i + 1) * 2;
          const v3 = (i + 1) * 2 + 1;
          topIndices.push(v0, v1, v2, v1, v3, v2);
        }
      }

      const topGeom = new THREE.BufferGeometry();
      topGeom.setAttribute('position', new THREE.Float32BufferAttribute(topPositions, 3));
      topGeom.setIndex(topIndices);
      topGeom.computeVertexNormals();

      const roadMat = new THREE.MeshStandardMaterial({
        color: colorHex,
        roughness: 0.55,
        metalness: 0.12,
        side: THREE.DoubleSide
      });
      roadMeshGroup.add(new THREE.Mesh(topGeom, roadMat));

      // Side skirts and bottom soffit
      for (let i = 0; i < numSegs; i++) {
        const base = skirtPositions.length / 3;
        const l0t = leftTop[i], l1t = leftTop[i + 1], l0b = leftBot[i], l1b = leftBot[i + 1];
        const r0t = rightTop[i], r1t = rightTop[i + 1], r0b = rightBot[i], r1b = rightBot[i + 1];

        skirtPositions.push(
          l0t.x, l0t.y, l0t.z, l0b.x, l0b.y, l0b.z, l1t.x, l1t.y, l1t.z, l1b.x, l1b.y, l1b.z,
          r0t.x, r0t.y, r0t.z, r1t.x, r1t.y, r1t.z, r0b.x, r0b.y, r0b.z, r1b.x, r1b.y, r1b.z
        );
        skirtIndices.push(
          base + 0, base + 1, base + 2, base + 1, base + 3, base + 2,
          base + 4, base + 5, base + 6, base + 5, base + 7, base + 6
        );
      }

      const skirtGeom = new THREE.BufferGeometry();
      skirtGeom.setAttribute('position', new THREE.Float32BufferAttribute(skirtPositions, 3));
      skirtGeom.setIndex(skirtIndices);
      skirtGeom.computeVertexNormals();
      const skirtMat = new THREE.MeshStandardMaterial({
        color: isElevated ? 0x334155 : 0x1e293b,
        roughness: 0.85,
        metalness: 0.1,
        side: THREE.DoubleSide
      });
      roadMeshGroup.add(new THREE.Mesh(skirtGeom, skirtMat));

      // Centerline stripe
      const lineGeom = new THREE.BufferGeometry().setFromPoints(centerLinePts);
      const lineMat = new THREE.LineBasicMaterial({ color: 0xf8fafc, transparent: true, opacity: 0.55 });
      roadMeshGroup.add(new THREE.Line(lineGeom, lineMat));
    }

    function renderPlan3D(plan3D) {
      if (!threeScene || !roadMeshGroup || !plan3D || !plan3D.courses) return;

      while (roadMeshGroup.children.length > 0) {
        const obj = roadMeshGroup.children[0];
        if (obj.geometry) obj.geometry.dispose();
        if (obj.material) {
          if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose());
          else obj.material.dispose();
        }
        roadMeshGroup.remove(obj);
      }

      const footR = plan3D.footprintRadius || plan3D.radius || 220;
      const coreR = plan3D.coreRadius || (footR * 0.62);

      // 1. Concentric Boundary Rings (Setback Core and Footprint)
      const footprintRingGeom = new THREE.RingGeometry(footR - 0.8, footR + 0.8, 96);
      footprintRingGeom.rotateX(-Math.PI / 2);
      const footprintRingMat = new THREE.MeshBasicMaterial({ color: 0x475569, side: THREE.DoubleSide });
      const footprintMesh = new THREE.Mesh(footprintRingGeom, footprintRingMat);
      footprintMesh.position.y = 0.04;
      roadMeshGroup.add(footprintMesh);

      const coreRingGeom = new THREE.RingGeometry(coreR - 0.7, coreR + 0.7, 96);
      coreRingGeom.rotateX(-Math.PI / 2);
      const coreRingMat = new THREE.MeshBasicMaterial({ color: 0x3b82f6, opacity: 0.65, transparent: true, side: THREE.DoubleSide });
      const coreMesh = new THREE.Mesh(coreRingGeom, coreRingMat);
      coreMesh.position.y = 0.05;
      roadMeshGroup.add(coreMesh);

      // 2. Host Highway Corridor Stubs outside Footprint
      if (plan3D.legs && Array.isArray(plan3D.legs)) {
        plan3D.legs.forEach(leg => {
          if (leg.inboundStart && leg.inboundEnd) {
            addExtrudedRibbon([toVec3(leg.inboundStart), toVec3(leg.inboundEnd)], 8.5, 0x475569, 0.3, false);
          }
          if (leg.outboundStart && leg.outboundEnd) {
            addExtrudedRibbon([toVec3(leg.outboundStart), toVec3(leg.outboundEnd)], 8.5, 0x475569, 0.3, false);
          }
        });
      }

      // 3. Extrude Road Ribbons & Bridge Pillars
      const pillarMat = new THREE.MeshStandardMaterial({ color: 0x64748b, roughness: 0.85, metalness: 0.15 });

      plan3D.courses.forEach(course => {
        const rawPts = course.samples || course.points;
        if (!rawPts || rawPts.length < 2) return;
        const pts = rawPts.map(toVec3);

        const width = course.width || 8.0;
        const colorStr = course.color || (course.stackLevel >= 2 ? '#f59e0b' : (course.stackLevel === 1 ? '#10b981' : '#64748b'));
        const colorHex = parseInt(colorStr.replace('#', '0x'), 16);
        const isElev = Boolean(course.forceElevated || course.stackLevel > 0);
        const deckThickness = isElev ? 0.95 : 0.30;

        addExtrudedRibbon(pts, width, colorHex, deckThickness, isElev);

        // Support Pillars
        const pillarStep = Math.max(4, Math.floor(pts.length / 3));
        for (let pIdx = pillarStep; pIdx < pts.length - 2; pIdx += pillarStep) {
          const p = pts[pIdx];
          if (p.y > 4.2) {
            const pHeight = Math.max(1.0, p.y - deckThickness * 0.5);
            const pillarGeom = new THREE.CylinderGeometry(1.3, 1.6, pHeight, 16);
            const pillarMesh = new THREE.Mesh(pillarGeom, pillarMat);
            pillarMesh.position.set(p.x, pHeight * 0.5, p.z);
            roadMeshGroup.add(pillarMesh);
          }
        }
      });
    }

    function buildOfflinePlan3D(payload) {
      const r = payload.footprintRadius || 220;
      const coreR = r * 0.62;
      const rad = (payload.skewDeg || 90) * Math.PI / 180;
      const is3Way = (payload.archetype === 'Trumpet' || payload.archetype === 'DirectionalT' || payload.archetype === 'YStack');
      const angles = is3Way ? [-rad, 0, Math.PI] : [rad - Math.PI, 0, rad, Math.PI];
      const courses = [];
      angles.forEach((a, idx) => {
        const samples = [];
        for (let s = 0; s <= 16; s++) {
          const t = s / 16;
          const dist = r * (1 - 0.85 * t);
          samples.push({ x: dist * Math.cos(a), y: idx === 0 ? 8.0 * Math.sin(Math.PI * t) : 0, z: -dist * Math.sin(a) });
        }
        courses.push({ id: 'offline_' + idx, width: 9.0, stackLevel: idx === 0 ? 1 : 0, forceElevated: idx === 0, color: idx === 0 ? '#10b981' : '#64748b', samples });
      });
      return { footprintRadius: r, coreRadius: coreR, courses, legs: [] };
    }
```

### 2.3 Update `update()` and `fetchServerEvaluation()` in `SandboxHtmlGenerator.cs`
In `update()` (line 461):
```javascript
      lastUiParams = payload;
      if (isServerLive) {
        fetchServerEvaluation(payload);
      } else {
        renderOfflineSvg(payload);
        if (currentViewMode === 'Split' || currentViewMode === '3D') {
          initThreeJs();
          onResize3D();
          renderPlan3D(buildOfflinePlan3D(payload));
        }
      }
```
And in `fetchServerEvaluation(payload)` (lines 494–497):
```javascript
          if (currentViewMode === 'Split' || currentViewMode === '3D') {
            initThreeJs();
            onResize3D();
            refreshActive3DScene();
          }
```

---

## Phase 3: Regenerate `docs/lab-gallery/sandbox.html` & Add Contract Unit Test

### 3.1 Regenerate Static HTML Bundle
Run:
```powershell
dotnet run --project Tests/ProceduralInterchanges.Lab/ProceduralInterchanges.Lab.csproj
```
Confirm that `docs/lab-gallery/sandbox.html` is overwritten with the updated `SandboxHtmlGenerator.GenerateHtml()` output.

### 3.2 Add 3D Serialization & HTML Contract Test in [`Tests/ProceduralInterchanges.Tests/Templates/RlTemplateAdaptationTests.cs`](file:///c:/Users/liand/Documents/antigravity/procedural-interchanges/Tests/ProceduralInterchanges.Tests/Templates/RlTemplateAdaptationTests.cs)
Wait: `SandboxServer` is in `ProceduralInterchanges.Lab`, while `RlTemplateAdaptationTests` is in `ProceduralInterchanges.Tests`.
Check if `ProceduralInterchanges.Tests.csproj` references `ProceduralInterchanges.Lab.csproj` or keep the test in `ProceduralInterchanges.Tests` or verify via CLI smoke test:
- Run `dotnet build Tests/ProceduralInterchanges.Lab/ProceduralInterchanges.Lab.csproj` (must succeed with 0 errors).
- Run `dotnet test Tests/ProceduralInterchanges.Tests/ProceduralInterchanges.Tests.csproj` (all 63 tests must pass).
- Start the Sandbox server and invoke `POST http://localhost:5050/api/evaluate` via `Invoke-RestMethod`, asserting that:
  1. `response.plan3DA.footprintRadius` is present and equals `footprintRadius`.
  2. `response.plan3DA.coreRadius` is present and $> 0$.
  3. `response.plan3DA.courses` is non-empty, and every course has `.samples` (with `.x`, `.y`, `.z` properties) and `.color` starting with `#`.
  4. `response.plan3DA.legs` has the expected leg count (`3` for 3-way, `4` for 4-way).
