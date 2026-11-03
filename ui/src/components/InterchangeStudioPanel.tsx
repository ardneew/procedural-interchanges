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
            onClick={() => setGlobalParam("lefthandtraffic", state.leftHandTraffic ? 0 : 1)}
            title={state.leftHandTraffic ? "Switch to Right-Hand Traffic (RHT)" : "Switch to Left-Hand Traffic (LHT)"}
            style={{ fontSize: "11rem", fontWeight: 700, color: state.leftHandTraffic ? "#f59e0b" : "#00e5ff" }}
          >
            {state.leftHandTraffic ? "LHT" : "RHT"}
          </button>
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

      {/* 2-Step Progress Bar */}
      <div className={styles.stepBar}>
        <div
          className={`${styles.stepItem} ${stage <= 2 ? styles.active : styles.completed}`}
          onClick={() => setStage(2)}
        >
          1. Highway Ends ({legs.length}/{state.requiredLegs || 3})
        </div>
        <div
          className={`${styles.stepItem} ${stage === 3 ? styles.active : ""}`}
          onClick={() => {
            if (legs.length >= (state.requiredLegs || 3)) setStage(3);
          }}
        >
          2. Configure & Build
        </div>
      </div>

      {/* Main Content Area */}
      <div className={styles.contentScroll}>
        {state.angleWarning && (
          <div
            style={{
              background: "rgba(245, 158, 11, 0.15)",
              border: "1rem solid #f59e0b",
              color: "#f59e0b",
              padding: "8rem 12rem",
              borderRadius: "6rem",
              fontSize: "11rem",
              lineHeight: 1.4,
              display: "flex",
              alignItems: "center",
              gap: "6rem",
            }}
          >
            <span style={{ fontSize: "14rem" }}>⚠️</span>
            <span>{state.angleWarning}</span>
          </div>
        )}

        {stage <= 2 && (
          <div className={styles.section}>
            <div className={styles.sectionTitle}>
              <span>Select Highway Ends</span>
            </div>

            <p style={{ fontSize: "11rem", color: "#94a3b8", lineHeight: 1.5, margin: 0 }}>
              Hover and click each highway end in the world ({legs.length} / {state.requiredLegs || 3} selected) to procedurally generate and connect the interchange.
            </p>

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

            {legs.length >= (state.requiredLegs || 3) && (
              <button
                className={styles.buildBtn}
                style={{ marginTop: "10rem" }}
                onClick={() => setStage(3)}
              >
                Proceed to Configure & Build ({legs.length} Legs) →
              </button>
            )}
          </div>
        )}

        {stage === 3 && (
          <>
            <div className={styles.controlRow} style={{ marginBottom: "6rem" }}>
              <span className={styles.label}>Traffic Handedness</span>
              <button
                className={styles.undoBtn}
                style={{ padding: "4rem 8rem", fontSize: "11rem", color: state.leftHandTraffic ? "#f59e0b" : "#00e5ff" }}
                onClick={() => setGlobalParam("lefthandtraffic", state.leftHandTraffic ? 0 : 1)}
              >
                {state.leftHandTraffic ? "Left-Hand Traffic (LHT)" : "Right-Hand Traffic (RHT)"}
              </button>
            </div>

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
            disabled={legs.length < (state.requiredLegs || 3)}
          >
            Build Interchange
          </button>
        )}
      </div>
    </div>
  );
};
