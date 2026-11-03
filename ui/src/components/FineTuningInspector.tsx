import React from "react";
import { InterchangeUIState } from "../types/interchange-types";
import {
  setGlobalParam,
  setGlobalPrefab,
  setGlobalUpgrade,
  setLegOverride,
  setCourseOverride,
} from "../bindings/interchange-bindings";
import { CustomDropdown } from "./CustomDropdown";
import styles from "./studio.module.scss";

interface FineTuningInspectorProps {
  state: InterchangeUIState;
}

export const FineTuningInspector: React.FC<FineTuningInspectorProps> = ({ state }) => {
  const courses2D = Array.isArray(state?.courses2D) ? state.courses2D : [];
  const legs = Array.isArray(state?.legs) ? state.legs : [];
  const availablePrefabs = Array.isArray(state?.availablePrefabs) ? state.availablePrefabs : [];
  const selectedCourse = courses2D.find((c) => c.id === state.selectedCourseId);

  const laneOptions = [1, 2, 3, 4, 5].map((l) => ({
    value: l,
    label: `${l} ${l === 1 ? "Lane" : "Lanes"}`,
  }));

  const prefabOptions = [
    { value: "", label: "Auto-Detect" },
    ...availablePrefabs.map((p) => ({ value: p, label: p })),
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "12rem" }}>
      {/* Geometry Standards Sliders */}
      <div className={styles.section}>
        <div className={styles.sectionTitle}>
          <span>Engineering Standards</span>
          <span style={{ fontSize: "10rem", color: "#38bdf8" }}>AASHTO Compliant</span>
        </div>

        {/* Footprint Radius */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Intersection Size</span>
          <input
            type="range"
            min="120"
            max="500"
            step="10"
            value={state.footprintRadius ?? 260}
            onChange={(e) => setGlobalParam("radius", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.footprintRadius ?? 260}m</span>
        </div>

        {/* Rotation Skew */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Rotation Skew</span>
          <input
            type="range"
            min="-180"
            max="180"
            step="1"
            value={state.rotationDeg ?? 0}
            onChange={(e) => setGlobalParam("rotation", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{(state.rotationDeg ?? 0).toFixed(0)}°</span>
        </div>

        {/* Max Mainline Grade */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Max Mainline Slope</span>
          <input
            type="range"
            min="2.0"
            max="8.0"
            step="0.5"
            value={state.maxMainlineGrade ?? 4.0}
            onChange={(e) => setGlobalParam("maxMainlineGrade", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{(state.maxMainlineGrade ?? 4.0).toFixed(1)}%</span>
        </div>

        {/* Max Ramp Grade */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Max Ramp Slope</span>
          <input
            type="range"
            min="3.0"
            max="10.0"
            step="0.5"
            value={state.maxRampGrade ?? 6.0}
            onChange={(e) => setGlobalParam("maxRampGrade", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{(state.maxRampGrade ?? 6.0).toFixed(1)}%</span>
        </div>

        {/* Min Bridge Clearance */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Overpass Clearance</span>
          <input
            type="range"
            min="8.0"
            max="15.0"
            step="0.5"
            value={state.minClearance ?? 9.5}
            onChange={(e) => setGlobalParam("minClearance", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{(state.minClearance ?? 9.5).toFixed(1)}m</span>
        </div>

        {/* Design Speed */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Design Speed</span>
          <input
            type="range"
            min="60"
            max="130"
            step="10"
            value={state.designSpeed ?? 100}
            onChange={(e) => setGlobalParam("designSpeed", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.designSpeed ?? 100} km/h</span>
        </div>

        {/* Grade Separation & Overpass Toggles */}
        <div className={styles.checkboxGrid} style={{ marginTop: "8rem" }}>
          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              checked={state.gradeSeparateMainline ?? true}
              onChange={(e) => setGlobalParam("gradeSeparateMainline", e.target.checked ? 1 : 0)}
            />
            <span>Grade-Separate Mainline</span>
          </label>
          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              checked={state.mainlineOverpass ?? false}
              onChange={(e) => setGlobalParam("mainlineOverpass", e.target.checked ? 1 : 0)}
            />
            <span>Mainline Overpass</span>
          </label>
        </div>
      </div>

      {/* Selected Segment Override (when clicked on 2D plan) */}
      {selectedCourse && (
        <div className={styles.section} style={{ border: "1rem solid #00e5ff" }}>
          <div className={styles.sectionTitle}>
            <span>Selected Segment: {selectedCourse.roleName}</span>
            <span style={{ fontSize: "10rem", color: "#38bdf8" }}>{selectedCourse.id}</span>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Lanes</span>
            <div style={{ flex: 1 }}>
              <CustomDropdown
                value={selectedCourse.lanes}
                options={laneOptions}
                onChange={(val) =>
                  setCourseOverride(
                    selectedCourse.id,
                    parseInt(val, 10),
                    selectedCourse.prefabName || "",
                    0
                  )
                }
              />
            </div>
          </div>

          {availablePrefabs && availablePrefabs.length > 0 && (
            <div className={styles.controlRow}>
              <span className={styles.label}>Road Prefab</span>
              <div style={{ flex: 1 }}>
                <CustomDropdown
                  value={selectedCourse.prefabName || ""}
                  options={prefabOptions}
                  onChange={(val) =>
                    setCourseOverride(
                      selectedCourse.id,
                      selectedCourse.lanes,
                      String(val),
                      0
                    )
                  }
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* Per-Leg Overrides */}
      {legs && legs.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <span>Connected Highway Legs</span>
            <span style={{ fontSize: "10rem", color: "#64748b" }}>{legs.length} Portals</span>
          </div>

          {legs.map((leg) => {
            const legLaneOptions = [
              { value: 0, label: `Auto Lanes (${leg.inboundLanes}L)` },
              ...laneOptions,
            ];
            const legPrefabOptions = [
              { value: "", label: "Auto Prefab" },
              ...availablePrefabs.map((p) => ({ value: p, label: p })),
            ];

            return (
              <div
                key={`leg-card-${leg.index}`}
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "4rem",
                  padding: "6rem",
                  background: "rgba(0,0,0,0.25)",
                  borderRadius: "4rem",
                  marginBottom: "6rem",
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "11rem" }}>
                  <span style={{ fontWeight: 600, color: "#38bdf8" }}>
                    Leg #{leg.index + 1} ({leg.azimuthDeg.toFixed(0)}°)
                  </span>
                  <span style={{ color: "#94a3b8" }}>
                    {leg.isDual ? "Dual Carriageway" : "Single Highway"} ({leg.inboundLanes}L)
                  </span>
                </div>

                <div style={{ display: "flex", gap: "8rem" }}>
                  <div style={{ flex: 1 }}>
                    <CustomDropdown
                      value={leg.overrideLanes || 0}
                      options={legLaneOptions}
                      onChange={(val) =>
                        setLegOverride(
                          leg.index,
                          parseInt(val, 10),
                          leg.overridePrefab || "",
                          leg.upgrades || 0
                        )
                      }
                    />
                  </div>

                  {availablePrefabs && availablePrefabs.length > 0 && (
                    <div style={{ flex: 1 }}>
                      <CustomDropdown
                        value={leg.overridePrefab || ""}
                        options={legPrefabOptions}
                        onChange={(val) =>
                          setLegOverride(
                            leg.index,
                            leg.overrideLanes || 0,
                            String(val),
                            leg.upgrades || 0
                          )
                        }
                      />
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Road Types & Global Prefabs */}
      {availablePrefabs && availablePrefabs.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <span>Road Types & Prefabs</span>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Mainline Highway</span>
            <div style={{ flex: 1 }}>
              <CustomDropdown
                value=""
                options={[
                  { value: "", label: "Auto-Detect from Scanned Road" },
                  ...availablePrefabs.map((p) => ({ value: p, label: p })),
                ]}
                onChange={(val) => setGlobalPrefab("main", String(val))}
              />
            </div>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Connecting Ramp</span>
            <div style={{ flex: 1 }}>
              <CustomDropdown
                value=""
                options={[
                  { value: "", label: "Auto-Detect Standard Ramp" },
                  ...availablePrefabs.map((p) => ({ value: p, label: p })),
                ]}
                onChange={(val) => setGlobalPrefab("ramp", String(val))}
              />
            </div>
          </div>
        </div>
      )}

      {/* Upgrades Checkboxes */}
      <div className={styles.section}>
        <div className={styles.sectionTitle}>
          <span>Road Upgrades & Features</span>
        </div>

        <div className={styles.checkboxGrid}>
          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={true}
              onChange={(e) => setGlobalUpgrade(1 << 2, e.target.checked)} // StreetLighting
            />
            <span>Street Lighting</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 3, e.target.checked)} // WideMedian
            />
            <span>Wide Median</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 0, e.target.checked)} // SoundBarrierLeft
            />
            <span>Sound Barrier (Left)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 1, e.target.checked)} // SoundBarrierRight
            />
            <span>Sound Barrier (Right)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 4, e.target.checked)} // RetainingWallLeft
            />
            <span>Retaining Wall (Left)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 5, e.target.checked)} // RetainingWallRight
            />
            <span>Retaining Wall (Right)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 6, e.target.checked)} // RemoveTrafficLights
            />
            <span>Remove Traffic Lights</span>
          </label>
        </div>
      </div>
    </div>
  );
};
