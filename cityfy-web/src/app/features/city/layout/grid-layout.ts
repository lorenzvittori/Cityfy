// Template "Griglia": a rectangular city of straight streets, like Manhattan.
// The city rectangle is split into districts with areas proportional to their buildings
// (treemap by recursive halving, cuts across the longest side), each district into its blocks the
// same way. Main roads between districts, streets between blocks: no empty ground.

import { axisCuts, centroid, insetConvex, polygonArea, splitByAreas } from './geometry';
import { LayoutContext, MAIN_ROAD, Point, TemplateResult, blockArea, districtArea } from './layout-types';

const ASPECT = 1.3; // width / depth of the city

export function gridLayout(ctx: LayoutContext): TemplateResult {
  // District areas with their ring of main road
  const areas = ctx.districts.map((d) => (Math.sqrt(districtArea(d, ctx)) + MAIN_ROAD) ** 2);
  const total = areas.reduce((s, a) => s + a, 0);
  const width = Math.sqrt(total * ASPECT);
  const depth = total / width;
  const city: Point[] = [
    { x: -width / 2, z: -depth / 2 },
    { x: width / 2, z: -depth / 2 },
    { x: width / 2, z: depth / 2 },
    { x: -width / 2, z: depth / 2 },
  ];

  const result: TemplateResult = { districts: [], blocks: [], plazas: [] };
  const pieces = splitByAreas(city, areas, axisCuts);

  ctx.districts.forEach((d, di) => {
    const outline = pieces[di];
    const c = centroid(outline);
    result.districts.push({
      districtId: d.districtId,
      name: d.name,
      color: d.color,
      x: c.x,
      z: c.z,
      radius: Math.sqrt(polygonArea(outline) / Math.PI),
      outlines: [outline],
    });

    // block cells up to the street center lines, buildable inside the main roads
    const inner = insetConvex(outline, MAIN_ROAD / 2);
    const cells = splitByAreas(outline, d.blocks.map((b) => blockArea(b.buildings.length, ctx)), axisCuts);
    d.blocks.forEach((b, bi) =>
      result.blocks.push({ source: b, districtId: d.districtId, color: d.color, cell: cells[bi], bound: inner.length >= 3 ? inner : outline }),
    );
  });

  return result;
}
