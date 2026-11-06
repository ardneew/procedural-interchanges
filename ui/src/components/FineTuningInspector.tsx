import { CustomSlider } from "./CustomSlider";
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

        {/* Intersection Size */}
        <CustomSlider
          label="Intersection Size"
          min={120}
          max={500}
          step={10}
          value={state.footprintRadius ?? 260}
          unit="m"
          onChange={(v) => setGlobalParam("radius", v)}
        />

        {/* Rotation Skew */}
        <CustomSlider
          label="Rotation Skew"
          min={-180}
          max={180}
          step={1}
          value={state.rotationDeg ?? 0}
          unit="deg"
          onChange={(v) => setGlobalParam("rotation", v)}
        />

        {/* Max Mainline Slope */}
        <CustomSlider
          label="Max Mainline Slope"
          min={2.0}
          max={8.0}
          step={0.5}
          decimals={1}
          value={state.maxMainlineGrade ?? 4.0}
          unit="%"
          onChange={(v) => setGlobalParam("maxMainlineGrade", v)}
        />

        {/* Max Ramp Slope */}
        <CustomSlider
          label="Max Ramp Slope"
          min={3.0}
          max={10.0}
          step={0.5}
          decimals={1}
          value={state.maxRampGrade ?? 6.0}
          unit="%"
          onChange={(v) => setGlobalParam("maxRampGrade", v)}
        />

        {/* Overpass Clearance (Default 7.5m) */}
        <CustomSlider
          label="Overpass Clearance"
          min={6.0}
          max={15.0}
          step={0.5}
          decimals={1}
          value={state.minClearance ?? 7.5}
          unit="m"
          onChange={(v) => setGlobalParam("minClearance", v)}
        />

        {/* Design Speed */}
        <CustomSlider
          label="Design Speed"
          min={60}
          max={130}
          step={10}
          value={state.designSpeed ?? 100}
          unit=" km/h"
          onChange={(v) => setGlobalParam("designSpeed", v)}
        />

        {/* Flow Priority & Underground Controls */}
        <div className={styles.controlRow} style={{ marginTop: "8rem" }}>
          <span className={styles.label}>Flow Priority</span>
          <div style={{ display: "flex", gap: "4rem" }}>
            <button
              className={styles.ghostBtn}
              style={{
                padding: "3rem 6rem",
                fontSize: "10rem",
                color: (state.flowPriorityMode ?? 0) === 0 ? "#38bdf8" : "#94a3b8",
                borderColor: (state.flowPriorityMode ?? 0) === 0 ? "#38bdf8" : "rgba(255,255,255,0.1)",
              }}
              onClick={() => setGlobalParam("flowpriority", 0)}
              title="Maintain 100% of mainline lanes through diverge/merge with acute gore exit"
            >
              Maintain Thru-Lanes
            </button>
            <button
              className={styles.ghostBtn}
              style={{
                padding: "3rem 6rem",
                fontSize: "10rem",
                color: (state.flowPriorityMode ?? 0) === 1 ? "#38bdf8" : "#94a3b8",
                borderColor: (state.flowPriorityMode ?? 0) === 1 ? "#38bdf8" : "rgba(255,255,255,0.1)",
              }}
              onClick={() => setGlobalParam("flowpriority", 1)}
              title="Split mainline lanes evenly at major exits"
            >
              Lane-Drop Split
            </button>
            <button
              className={styles.ghostBtn}
              style={{ padding: "3rem 6rem", fontSize: "10rem", color: "#f8fafc" }}
              onClick={() => setGlobalParam("swapprimary", (state.primaryAxisOffset ?? 0) + 1)}
              title="Cycle which opposing highway pair is the continuous Primary Throughway"
            >
              Swap Primary
            </button>
          </div>
        </div>

        {/* Underground Mode Selector */}
        <div className={styles.controlRow} style={{ marginTop: "6rem" }}>
          <span className={styles.label}>Underground Mode</span>
          <select
            value={state.undergroundMode ?? 0}
            onChange={(e) => setGlobalParam("underground", parseInt(e.target.value, 10))}
            style={{ flex: 1, marginLeft: "10rem" }}
          >
            <option value={0}>None (Surface & Overpasses)</option>
            <option value={1}>Depress Secondary Thruway (-1 Tunnel)</option>
            <option value={2}>Shift Stack Down (-1 Tunnel, Lower Bridges)</option>
            <option value={3}>Both Thruways Underground (-1 / -2 Tunnels)</option>
          </select>
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

      {/* Selected Segment Override */}
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
            <div style={{ flex: 1 }}>
              <CustomDropdown
                value={selectedCourse.lanes}
                options={laneOptions}
                onChange={(val) =>
                  setCourseOverride(
                    selectedCourse.id,
                    Number(val),
                    selectedCourse.prefabName || "",
                    0
                  )
                }
              />
            </div>
          </div>

          {availablePrefabs.length > 0 && (
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
      {legs.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <span>Connected Highway Legs</span>
            <span style={{ fontSize: "10rem", color: "#94a3b8" }}>{legs.length} Portals</span>
          </div>

          {legs.map((leg) => (
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
                  <CustomDropdown
                    value={leg.overrideLanes || 0}
                    options={[
                      { value: 0, label: `Auto Lanes (${leg.inboundLanes}L)` },
                      ...laneOptions,
                    ]}
                    onChange={(val) =>
                      setLegOverride(
                        leg.index,
                        Number(val),
                        leg.overridePrefab || "",
                        leg.upgrades || 0
                      )
                    }
                  />
                </div>

                {availablePrefabs.length > 0 && (
                  <div style={{ flex: 1 }}>
                    <CustomDropdown
                      value={leg.overridePrefab || ""}
                      options={[
                        { value: "", label: "Auto Prefab" },
                        ...availablePrefabs.map((p) => ({ value: p, label: p })),
                      ]}
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
          ))}
        </div>
      )}

      {/* Road Types & Global Prefabs */}
      {availablePrefabs.length > 0 && (
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
              onChange={(e) => setGlobalUpgrade(1 << 2, e.target.checked)}
            />
            <span>Street Lighting</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 3, e.target.checked)}
            />
            <span>Wide Median</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 0, e.target.checked)}
            />
            <span>Sound Barrier (Left)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 1, e.target.checked)}
            />
            <span>Sound Barrier (Right)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 4, e.target.checked)}
            />
            <span>Retaining Wall (Left)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 5, e.target.checked)}
            />
            <span>Retaining Wall (Right)</span>
          </label>

          <label className={styles.checkboxItem}>
            <input
              type="checkbox"
              defaultChecked={false}
              onChange={(e) => setGlobalUpgrade(1 << 6, e.target.checked)}
            />
            <span>Remove Traffic Lights</span>
          </label>
        </div>
      </div>
    </div>
  );
};
