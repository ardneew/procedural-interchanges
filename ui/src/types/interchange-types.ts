export interface Point3D {
  x: number;
  y: number;
  z: number;
}

export interface Point2D {
  x: number;
  y: number;
}

export interface ArchetypeInfo {
  id: number;
  name: string;
  description: string;
  minLegs: number;
  maxLegs: number;
  supported: boolean;
  reason: string;
}

export interface LegInfo {
  index: number;
  azimuthDeg: number;
  isDual: boolean;
  separation: number;
  inboundLanes: number;
  outboundLanes: number;
  overrideLanes: number;
  overridePrefab: string;
  upgrades: number;
}

export interface Course2D {
  id: string;
  role: number;
  roleName: string;
  stackLevel: number;
  lanes: number;
  compliance: number; // 0 = Optimal, 1 = Marginal, 2 = Violation
  complianceMessage: string;
  maxGrade: number;
  minRadius: number;
  minClearance: number;
  prefabName: string;
  p0x: number;
  p0y: number;
  p1x: number;
  p1y: number;
  p2x: number;
  p2y: number;
  p3x: number;
  p3y: number;
}

export interface ElevationSample {
  distance: number;
  roadY: number;
  terrainY: number;
  grade: number;
  clearance: number;
}

export interface Telemetry {
  estimatedCost: number;
  worstGrade: number;
  tightestRadius: number;
  lowestClearance: number;
  violationCount: number;
}

export interface InterchangeUIState {
  toolActive: boolean;
  stage: number; // 1 = PickCenter, 2 = SelectHighways, 3 = ConfigureInterchange, 4 = Building
  stageName: string;
  center: Point3D;
  footprintRadius: number;
  rotationDeg: number;
  selectedArchetype: number;
  maxMainlineGrade: number;
  maxRampGrade: number;
  minClearance: number;
  designSpeed: number;
  leftHandTraffic: boolean;
  gradeSeparateMainline: boolean;
  mainlineOverpass: boolean;
  liveGhostEnabled: boolean;
  overlayEnabled: boolean;
  canUndo: boolean;
  requiredLegs: number;
  angleWarning: string;
  availableArchetypes: ArchetypeInfo[];
  legs: LegInfo[];
  courses2D: Course2D[];
  selectedCourseId: string;
  elevationProfile: ElevationSample[];
  telemetry: Telemetry;
  availablePrefabs: string[];
}
