// Map of a generated city, computed by the backend (ServiceDefault/City/CityLayoutContracts.cs):
// scene units on the ground plane (x, z). The frontend only draws it.

export interface Point {
  x: number;
  z: number;
}

export interface PlacedBuilding {
  artistId: string;
  name: string;
  districtId: string;
  blockId: string;
  genreName: string;
  x: number;
  z: number;
  height: number;
  width: number;
  /** Rotation around the vertical axis (radians): buildings follow the block grid. */
  rotation: number;
  color: string;
}

export interface PlacedDistrict {
  districtId: string;
  name: string;
  color: string;
  /** Where the name is shown. */
  x: number;
  z: number;
  /** Approximate size (label scale). */
  radius: number;
  /** Ground of the district including half of the surrounding main road (asphalt), convex pieces. */
  outlines: Point[][];
}

/** A sub-block: sidewalk ring (outline) around the plate the buildings stand on. */
export interface BlockPart {
  outline: Point[];
  plate: Point[];
}

export interface PlacedBlock {
  blockId: string;
  districtId: string;
  name: string;
  color: string;
  cell: Point[];
  parts: BlockPart[];
}

/** Roundabout: green island in the middle of a square. */
export interface Plaza {
  x: number;
  z: number;
  radius: number;
}

export type RoadKind = 'main' | 'street';

export interface RoadSegment {
  a: Point;
  b: Point;
  width: number;
  kind: RoadKind;
}

export type SignKind = 'stop' | 'yield' | 'crossing';

/** Street furniture: position on the sidewalk, angle = direction it faces (radians, x/z plane). */
export interface Furniture {
  x: number;
  z: number;
  angle: number;
}

/** White paint on the asphalt: a thin rectangle (center, angle of its length, length, width). */
export interface Stripe {
  x: number;
  z: number;
  angle: number;
  length: number;
  width: number;
}

export interface Streets {
  roads: RoadSegment[];
  crossings: (Furniture & { width: number })[];
  lamps: Furniture[];
  trafficLights: Furniture[];
  signs: (Furniture & { kind: SignKind })[];
  markings: Stripe[];
}

export interface CityMap {
  template: string;
  buildings: PlacedBuilding[];
  districts: PlacedDistrict[];
  blocks: PlacedBlock[];
  plazas: Plaza[];
  streets: Streets;
  bounds: { minX: number; maxX: number; minZ: number; maxZ: number };
  width: number;
  depth: number;
}
