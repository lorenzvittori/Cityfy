// City layout, pure (no three.js): ported from the city_generator prototype ("shelf packing"),
// extended to two levels:
//   city     = districts (macro-genres) packed in rows separated by main roads
//   district = blocks (genres) packed in rows separated by streets
//   block    = buildings (artists) on a square-ish grid of fixed lots
// Heights/widths come from the API (already scaled on the plays) and are multiplied by SCALE.

import { City } from '../../../core/api/api.models';

export const SCALE = 0.25;          // API units -> scene units (API height 4..40 -> 1..10)
export const LOT_GAP = 1;           // between buildings of a block
export const STREET = 2.5;          // between blocks of a district
export const MAIN_ROAD = 5;         // between districts
export const DISTRICT_MARGIN = 1.5; // ground plate border around a district

export interface PlacedBuilding {
  artistId: string;
  name: string;
  districtId: string;
  genreName: string;
  x: number;
  z: number;
  height: number;
  width: number;
  color: string;
}

export interface PlacedDistrict {
  districtId: string;
  name: string;
  color: string;
  x: number;
  z: number;
  width: number;
  depth: number;
}

export interface CityLayout {
  buildings: PlacedBuilding[];
  districts: PlacedDistrict[];
  width: number;
  depth: number;
}

interface Box {
  width: number;
  depth: number;
}

/**
 * Shelf packing: items left to right, a new row when the row would exceed maxRowWidth.
 * Returns the top-left corner of every item and the total size.
 */
export function packShelf<T extends Box>(items: T[], gap: number, maxRowWidth: number) {
  const placed: { item: T; x: number; z: number }[] = [];
  let x = 0;
  let z = 0;
  let rowDepth = 0;
  let width = 0;

  for (const item of items) {
    if (x > 0 && x + item.width > maxRowWidth) {
      z += rowDepth + gap;
      x = 0;
      rowDepth = 0;
    }
    placed.push({ item, x, z });
    x += item.width + gap;
    rowDepth = Math.max(rowDepth, item.depth);
    width = Math.max(width, x - gap);
  }

  return { placed, width, depth: items.length ? z + rowDepth : 0 };
}

/** Row width that makes the packing roughly square. */
function squareRowWidth(items: Box[], gap: number): number {
  const area = items.reduce((sum, i) => sum + (i.width + gap) * (i.depth + gap), 0);
  const widest = items.reduce((max, i) => Math.max(max, i.width), 0);
  return Math.max(widest, Math.sqrt(area) * 1.15);
}

export function computeCityLayout(city: City): CityLayout {
  // Lot size is fixed for the whole city (widest building), so no building overflows its lot
  const maxWidth = Math.max(1, ...city.districts.flatMap((d) => d.blocks.flatMap((b) => b.buildings.map((x) => x.width)))) * SCALE;
  const pitch = maxWidth + LOT_GAP;

  // 1. Blocks: grid of lots
  const districts = city.districts.map((d) => {
    const blocks = d.blocks.map((b) => {
      const cols = Math.ceil(Math.sqrt(b.buildings.length));
      const rows = Math.ceil(b.buildings.length / Math.max(cols, 1));
      return { block: b, cols, width: cols * pitch, depth: rows * pitch };
    });

    // 2. District: blocks packed with streets
    const packed = packShelf(blocks, STREET, squareRowWidth(blocks, STREET));
    return { district: d, packed, width: packed.width + 2 * DISTRICT_MARGIN, depth: packed.depth + 2 * DISTRICT_MARGIN };
  });

  // 3. City: districts packed with main roads, centered on the origin
  const city2 = packShelf(districts, MAIN_ROAD, squareRowWidth(districts, MAIN_ROAD));
  const offsetX = city2.width / 2;
  const offsetZ = city2.depth / 2;

  const placedBuildings: PlacedBuilding[] = [];
  const placedDistricts: PlacedDistrict[] = [];

  for (const { item: d, x: dx, z: dz } of city2.placed) {
    placedDistricts.push({
      districtId: d.district.districtId,
      name: d.district.name,
      color: d.district.color,
      x: dx + d.width / 2 - offsetX,
      z: dz + d.depth / 2 - offsetZ,
      width: d.width,
      depth: d.depth,
    });

    for (const { item: b, x: bx, z: bz } of d.packed.placed) {
      b.block.buildings.forEach((building, i) => {
        const col = i % b.cols;
        const row = Math.floor(i / b.cols);
        placedBuildings.push({
          artistId: building.artistId,
          name: building.name,
          districtId: d.district.districtId,
          genreName: b.block.name,
          // lot center = district corner + margin + block corner + lot offset
          x: dx + DISTRICT_MARGIN + bx + col * pitch + pitch / 2 - offsetX,
          z: dz + DISTRICT_MARGIN + bz + row * pitch + pitch / 2 - offsetZ,
          height: Math.max(building.height * SCALE, 0.2),
          width: building.width * SCALE,
          color: building.color,
        });
      });
    }
  }

  return { buildings: placedBuildings, districts: placedDistricts, width: city2.width, depth: city2.depth };
}
