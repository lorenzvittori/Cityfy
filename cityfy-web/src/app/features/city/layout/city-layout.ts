// City layout, pure (no three.js).
//   1. a template draws districts and genre blocks as convex cells up to the road center lines;
//   2. big genre blocks are split in sub-blocks by inner streets; every sub-block is shrunk by half
//      a street (main roads: by the district bound) and gets a sidewalk ring around its plate;
//   3. artists get a lot on the plates: grid aligned with the longest side, most listened in the middle;
//   4. roads are the borders shared by cells: markings, crossings, lamps, lights and signs along them.
// Every artist is placed exactly once (fallback: grid around the block center).

import { Block, City } from '../../../core/api/api.models';
import { centroid, insetConvex, intersectConvex, longestCuts, lotsInBlock, polygonArea, splitByAreas } from './geometry';
import { gridLayout } from './grid-layout';
import {
  BlockPart,
  CityLayout,
  CityTemplate,
  LOT_GAP,
  LayoutContext,
  MAIN_ROAD,
  PlacedBlock,
  PlacedBuilding,
  Point,
  SCALE,
  SIDEWALK,
  STREET,
  TemplateResult,
  subBlocks,
} from './layout-types';
import { organicLayout } from './organic-layout';
import { radialLayout } from './radial-layout';
import { RoadCell, sharedEdges, streetFurniture } from './streets';

export * from './layout-types';

export const CITY_TEMPLATES: { value: CityTemplate; label: string; description: string }[] = [
  { value: 'organic', label: 'Organica', description: 'Quartieri dalla forma irregolare, ognuno con la sua piazza e rotonda' },
  { value: 'grid', label: 'Griglia', description: 'Isolati rettangolari e strade dritte, come Manhattan' },
  { value: 'radial', label: 'Raggiera', description: 'Quartieri a spicchi attorno a una grande rotonda centrale' },
];

const TEMPLATES: Record<CityTemplate, (ctx: LayoutContext) => TemplateResult> = {
  organic: organicLayout,
  grid: gridLayout,
  radial: radialLayout,
};

export function isCityTemplate(value: unknown): value is CityTemplate {
  return typeof value === 'string' && value in TEMPLATES;
}

export function computeCityLayout(city: City, template: CityTemplate = 'organic'): CityLayout {
  // Only blocks with buildings, only districts with blocks
  const districts = city.districts
    .map((d) => ({ ...d, blocks: d.blocks.filter((b) => b.buildings.length > 0) }))
    .filter((d) => d.blocks.length > 0);
  if (districts.length === 0)
    return {
      template,
      buildings: [],
      districts: [],
      blocks: [],
      plazas: [],
      streets: { roads: [], crossings: [], lamps: [], trafficLights: [], signs: [] },
      bounds: { minX: 0, maxX: 0, minZ: 0, maxZ: 0 },
      width: 0,
      depth: 0,
    };

  // Lot size is fixed for the whole city (widest building), so no building overflows its lot
  const maxWidth = Math.max(1, ...districts.flatMap((d) => d.blocks.flatMap((b) => b.buildings.map((x) => x.width)))) * SCALE;
  const pitch = maxWidth + LOT_GAP;
  const ctx: LayoutContext = { districts, pitch, maxWidth, squareRadius: Math.max(3.5, pitch * 1.8) };

  const shape = TEMPLATES[template](ctx);

  // Sub-blocks, sidewalks and buildings
  const buildings: PlacedBuilding[] = [];
  const blocks: PlacedBlock[] = [];
  const streetCells: RoadCell[] = [];
  const bounds = new Map<Point[], number>(); // streets separate the cells inside one bound
  for (const tb of shape.blocks) {
    const group = bounds.get(tb.bound) ?? bounds.set(tb.bound, bounds.size).get(tb.bound)!;
    const n = tb.source.buildings.length;
    const k = subBlocks(n);
    const raws = k > 1 ? splitByAreas(tb.cell, new Array(k).fill(1), longestCuts) : [tb.cell];
    raws.forEach((poly) => streetCells.push({ poly, group }));

    const parts = raws.map((raw) => blockPart(raw, tb.bound));
    blocks.push({ blockId: tb.source.blockId, districtId: tb.districtId, name: tb.source.name, color: tb.color, cell: tb.cell, parts });
    buildings.push(...placeBuildings(tb.source, tb.districtId, tb.cell, parts, pitch, maxWidth));
  }

  // Roads: main roads between districts (+ template avenues), streets between blocks
  const mainCells: RoadCell[] = shape.districts.flatMap((d) => d.outlines.map((poly) => ({ poly, group: 0 })));
  const roads = [
    ...sharedEdges(mainCells, MAIN_ROAD, 'main'),
    ...(shape.avenues ?? []),
    ...sharedEdges(streetCells, STREET, 'street'),
  ];
  const streets = streetFurniture(roads, blocks.flatMap((b) => b.parts));

  // Bounds (camera framing, minimap, navigation limits)
  const points = shape.districts.flatMap((d) => d.outlines.flat());
  const xs = points.map((p) => p.x);
  const zs = points.map((p) => p.z);
  const box = { minX: Math.min(...xs), maxX: Math.max(...xs), minZ: Math.min(...zs), maxZ: Math.max(...zs) };
  return {
    template,
    buildings,
    districts: shape.districts,
    blocks,
    plazas: shape.plazas,
    streets,
    bounds: box,
    width: box.maxX - box.minX,
    depth: box.maxZ - box.minZ,
  };
}

