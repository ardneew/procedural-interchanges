import React from "react";
import { useValue } from "cs2/api";
import { state$, toggleTool } from "../bindings/interchange-bindings";
import styles from "./studio.module.scss";

export const TopLeftButton: React.FC = () => {
  const state = useValue(state$);
  const isActive = state?.toolActive ?? false;

  return (
    <div
      className={`${styles.launcherButton} ${isActive ? styles.active : ""}`}
      onClick={toggleTool}
      title="Procedural Interchanges Studio (Ctrl+Shift+I)"
    >
      <svg viewBox="0 0 24 24">
        {/* Interchange interchange clover/diamond stylized icon */}
        <circle cx="12" cy="12" r="3" stroke="#00e5ff" strokeWidth="2" />
        <path d="M 2 12 H 9 M 15 12 H 22" stroke="#ffffff" strokeWidth="2" />
        <path d="M 12 2 V 9 M 12 15 V 22" stroke="#ffffff" strokeWidth="2" />
        <path d="M 5 5 C 9 5, 9 9, 5 9" stroke="#00e5ff" strokeWidth="1.5" />
        <path d="M 19 19 C 15 19, 15 15, 19 15" stroke="#00e5ff" strokeWidth="1.5" />
      </svg>
    </div>
  );
};
