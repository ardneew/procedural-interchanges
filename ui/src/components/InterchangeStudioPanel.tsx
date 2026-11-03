import React, { useState } from "react";
import { useValue } from "cs2/api";
import {
  state$,
  toggleTool,
  setStage,
  autoDetectHighways,
  autoFitFootprint,
  setGlobalParam,
  removeLeg,
  buildInterchange,
  undoLastBuild,
  setGhostEnabled,
  setOverlayEnabled,
} from "../bindings/interchange-bindings";
import { ArchetypeSelector } from "./ArchetypeSelector";
import { FineTuningInspector } from "./FineTuningInspector";
import { Visualizer2DPlan } from "./Visualizer2DPlan";
import { Visualizer2DElevation } from "./Visualizer2DElevation";
import styles from "./studio.module.scss";

export const InterchangeStudioPanel: React.FC = () => {
  const state = useValue(state$);
  const [activeTab, setActiveTab] = useState<"visualizer" | "archetypes" | "tuning">("visualizer");

  if (!state || !state.toolActive) {
    return null;
  }

  const { stage, legs, courses2D, telemetry, availableArchetypes, selectedArchetype } = state;

  return (
    <div className={styles.studioContainer}>
      {/* Studio Header */}
      <div className={styles.studioHeader}>
        <div className={styles.titleBlock}>
          <svg className={styles.icon} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="3" />
            <path d="M 2 12 H 9 M 15 12 H 22" />
            <path d="M 12 2 V 9 M 12 15 V 22" />
          </svg>
          <h2>Interchange Studio</h2>
        </div>

        <div className={styles.headerControls}>
          <button
            className={styles.iconBtn}
            onClick={() => setGhostEnabled(!state.liveGhostEnabled)}
            title={state.liveGhostEnabled ? "Disable 3D Ghost Preview" : "Enable 3D Ghost Preview"}
            style={{ color: state.liveGhostEnabled ? "#00e5ff" : "#64748b" }}
          >
            3D
          </button>
          <button
            className={styles.iconBtn}
            onClick={() => setOverlayEnabled(!state.overlayEnabled)}
            title={state.overlayEnabled ? "Disable Overlay" : "Enable Overlay"}
            style={{ color: state.overlayEnabled ? "#2ecc71" : "#64748b" }}
          >
            CAD
          </button>
          <button className={styles.iconBtn} onClick={toggleTool} title="Close Studio">
            ✕
          </button>
        </div>
      </div>

      {/* 4-Step Progress Bar */}
      <div className={styles.stepBar}>
        <div
          className={`${styles.stepItem} ${stage === 1 ? styles.active : stage > 1 ? styles.completed : ""}`}
          onClick={() => setStage(1)}
        >
          1. Center
        </div>
        <div
          className={`${styles.stepItem} ${stage === 2 ? styles.active : stage > 2 ? styles.completed : ""}`}
          onClick={() => setStage(2)}
        >
          2. Highways ({legs.length})
        </div>
        <div
          className={`${styles.stepItem} ${stage === 3 ? styles.active : ""}`}
          onClick={() => {
            if (legs.length >= 3) setStage(3);
          }}
        >
          3. Design & Tune
        </div>
      </div>

      {/* Main Content Area */}
      <div className={styles.contentScroll}>
        {stage === 1 && (
          <div className={styles.section}>
            <div className={styles.sectionTitle}>
              <span>Set Center Point</span>
            </div>
            <p style={{ fontSize: "11rem", color: "#94a3b8", lineHeight: 1.5, margin: 0 }}>
              Click anywhere on the ground or existing highway to establish the center of the intersection.
              Nearby dual-carriageway and two-way highways will be automatically detected.
            </p>
          </div>
        )}

        {stage === 2 && (
          <div className={styles.section}>
            <div className={styles.sectionTitle}>
              <span>Connected Highway Legs</span>
              <button
                className={styles.undoBtn}
                style={{ padding: "4rem 8rem", fontSize: "10rem" }}
                onClick={autoDetectHighways}
              >
                Auto-Detect Nearby
              </button>
            </div>

            <p style={{ fontSize: "11rem", color: "#94a3b8", lineHeight: 1.5, margin: 0 }}>
              Click highway edges to add or remove connected legs. Separated direction highways are automatically paired.
              Hold <b>Shift</b> while clicking to select a single one-way carriageway without twin pairing.
            </p>

            <div className={styles.controlRow} style={{ marginTop: "10rem", marginBottom: "4rem" }}>
              <span className={styles.label}>Footprint Size</span>
              <input
                type="range"
                min="120"
                max="500"
                step="10"
                value={state.footprintRadius}
                onChange={(e) => setGlobalParam("radius", parseFloat(e.target.value))}
              />
              <span className={styles.value}>{state.footprintRadius}m</span>
              <button
                className={styles.undoBtn}
                style={{ padding: "4rem 8rem", fontSize: "10rem", marginLeft: "6rem" }}
                onClick={autoFitFootprint}
                title="Automatically find minimal footprint radius that eliminates clearance and grade violations"
              >
                Auto-Fit
              </button>
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: "6rem", marginTop: "8rem" }}>
              {legs.map((leg) => (
                <div
                  key={`leg-item-${leg.index}`}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "6rem 10rem",
                    background: "rgba(255,255,255,0.04)",
                    borderRadius: "6rem",
                    fontSize: "11rem",
                  }}
                >
                  <span>
                    Leg #{leg.index + 1} ({leg.azimuthDeg.toFixed(0)}°) — {leg.isDual ? "Dual Carriageway" : "Single Two-Way"}
                  </span>
                  <button
                    style={{ background: "none", border: "none", color: "#e74c3c", cursor: "pointer" }}
                    onClick={() => removeLeg(leg.index)}
                    title="Remove Leg"
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>

            {legs.length >= 3 && (
              <button
                className={styles.buildBtn}
                style={{ marginTop: "10rem" }}
                onClick={() => setStage(3)}
              >
                Proceed to Design ({legs.length} Legs) →
              </button>
            )}
          </div>
        )}

        {stage === 3 && (
          <>
            <div className={styles.controlRow} style={{ marginBottom: "10rem" }}>
              <span className={styles.label}>Footprint Size</span>
              <input
                type="range"
                min="120"
                max="500"
                step="10"
                value={state.footprintRadius}
                onChange={(e) => setGlobalParam("radius", parseFloat(e.target.value))}
              />
              <span className={styles.value}>{state.footprintRadius}m</span>
              <button
                className={styles.undoBtn}
                style={{ padding: "4rem 8rem", fontSize: "10rem", marginLeft: "6rem" }}
                onClick={autoFitFootprint}
                title="Automatically find minimal footprint radius that eliminates clearance and grade violations"
              >
                Auto-Fit
              </button>
            </div>

            {/* View Sub-Tabs */}
            <div style={{ display: "flex", gap: "6rem" }}>
              <button
                className={`${styles.undoBtn} ${activeTab === "visualizer" ? styles.selected : ""}`}
                style={{ flex: 1, padding: "6rem", fontSize: "11rem" }}
                onClick={() => setActiveTab("visualizer")}
              >
                2D CAD & Profile
              </button>
              <button
                className={`${styles.undoBtn} ${activeTab === "archetypes" ? styles.selected : ""}`}
                style={{ flex: 1, padding: "6rem", fontSize: "11rem" }}
                onClick={() => setActiveTab("archetypes")}
              >
                Intersections
              </button>
              <button
                className={`${styles.undoBtn} ${activeTab === "tuning" ? styles.selected : ""}`}
                style={{ flex: 1, padding: "6rem", fontSize: "11rem" }}
                onClick={() => setActiveTab("tuning")}
              >
                Fine-Tuning
              </button>
            </div>

            {activeTab === "visualizer" && (
              <>
                <Visualizer2DPlan
                  courses={courses2D}
                  legs={legs}
                  selectedCourseId={state.selectedCourseId}
                  footprintRadius={state.footprintRadius}
                />
                <Visualizer2DElevation
                  samples={state.elevationProfile}
                  selectedCourseId={state.selectedCourseId}
                />
              </>
            )}

            {activeTab === "archetypes" && (
              <ArchetypeSelector
                archetypes={availableArchetypes}
                selectedId={selectedArchetype}
              />
            )}

            {activeTab === "tuning" && (
              <FineTuningInspector state={state} />
            )}
          </>
        )}
      </div>

      {/* Telemetry Bar */}
      {stage === 3 && (
        <div style={{ padding: "0 16rem" }}>
          <div className={styles.telemetryBar}>
            <div className={styles.telemetryItem}>
              <span className={styles.tLabel}>Cost</span>
              <span className={styles.tVal}>₡{(telemetry.estimatedCost / 1000).toFixed(0)}k</span>
            </div>
            <div className={styles.telemetryItem}>
              <span className={styles.tLabel}>Worst Slope</span>
              <span className={`${styles.tVal} ${telemetry.worstGrade > 6.0 ? styles.violation : telemetry.worstGrade > 4.0 ? styles.marginal : styles.optimal}`}>
                {telemetry.worstGrade.toFixed(1)}%
              </span>
            </div>
            <div className={styles.telemetryItem}>
              <span className={styles.tLabel}>Tightest R</span>
              <span className={`${styles.tVal} ${telemetry.tightestRadius < 30 ? styles.violation : telemetry.tightestRadius < 55 ? styles.marginal : styles.optimal}`}>
                {telemetry.tightestRadius > 999 ? "∞" : `${telemetry.tightestRadius.toFixed(0)}m`}
              </span>
            </div>
            <div className={styles.telemetryItem}>
              <span className={styles.tLabel}>Clearance</span>
              <span className={`${styles.tVal} ${telemetry.lowestClearance < 7.5 ? styles.violation : styles.optimal}`}>
                {telemetry.lowestClearance > 50 ? "—" : `${telemetry.lowestClearance.toFixed(1)}m`}
              </span>
            </div>
          </div>
        </div>
      )}

      {/* Bottom Actions Footer */}
      <div className={styles.footerActions}>
        <button
          className={styles.undoBtn}
          onClick={undoLastBuild}
          disabled={!state.canUndo}
          title="Demolish last interchange and restore original highways (Ctrl+Z)"
        >
          Undo
        </button>

        {stage === 3 && (
          <button
            className={styles.buildBtn}
            onClick={buildInterchange}
            disabled={legs.length < 3}
          >
            Build Interchange
          </button>
        )}
      </div>
    </div>
  );
};