/** Sub-block: half a street off the center lines, inside the district bound; sidewalk ring inside. */
function blockPart(raw: Point[], bound: Point[]): BlockPart {
  const shrunk = insetConvex(raw, STREET / 2);
  let outline = intersectConvex(shrunk, bound);
  if (outline.length < 3) outline = shrunk.length >= 3 ? shrunk : raw;
  const plate = insetConvex(outline, SIDEWALK);
  return { outline, plate: plate.length >= 3 ? plate : outline };
}

/** Buildings of a genre over its sub-blocks (by plate area), the part nearest the center first. */
function placeBuildings(source: Block, districtId: string, cell: Point[], parts: BlockPart[], pitch: number, maxWidth: number): PlacedBuilding[] {
  const n = source.buildings.length;
  const areas = parts.map((p) => polygonArea(p.plate));
  const counts = apportion(n, areas);
  const c = centroid(cell);
  const order = parts
    .map((_, i) => i)
    .sort((i, j) => {
      const a = centroid(parts[i].plate);
      const b = centroid(parts[j].plate);
      return Math.hypot(a.x - c.x, a.z - c.z) - Math.hypot(b.x - c.x, b.z - c.z);
    });

  const out: PlacedBuilding[] = [];
  let next = 0;
  for (const i of order) {
    const chunk = source.buildings.slice(next, next + counts[i]);
    next += counts[i];
    if (chunk.length === 0) continue;
    const lots = lotsInBlock(parts[i].plate, chunk.length, pitch, maxWidth);
    chunk.forEach((building, j) =>
      out.push({
        artistId: building.artistId,
        name: building.name,
        districtId,
        blockId: source.blockId,
        genreName: source.name,
        x: lots.points[j].x,
        z: lots.points[j].z,
        height: Math.max(building.height * SCALE, 0.2),
        width: building.width * SCALE,
        rotation: -lots.rotation, // grid angle in the x/z plane -> rotation around the y axis
        color: building.color,
      }),
    );
  }
  return out;
}

/** n items split proportionally to the weights (largest remainders), sum exactly n. */
export function apportion(n: number, weights: number[]): number[] {
  const total = weights.reduce((s, w) => s + w, 0);
  if (total <= 0) return weights.map((_, i) => (i === 0 ? n : 0));
  const exact = weights.map((w) => (n * w) / total);
  const counts = exact.map(Math.floor);
  const order = exact.map((_, i) => i).sort((i, j) => exact[j] - counts[j] - (exact[i] - counts[i]));
  for (let k = 0, left = n - counts.reduce((s, x) => s + x, 0); k < left; k++) counts[order[k % order.length]]++;
  return counts;
}
