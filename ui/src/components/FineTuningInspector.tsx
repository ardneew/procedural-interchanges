import React from "react";
import { InterchangeUIState } from "../types/interchange-types";
import {
  setGlobalParam,
  setGlobalPrefab,
  setGlobalUpgrade,
  setLegOverride,
  setCourseOverride,
} from "../bindings/interchange-bindings";
import styles from "./studio.module.scss";

interface FineTuningInspectorProps {
  state: InterchangeUIState;
}

export const FineTuningInspector: React.FC<FineTuningInspectorProps> = ({ state }) => {
  const selectedCourse = state.courses2D.find((c) => c.id === state.selectedCourseId);

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
            value={state.footprintRadius}
            onChange={(e) => setGlobalParam("radius", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.footprintRadius}m</span>
        </div>

        {/* Rotation Skew */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Rotation Skew</span>
          <input
            type="range"
            min="-180"
            max="180"
            step="1"
            value={state.rotationDeg}
            onChange={(e) => setGlobalParam("rotation", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.rotationDeg.toFixed(0)}°</span>
        </div>

        {/* Max Mainline Grade */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Max Mainline Slope</span>
          <input
            type="range"
            min="2.0"
            max="8.0"
            step="0.5"
            value={state.maxMainlineGrade}
            onChange={(e) => setGlobalParam("maxMainlineGrade", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.maxMainlineGrade.toFixed(1)}%</span>
        </div>

        {/* Max Ramp Grade */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Max Ramp Slope</span>
          <input
            type="range"
            min="3.0"
            max="10.0"
            step="0.5"
            value={state.maxRampGrade}
            onChange={(e) => setGlobalParam("maxRampGrade", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.maxRampGrade.toFixed(1)}%</span>
        </div>

        {/* Min Bridge Clearance */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Overpass Clearance</span>
          <input
            type="range"
            min="8.0"
            max="15.0"
            step="0.5"
            value={state.minClearance}
            onChange={(e) => setGlobalParam("minClearance", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.minClearance.toFixed(1)}m</span>
        </div>

        {/* Design Speed */}
        <div className={styles.controlRow}>
          <span className={styles.label}>Design Speed</span>
          <input
            type="range"
            min="60"
            max="130"
            step="10"
            value={state.designSpeed}
            onChange={(e) => setGlobalParam("designSpeed", parseFloat(e.target.value))}
          />
          <span className={styles.value}>{state.designSpeed} km/h</span>
        </div>

        {/* Grade Separation & Overpass Toggles */}
        <div className={styles.checkboxGrid} style={{ marginTop: "8rem" }}>
          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              checked={state.gradeSeparateMainline}
              onChange={(e) => setGlobalParam("gradeSeparateMainline", e.target.checked ? 1 : 0)}
            />
            <span>Grade-Separate Mainline</span>
          </label>
          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              checked={state.mainlineOverpass}
              onChange={(e) => setGlobalParam("mainlineOverpass", e.target.checked ? 1 : 0)}
            />
            <span>Mainline Overpass</span>
          </label>
        </div>
      </div>

      {/* Selected Segment Override (when clicked on 2D plan) */}
      {selectedCourse && (
        <div
          className={styles.section}
          style={{
            background: "rgba(56, 189, 248, 0.05)",
            border: "1rem solid rgba(56, 189, 248, 0.35)",
            borderRadius: "6rem",
            padding: "8rem 10rem",
          }}
        >
          <div className={styles.sectionTitle}>
            <span style={{ color: "#38bdf8" }}>Selected: {selectedCourse.roleName}</span>
            <span style={{ fontSize: "10rem", color: "#94a3b8" }}>{selectedCourse.id}</span>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Lanes</span>
            <select
              value={selectedCourse.lanes}
              onChange={(e) =>
                setCourseOverride(
                  selectedCourse.id,
                  parseInt(e.target.value, 10),
                  selectedCourse.prefabName || "",
                  0
                )
              }
            >
              {[1, 2, 3, 4, 5].map((l) => (
                <option key={`lane-${l}`} value={l}>
                  {l} {l === 1 ? "Lane" : "Lanes"}
                </option>
              ))}
            </select>
          </div>

          {state.availablePrefabs && state.availablePrefabs.length > 0 && (
            <div className={styles.controlRow}>
              <span className={styles.label}>Road Prefab</span>
              <select
                value={selectedCourse.prefabName || ""}
                onChange={(e) =>
                  setCourseOverride(
                    selectedCourse.id,
                    selectedCourse.lanes,
                    e.target.value,
                    0
                  )
                }
              >
                <option value="">Auto-Detect</option>
                {state.availablePrefabs.map((p) => (
                  <option key={`sel-pref-${p}`} value={p}>
                    {p}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>
      )}

      {/* Per-Leg Overrides */}
      {state.legs && state.legs.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <span>Connected Highway Legs</span>
            <span style={{ fontSize: "10rem", color: "#94a3b8" }}>{state.legs.length} Portals</span>
          </div>

          {state.legs.map((leg) => (
            <div
              key={`leg-card-${leg.index}`}
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "4rem",
                padding: "8rem",
                background: "rgba(255,255,255,0.03)",
                border: "1rem solid rgba(255,255,255,0.06)",
                borderRadius: "6rem",
                marginBottom: "4rem",
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
                  <select
                    style={{ width: "100%", fontSize: "10rem" }}
                    value={leg.overrideLanes || 0}
                    onChange={(e) =>
                      setLegOverride(
                        leg.index,
                        parseInt(e.target.value, 10),
                        leg.overridePrefab || "",
                        leg.upgrades || 0
                      )
                    }
                  >
                    <option value={0}>Auto Lanes ({leg.inboundLanes}L)</option>
                    {[1, 2, 3, 4, 5].map((l) => (
                      <option key={`leg-${leg.index}-l-${l}`} value={l}>
                        {l} {l === 1 ? "Lane" : "Lanes"}
                      </option>
                    ))}
                  </select>
                </div>

                {state.availablePrefabs && state.availablePrefabs.length > 0 && (
                  <div style={{ flex: 1 }}>
                    <select
                      style={{ width: "100%", fontSize: "10rem" }}
                      value={leg.overridePrefab || ""}
                      onChange={(e) =>
                        setLegOverride(
                          leg.index,
                          leg.overrideLanes || 0,
                          e.target.value,
                          leg.upgrades || 0
                        )
                      }
                    >
                      <option value="">Auto Prefab</option>
                      {state.availablePrefabs.map((p) => (
                        <option key={`leg-${leg.index}-p-${p}`} value={p}>
                          {p}
                        </option>
                      ))}
                    </select>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Road Types & Global Prefabs */}
      {state.availablePrefabs && state.availablePrefabs.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <span>Road Types & Prefabs</span>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Mainline Highway</span>
            <select
              onChange={(e) => setGlobalPrefab("main", e.target.value)}
              defaultValue=""
            >
              <option value="">Auto-Detect from Scanned Road</option>
              {state.availablePrefabs.map((p) => (
                <option key={`main-${p}`} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </div>

          <div className={styles.controlRow}>
            <span className={styles.label}>Connecting Ramp</span>
            <select
              onChange={(e) => setGlobalPrefab("ramp", e.target.value)}
              defaultValue=""
            >
              <option value="">Auto-Detect Standard Ramp</option>
              {state.availablePrefabs.map((p) => (
                <option key={`ramp-${p}`} value={p}>
                  {p}
                </option>
              ))}
            </select>
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
