import React from "react";
import { ElevationSample } from "../types/interchange-types";
import styles from "./studio.module.scss";

interface Visualizer2DElevationProps {
  samples: ElevationSample[];
  selectedCourseId: string;
}

export const Visualizer2DElevation: React.FC<Visualizer2DElevationProps> = ({
  samples,
  selectedCourseId,
}) => {
  if (!samples || samples.length === 0) {
    return (
      <div className={styles.visWrapper} style={{ height: "90rem", justifyContent: "center" }}>
        <span style={{ fontSize: "11rem", color: "#64748b" }}>Select a road course above to view elevation profile</span>
      </div>
    );
  }

  const svgWidth = 380;
  const svgHeight = 110;
  const padding = 24;

  const maxDist = Math.max(10, samples[samples.length - 1].distance);
  let minY = Infinity;
  let maxY = -Infinity;

  for (let i = 0; i < samples.length; i++) {
    const s = samples[i];
    minY = Math.min(minY, s.terrainY, s.roadY);
    maxY = Math.max(maxY, s.terrainY, s.roadY);
  }

  // Add 4m margin top & bottom
  minY -= 3;
  maxY += 5;
  const rangeY = Math.max(6, maxY - minY);

  const getX = (dist: number) => padding + (dist / maxDist) * (svgWidth - padding * 2);
  const getY = (elev: number) => svgHeight - padding - ((elev - minY) / rangeY) * (svgHeight - padding * 2);

  // Build terrain SVG path
  let terrainD = `M ${getX(samples[0].distance).toFixed(1)},${getY(samples[0].terrainY).toFixed(1)}`;
  for (let i = 1; i < samples.length; i++) {
    terrainD += ` L ${getX(samples[i].distance).toFixed(1)},${getY(samples[i].terrainY).toFixed(1)}`;
  }
  const terrainAreaD = `${terrainD} L ${getX(samples[samples.length - 1].distance).toFixed(1)},${svgHeight - padding} L ${getX(samples[0].distance).toFixed(1)},${svgHeight - padding} Z`;

  // Build road deck SVG path
  let roadD = `M ${getX(samples[0].distance).toFixed(1)},${getY(samples[0].roadY).toFixed(1)}`;
  for (let i = 1; i < samples.length; i++) {
    roadD += ` L ${getX(samples[i].distance).toFixed(1)},${getY(samples[i].roadY).toFixed(1)}`;
  }

  // Find max clearance
  let maxClr = 0;
  let midClrDist = 0;
  let midClrRoadY = 0;
  let midClrTerrainY = 0;

  for (let i = 0; i < samples.length; i++) {
    if (samples[i].clearance > maxClr) {
      maxClr = samples[i].clearance;
      midClrDist = samples[i].distance;
      midClrRoadY = samples[i].roadY;
      midClrTerrainY = samples[i].terrainY;
    }
  }

  return (
    <div className={styles.visWrapper}>
      <svg width={svgWidth} height={svgHeight} viewBox={`0 0 ${svgWidth} ${svgHeight}`}>
        <defs>
          <linearGradient id="terrainGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#2c2a29" stopOpacity="0.8" />
            <stop offset="100%" stopColor="#121110" stopOpacity="0.9" />
          </linearGradient>
        </defs>

        {/* Axis lines */}
        <line x1={padding} y1={svgHeight - padding} x2={svgWidth - padding} y2={svgHeight - padding} stroke="rgba(255,255,255,0.15)" strokeWidth="1" />
        <line x1={padding} y1={padding} x2={padding} y2={svgHeight - padding} stroke="rgba(255,255,255,0.15)" strokeWidth="1" />

        {/* Terrain Area & Surface */}
        <path d={terrainAreaD} fill="url(#terrainGrad)" />
        <path d={terrainD} fill="none" stroke="#685848" strokeWidth="2" strokeDasharray="3,2" />

        {/* Road Surface Profile */}
        <path d={roadD} fill="none" stroke="#00e5ff" strokeWidth="2.5" />

        {/* Bridge clearance headroom bar if elevated */}
        {maxClr > 2.0 && (
          <g>
            <line
              x1={getX(midClrDist)}
              y1={getY(midClrTerrainY)}
              x2={getX(midClrDist)}
              y2={getY(midClrRoadY)}
              stroke="#2ecc71"
              strokeWidth="1.5"
              strokeDasharray="2,2"
            />
            <rect
              x={getX(midClrDist) + 4}
              y={(getY(midClrTerrainY) + getY(midClrRoadY)) / 2 - 8}
              width="44"
              height="16"
              fill="rgba(0,0,0,0.85)"
              rx="3"
            />
            <text
              x={getX(midClrDist) + 26}
              y={(getY(midClrTerrainY) + getY(midClrRoadY)) / 2 + 3}
              textAnchor="middle"
              fill="#2ecc71"
              fontSize="9"
              fontWeight="bold"
            >
              {maxClr.toFixed(1)}m
            </text>
          </g>
        )}

        {/* Axis labels */}
        <text x={padding} y={svgHeight - 6} fill="#64748b" fontSize="9">0m</text>
        <text x={svgWidth - padding} y={svgHeight - 6} textAnchor="end" fill="#64748b" fontSize="9">{maxDist.toFixed(0)}m</text>
        <text x={padding - 4} y={getY(minY + 3)} textAnchor="end" fill="#64748b" fontSize="8">{(minY + 3).toFixed(0)}m</text>
        <text x={padding - 4} y={getY(maxY - 3)} textAnchor="end" fill="#64748b" fontSize="8">{(maxY - 3).toFixed(0)}m</text>
      </svg>

      <div style={{ display: "flex", justifyContent: "space-between", width: "100%", fontSize: "10rem", color: "#94a3b8", padding: "0 6rem" }}>
        <span>Profile: <b style={{ color: "#00e5ff" }}>{selectedCourseId}</b></span>
        <span>Natural Ground vs Overpass Deck</span>
      </div>
    </div>
  );
};
