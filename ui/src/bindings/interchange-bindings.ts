import { bindValue, trigger } from "cs2/api";
import { InterchangeUIState } from "../types/interchange-types";

const GROUP = "proceduralInterchanges";

export const defaultState: InterchangeUIState = {
  toolActive: false,
  stage: 1,
  stageName: "PickCenter",
  center: { x: 0, y: 0, z: 0 },
  footprintRadius: 260,
  rotationDeg: 0,
  selectedArchetype: 20,
  maxMainlineGrade: 4.0,
  maxRampGrade: 6.0,
  minClearance: 9.5,
  designSpeed: 100,
  leftHandTraffic: false,
  gradeSeparateMainline: true,
  mainlineOverpass: false,
  liveGhostEnabled: true,
  overlayEnabled: true,
  canUndo: false,
  availableArchetypes: [],
  legs: [],
  courses2D: [],
  selectedCourseId: "",
  elevationProfile: [],
  telemetry: {
    estimatedCost: 0,
    worstGrade: 0,
    tightestRadius: 0,
    lowestClearance: 0,
    violationCount: 0,
  },
  availablePrefabs: [],
};

export const state$ = bindValue<InterchangeUIState>(GROUP, "state", defaultState);

export const toggleTool = () => trigger(GROUP, "toggleTool");
export const setStage = (stage: number) => trigger(GROUP, "setStage", stage);
export const autoDetectHighways = () => trigger(GROUP, "autoDetectHighways");
export const autoFitFootprint = () => trigger(GROUP, "autoFitFootprint");
export const removeLeg = (legIndex: number) => trigger(GROUP, "removeLeg", legIndex);
export const setArchetype = (archetypeId: number) => trigger(GROUP, "setArchetype", archetypeId);
export const setGlobalParam = (key: string, value: number) => trigger(GROUP, "setGlobalParam", key, value);
export const setGlobalPrefab = (role: string, prefabName: string) => trigger(GROUP, "setGlobalPrefab", role, prefabName);
export const setGlobalUpgrade = (flagBit: number, enabled: boolean) => trigger(GROUP, "setGlobalUpgrade", flagBit, enabled);
export const selectCourseForProfile = (courseId: string) => trigger(GROUP, "selectCourseForProfile", courseId);
export const setLegOverride = (legIndex: number, lanes: number, prefabName: string, upgradesMask: number) =>
  trigger(GROUP, "setLegOverride", legIndex, lanes, prefabName, upgradesMask);
export const setCourseOverride = (courseId: string, lanes: number, prefabName: string, upgradesMask: number) =>
  trigger(GROUP, "setCourseOverride", courseId, lanes, prefabName, upgradesMask);
export const buildInterchange = () => trigger(GROUP, "buildInterchange");
export const undoLastBuild = () => trigger(GROUP, "undoLastBuild");
export const setGhostEnabled = (enabled: boolean) => trigger(GROUP, "setGhostEnabled", enabled);
export const setOverlayEnabled = (enabled: boolean) => trigger(GROUP, "setOverlayEnabled", enabled);
