// Types and constants shared by the city templates (pure, no three.js).

import { Block, District } from '../../../core/api/api.models';

export const SCALE = 0.25;          // API units -> scene units (API height 4..40 -> 1..10)
export const LOT_GAP = 1;           // between buildings of a block
export const STREET = 3;            // street between blocks (two lanes), sidewalks excluded
export const MAIN_ROAD = 6;         // main road between districts
export const SIDEWALK = 0.7;        // sidewalk around every block (inside the block outline)
export const CURB = 0.15;           // sidewalk height: buildings stand on it
export const SUBBLOCK_MAX = 16;     // bigger genre blocks are split in sub-blocks by inner streets

export type CityTemplate = 'organic' | 'grid' | 'radial';

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
  /** Ground of the genre, up to the middle of the surrounding streets. */
  cell: Point[];
  /** Sub-blocks separated by inner streets (one when the genre is small). */
  parts: BlockPart[];
}

/** Roundabout: green island in the middle of a square. */
export interface Plaza {
  x: number;
  z: number;
  radius: number;
}

export type RoadKind = 'main' | 'street';

/** Center line of a road between two intersections. */
export interface RoadSegment {
  a: Point;
  b: Point;
  width: number;
  kind: RoadKind;
}

export type SignKind = 'stop' | 'yield' | 'crossing';

/** Street furniture item: position on the sidewalk, angle = direction it faces (radians, x/z plane). */
export interface Furniture {
  x: number;
  z: number;
  angle: number;
}

export interface Streets {
  roads: RoadSegment[];
  /** Zebra crossings: center, angle of the road, width of the road. */
  crossings: (Furniture & { width: number })[];
  lamps: Furniture[];
  trafficLights: Furniture[];
  signs: (Furniture & { kind: SignKind })[];
}

export interface CityLayout {
  template: CityTemplate;
  buildings: PlacedBuilding[];
  districts: PlacedDistrict[];
  blocks: PlacedBlock[];
  plazas: Plaza[];
  streets: Streets;
  bounds: { minX: number; maxX: number; minZ: number; maxZ: number };
  width: number;
  depth: number;
}

/** Input of a template: districts/blocks with at least one building, sizes of the lots. */
export interface LayoutContext {
  districts: District[];
  pitch: number;
  maxWidth: number;
  squareRadius: number;
}

/**
 * A block drawn by a template: its cell goes up to the center line of the surrounding streets;
 * bound = buildable part of the district (main roads excluded), convex.
 */
export interface TemplateBlock {
  source: Block;
  districtId: string;
  color: string;
  cell: Point[];
  bound: Point[];
}

/** Output of a template: the shapes; sub-blocks, sidewalks, buildings and streets come afterwards. */
export interface TemplateResult {
  districts: PlacedDistrict[];
  blocks: TemplateBlock[];
  plazas: Plaza[];
  /** Roads that are not the border between two cells (e.g. the avenues of the radial city). */
  avenues?: RoadSegment[];
}

export function subBlocks(n: number): number {
  return Math.ceil(n / SUBBLOCK_MAX);
}

/** Ground to reserve for a block of n buildings: lots, sidewalks, its share of streets, inner streets. */
export function blockArea(n: number, ctx: LayoutContext): number {
  const k = subBlocks(n);
  const lots = Math.sqrt(n * ctx.pitch * ctx.pitch * 1.15) + ctx.maxWidth * 0.5;
  const side = lots + 2 * SIDEWALK * Math.sqrt(k) + STREET;
  const inner = 2 * (Math.sqrt(k) - 1) * STREET * side;
  return side * side + inner;
}

export function districtArea(d: District, ctx: LayoutContext): number {
  return d.blocks.reduce((sum, b) => sum + blockArea(b.buildings.length, ctx), 0);
}
