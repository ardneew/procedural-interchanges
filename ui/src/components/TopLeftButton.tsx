import React from "react";
import { useValue } from "cs2/api";
import { FloatingButton } from "cs2/ui";
import { state$, toggleTool } from "../bindings/interchange-bindings";
import toolbarIcon from "../assets/toolbar-icon.svg";

export const TopLeftButton: React.FC = () => {
  const state = useValue(state$);
  const isActive = state?.toolActive ?? false;

  return (
    <FloatingButton
      style={{ flexShrink: 0 }}
      src={toolbarIcon}
      selected={isActive}
      onSelect={toggleTool}
      tooltipLabel="Procedural Interchanges Studio (Ctrl+Shift+I)"
    />
  );
};
