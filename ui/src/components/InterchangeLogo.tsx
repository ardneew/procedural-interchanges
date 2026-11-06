import React from "react";

export interface InterchangeLogoProps {
  className?: string;
  size?: number | string;
  style?: React.CSSProperties;
}

/**
 * Standardized Brand Logo for Procedural Interchanges
 * Concept: Dynamic 4-Way Directional Turbine / Vortex Interchange
 */
export const InterchangeLogo: React.FC<InterchangeLogoProps> = ({
  className,
  size,
  style,
}) => {
  return (
    <svg
      viewBox="0 0 48 48"
      fill="none"
      className={className}
      style={{
        width: size,
        height: size,
        display: "block",
        flexShrink: 0,
        ...style,
      }}
    >
      {/* Dual-tone directional flyover ramps */}
      <path
        d="M 24 6 C 24 16, 32 20, 42 20"
        stroke="#38bdf8"
        strokeWidth="3.5"
        strokeLinecap="round"
      />
      <path
        d="M 42 24 C 32 24, 28 32, 28 42"
        stroke="#818cf8"
        strokeWidth="3.5"
        strokeLinecap="round"
      />
      <path
        d="M 24 42 C 24 32, 16 28, 6 28"
        stroke="#38bdf8"
        strokeWidth="3.5"
        strokeLinecap="round"
      />
      <path
        d="M 6 24 C 16 24, 20 16, 20 6"
        stroke="#818cf8"
        strokeWidth="3.5"
        strokeLinecap="round"
      />
      {/* Central grade hub */}
      <circle cx="24" cy="24" r="3.5" fill="#f8fafc" />
    </svg>
  );
};
