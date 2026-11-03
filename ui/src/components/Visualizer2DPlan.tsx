import React, { useState } from "react";
import { Course2D, LegInfo } from "../types/interchange-types";
import { selectCourseForProfile } from "../bindings/interchange-bindings";
import styles from "./studio.module.scss";

interface Visualizer2DPlanProps {
  courses: Course2D[];
  legs: LegInfo[];
  selectedCourseId: string;
  footprintRadius: number;
}

export const Visualizer2DPlan: React.FC<Visualizer2DPlanProps> = ({
  courses,
  legs,
  selectedCourseId,
  footprintRadius,
}) => {
  const [hoveredCourse, setHoveredCourse] = useState<Course2D | null>(null);
  const [colorMode, setColorMode] = useState<"compliance" | "stack">("compliance");

  const cx = 190;
  const cy = 190;
  const svgRadius = 150;

  const toSvgX = (nx: number) => cx + nx * svgRadius;
  const toSvgY = (ny: number) => cy + ny * svgRadius; // Invert Z to match 2D top-down

  const getStackColor = (level: number) => {
    switch (level) {
      case 0:
        return "#64748b"; // Slate
      case 1:
        return "#38bdf8"; // Sky
      case 2:
        return "#a855f7"; // Violet
      default:
        return "#f59e0b"; // Amber
    }
  };

  return (
    <div className={styles.visWrapper}>
      {/* Header bar with color mode toggle */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "6rem",
          padding: "0 4rem",
        }}
      >
        <span style={{ fontSize: "11rem", fontWeight: 600, color: "#94a3b8" }}>
          CAD Blueprint ({footprintRadius}m)
        </span>
        <button
          style={{
            background: "rgba(255,255,255,0.08)",
            border: "1rem solid rgba(255,255,255,0.15)",
            borderRadius: "4rem",
            color: "#38bdf8",
            fontSize: "10rem",
            padding: "2rem 8rem",
            cursor: "pointer",
          }}
          onClick={() => setColorMode(colorMode === "compliance" ? "stack" : "compliance")}
        >
          {colorMode === "compliance" ? "Mode: Compliance Heatmap" : "Mode: Stack / Tier"}
        </button>
      </div>

      <svg width="380" height="380" viewBox="0 0 380 380">
        <defs>
          <radialGradient id="cadBackground" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#0f172a" />
            <stop offset="100%" stopColor="#070b14" />
          </radialGradient>

          <filter id="bridgeShadow" x="-20%" y="-20%" width="140%" height="140%">
            <feDropShadow dx="0" dy="2" stdDeviation="3" floodColor="#000000" floodOpacity="0.8" />
          </filter>
        </defs>

        {/* Blueprint background grid */}
        <rect width="380" height="380" fill="url(#cadBackground)" />
        <circle cx={cx} cy={cy} r={svgRadius * 0.33} fill="none" stroke="rgba(255,255,255,0.05)" strokeDasharray="3,3" />
        <circle cx={cx} cy={cy} r={svgRadius * 0.66} fill="none" stroke="rgba(255,255,255,0.05)" strokeDasharray="3,3" />
        <circle cx={cx} cy={cy} r={svgRadius} fill="none" stroke="rgba(0, 229, 255, 0.35)" strokeWidth="1.5" />

        {/* Crosshairs & compass */}
        <line x1={cx - svgRadius - 10} y1={cy} x2={cx + svgRadius + 10} y2={cy} stroke="rgba(255,255,255,0.08)" />
        <line x1={cx} y1={cy - svgRadius - 10} x2={cx} y2={cy + svgRadius + 10} stroke="rgba(255,255,255,0.08)" />
        <text x={cx} y={cy - svgRadius - 12} textAnchor="middle" fill="#64748b" fontSize="9">N</text>
        <text x={cx + svgRadius + 16} y={cy + 3} textAnchor="start" fill="#64748b" fontSize="9">E</text>

        {/* Connected Highway Leg Portals */}
        {legs.map((leg) => {
          const rad = (leg.azimuthDeg * Math.PI) / 180;
          const px = cx + Math.cos(rad) * svgRadius;
          const py = cy + Math.sin(rad) * svgRadius;

          return (
            <g key={`leg-${leg.index}`}>
              <circle cx={px} cy={py} r={7} fill="#1e293b" stroke="#00e5ff" strokeWidth="1.5" />
              <text x={px} y={py + 3} textAnchor="middle" fill="#f8fafc" fontSize="8" fontWeight="bold">
                {leg.index + 1}
              </text>
            </g>
          );
        })}

        {/* Planned Network Curves sorted by stackLevel */}
        {[...courses]
          .sort((a, b) => a.stackLevel - b.stackLevel)
          .map((c) => {
            const isSelected = c.id === selectedCourseId;
            const isHovered = hoveredCourse?.id === c.id;

            const p0x = toSvgX(c.p0x);
            const p0y = toSvgY(c.p0y);
            const p1x = toSvgX(c.p1x);
            const p1y = toSvgY(c.p1y);
            const p2x = toSvgX(c.p2x);
            const p2y = toSvgY(c.p2y);
            const p3x = toSvgX(c.p3x);
            const p3y = toSvgY(c.p3y);

            const pathD = `M ${p0x.toFixed(1)},${p0y.toFixed(1)} C ${p1x.toFixed(1)},${p1y.toFixed(1)} ${p2x.toFixed(1)},${p2y.toFixed(1)} ${p3x.toFixed(1)},${p3y.toFixed(1)}`;

            const strokeColor =
              colorMode === "compliance"
                ? c.compliance === 0
                  ? "#2ecc71"
                  : c.compliance === 1
                  ? "#f1c40f"
                  : "#e74c3c"
                : getStackColor(c.stackLevel);

            const strokeWidth = (2 + c.lanes * 1.2) * (isSelected ? 1.5 : 1.0);

            return (
              <g
                key={c.id}
                style={{ cursor: "pointer" }}
                onClick={() => selectCourseForProfile(c.id)}
                onMouseEnter={() => setHoveredCourse(c)}
                onMouseLeave={() => setHoveredCourse(null)}
              >
                {/* Bridge drop shadow for elevated tiers */}
                {c.stackLevel > 0 && (
                  <path
                    d={pathD}
                    fill="none"
                    stroke="#000000"
                    strokeWidth={strokeWidth + 4}
                    strokeLinecap="round"
                    filter="url(#bridgeShadow)"
                    opacity="0.8"
                  />
                )}

                {/* Base casing */}
                <path
                  d={pathD}
                  fill="none"
                  stroke={isSelected ? "#ffffff" : "#1e293b"}
                  strokeWidth={strokeWidth + (isSelected ? 3 : 1.5)}
                  strokeLinecap="round"
                />

                {/* Road surface / compliance or stack coloring */}
                <path
                  d={pathD}
                  fill="none"
                  stroke={strokeColor}
                  strokeWidth={strokeWidth}
                  strokeLinecap="round"
                  opacity={isHovered ? 1.0 : 0.88}
                />
              </g>
            );
          })}
      </svg>

      {/* Legend & Hover readout */}
      {colorMode === "compliance" ? (
        <div className={styles.visLegend}>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#2ecc71" }} />
            <span>Optimal (&lt;4% / R&gt;90m)</span>
          </div>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#f1c40f" }} />
            <span>Marginal</span>
          </div>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#e74c3c" }} />
            <span>Violation</span>
          </div>
        </div>
      ) : (
        <div className={styles.visLegend}>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#64748b" }} />
            <span>Tier 0 (Ground)</span>
          </div>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#38bdf8" }} />
            <span>Tier 1 (Overpass)</span>
          </div>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#a855f7" }} />
            <span>Tier 2 (Flyover)</span>
          </div>
          <div className={styles.legendItem}>
            <div className={styles.dot} style={{ background: "#f59e0b" }} />
            <span>Tier 3 (Upper)</span>
          </div>
        </div>
      )}

      {hoveredCourse && (
        <div style={{ fontSize: "10rem", color: "#38bdf8", marginTop: "4rem" }}>
          {hoveredCourse.roleName} | {hoveredCourse.lanes} Lane | Slope {hoveredCourse.maxGrade.toFixed(1)}% | R={hoveredCourse.minRadius.toFixed(0)}m | Tier {hoveredCourse.stackLevel}
        </div>
      )}
    </div>
  );
};
