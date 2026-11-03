import React from "react";
import { ModRegistrar } from "cs2/modding";
import { TopLeftButton } from "./components/TopLeftButton";
import { InterchangeStudioPanel, PanelErrorBoundary } from "./components/InterchangeStudioPanel";

const SafeStudioPanel: React.FC = () => (
  <PanelErrorBoundary>
    <InterchangeStudioPanel />
  </PanelErrorBoundary>
);

const register: ModRegistrar = (moduleRegistry) => {
  try {
    moduleRegistry.append("GameTopLeft", TopLeftButton);
    moduleRegistry.append("Game", SafeStudioPanel);
  } catch (e) {
    console.error("ProceduralInterchanges UI registration failed:", e);
  }
};

export default register;
