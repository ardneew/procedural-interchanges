import React, { useState } from "react";
import { useValue } from "cs2/api";
import {
  state$,
  toggleTool,
  setStage,
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
            className={`${styles.pillToggle} ${state.leftHandTraffic ? styles.active : ""}`}
            onClick={() => setGlobalParam("lefthandtraffic", state.leftHandTraffic ? 0 : 1)}
            title={state.leftHandTraffic ? "Switch to Right-Hand Traffic (RHT)" : "Switch to Left-Hand Traffic (LHT)"}
          >
            {state.leftHandTraffic ? "LHT" : "RHT"}
          </button>
          <button
            className={`${styles.pillToggle} ${state.liveGhostEnabled ? styles.active : ""}`}
            onClick={() => setGhostEnabled(!state.liveGhostEnabled)}
            title={state.liveGhostEnabled ? "Disable 3D Ghost Preview" : "Enable 3D Ghost Preview"}
          >
            3D GHOST
          </button>
          <button
            className={`${styles.pillToggle} ${state.overlayEnabled ? styles.active : ""}`}
            onClick={() => setOverlayEnabled(!state.overlayEnabled)}
            title={state.overlayEnabled ? "Disable Overlay" : "Enable Overlay"}
          >
            CAD
          </button>
          <button className={styles.closeBtn} onClick={toggleTool} title="Close Studio">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>
      </div>

      {/* 2-Step Progress Bar */}
      <div className={styles.stepBar}>
        <div
          className={`${styles.stepItem} ${stage <= 2 ? styles.active : styles.completed}`}
          onClick={() => setStage(2)}
        >
          <div className={`${styles.stepBadge} ${stage <= 2 ? styles.activeBadge : styles.completedBadge}`}>
            {stage > 2 ? "✓" : "1"}
          </div>
          <span>Highway Ends ({legs.length}/{state.requiredLegs || 3})</span>
        </div>
        <div
          className={`${styles.stepItem} ${stage === 3 ? styles.active : ""}`}
          onClick={() => {
            if (legs.length >= (state.requiredLegs || 3)) setStage(3);
          }}
        >
          <div className={`${styles.stepBadge} ${stage === 3 ? styles.activeBadge : ""}`}>
            2
          </div>
          <span>Configure & Build</span>
        </div>
      </div>

      {/* Main Content Area */}
      <div className={styles.contentScroll}>
        {state.angleWarning && (
          <div className={styles.warningBox}>
            <svg viewBox="0 0 24 24">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
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
                    background: "rgba(255,255,255,0.035)",
                    border: "1rem solid rgba(255,255,255,0.06)",
                    borderRadius: "6rem",
                    fontSize: "11rem",
                  }}
                >
                  <span style={{ color: "#f8fafc" }}>
                    Leg #{leg.index + 1} ({leg.azimuthDeg.toFixed(0)}°) — {leg.isDual ? "Dual Carriageway" : "Single Highway"}
                  </span>
                  <button
                    style={{ background: "none", border: "none", color: "#f87171", cursor: "pointer", fontSize: "13rem" }}
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
                className={styles.primaryBtn}
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
                className={styles.ghostBtn}
                style={{ padding: "4rem 8rem", fontSize: "11rem", color: "#38bdf8" }}
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
                className={styles.ghostBtn}
                style={{ padding: "4rem 8rem", fontSize: "10rem", marginLeft: "6rem" }}
                onClick={autoFitFootprint}
                title="Automatically find minimal footprint radius that eliminates clearance and grade violations"
              >
                Auto-Fit
              </button>
            </div>

            {/* Segmented Sub-Tab Bar */}
            <div className={styles.subTabBar}>
              <button
                className={`${styles.tabBtn} ${activeTab === "visualizer" ? styles.active : ""}`}
                onClick={() => setActiveTab("visualizer")}
              >
                2D Blueprint
              </button>
              <button
                className={`${styles.tabBtn} ${activeTab === "archetypes" ? styles.active : ""}`}
                onClick={() => setActiveTab("archetypes")}
              >
                Archetypes
              </button>
              <button
                className={`${styles.tabBtn} ${activeTab === "tuning" ? styles.active : ""}`}
                onClick={() => setActiveTab("tuning")}
              >
                Engineering
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

      {/* Telemetry Strip */}
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
          className={styles.ghostBtn}
          onClick={undoLastBuild}
          disabled={!state.canUndo}
          title="Demolish last interchange and restore original highways (Ctrl+Z)"
        >
          Undo
        </button>

        {stage === 3 && (
          <button
            className={styles.primaryBtn}
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
