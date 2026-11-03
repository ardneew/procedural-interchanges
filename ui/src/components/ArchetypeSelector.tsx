import React from "react";
import { ArchetypeInfo } from "../types/interchange-types";
import { setArchetype } from "../bindings/interchange-bindings";
import styles from "./studio.module.scss";

interface ArchetypeSelectorProps {
  archetypes: ArchetypeInfo[];
  selectedId: number;
}

export const ArchetypeSelector: React.FC<ArchetypeSelectorProps> = ({
  archetypes,
  selectedId,
}) => {
  return (
    <div className={styles.archetypeGrid}>
      {archetypes.map((arch) => {
        const isSelected = arch.id === selectedId;
        const isDisabled = !arch.supported;

        return (
          <div
            key={arch.id}
            className={`${styles.archetypeCard} ${isSelected ? styles.selected : ""} ${
              isDisabled ? styles.disabled : ""
            }`}
            onClick={() => {
              if (!isDisabled) setArchetype(arch.id);
            }}
            title={arch.supported ? arch.description : arch.reason}
          >
            {/* Minimal SVG icon representing junction geometry */}
            <svg className={styles.cardIcon} viewBox="0 0 40 40">
              <circle cx="20" cy="20" r="17" fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="1" />
              {arch.minLegs === 3 ? (
                // 3-way icon
                <g stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="2" strokeLinecap="round">
                  <line x1="6" y1="28" x2="34" y2="28" />
                  <line x1="20" y1="6" x2="20" y2="28" />
                  {arch.id === 10 && <circle cx="27" cy="18" r="5" fill="none" stroke={isSelected ? "#7dd3fc" : "#cbd5e1"} strokeWidth="1.5" />}
                </g>
              ) : arch.id === 30 ? (
                // Roundabout icon
                <g stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="2" strokeLinecap="round">
                  <circle cx="20" cy="20" r="8" fill="none" stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="2" />
                  <line x1="4" y1="20" x2="12" y2="20" />
                  <line x1="28" y1="20" x2="36" y2="20" />
                  <line x1="20" y1="4" x2="20" y2="12" />
                  <line x1="20" y1="28" x2="20" y2="36" />
                </g>
              ) : (
                // 4-way cross icon with flyover/loop
                <g stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="2" strokeLinecap="round">
                  <line x1="4" y1="20" x2="36" y2="20" />
                  <line x1="20" y1="4" x2="20" y2="36" stroke={isSelected ? "#f8fafc" : "#cbd5e1"} strokeWidth="2.5" />
                  {arch.id === 22 && (
                    <>
                      <circle cx="13" cy="13" r="4" fill="none" stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="1" />
                      <circle cx="27" cy="13" r="4" fill="none" stroke={isSelected ? "#38bdf8" : "#94a3b8"} strokeWidth="1" />
                    </>
                  )}
                </g>
              )}
            </svg>

            <span className={styles.cardTitle}>{arch.name}</span>
            <span className={styles.cardBadge}>
              {arch.minLegs === arch.maxLegs ? `${arch.minLegs}-Way` : `${arch.minLegs}–${arch.maxLegs} Legs`}
            </span>
          </div>
        );
      })}
    </div>
  );
};
