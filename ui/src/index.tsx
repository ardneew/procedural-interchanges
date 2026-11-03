import { ModRegistrar } from "cs2/modding";
import { TopLeftButton } from "./components/TopLeftButton";
import { InterchangeStudioPanel } from "./components/InterchangeStudioPanel";

const register: ModRegistrar = (moduleRegistry) => {
  try {
    moduleRegistry.append("GameTopLeft", TopLeftButton);
    moduleRegistry.append("Game", InterchangeStudioPanel);
  } catch (e) {
    console.error("ProceduralInterchanges UI registration failed:", e);
  }
};

export default register;