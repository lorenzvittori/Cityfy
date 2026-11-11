// Template "Organica": a Voronoi treemap. The city is an irregular polygon split into the cells of
// a power diagram (weighted Voronoi) whose weights are tuned until every district cell has the area
// it needs: irregular districts that tile the city, main roads between them, no empty ground.
// Inside a district: a square with a roundabout at the center, the blocks around it in wedges
// (avenues radiating from the square), each wedge split again into blocks by rays and rings.

import {
  centroid,
  clipHalfPlane,
  convexHull,
  hash01,
  insetConvex,
  longestCuts,
  polygonArea,
  powerCell,
  radialCuts,
  splitByAreas,
} from './geometry';
import { LayoutContext, MAIN_ROAD, Point, STREET, TemplateResult, blockArea } from './layout-types';

const GOLDEN_ANGLE = Math.PI * (3 - Math.sqrt(5));
const MAX_WEDGE = (170 / 180) * Math.PI; // wider wedges would not be convex
const MAX_WEDGES = 6;

export function organicLayout(ctx: LayoutContext): TemplateResult {
  const squareArea = Math.PI * (ctx.squareRadius + STREET) ** 2;
  const blockAreas = ctx.districts.map((d) => d.blocks.map((b) => blockArea(b.buildings.length, ctx)));
  const targets = blockAreas.map((areas) => (Math.sqrt(areas.reduce((s, a) => s + a, 0) + squareArea) + MAIN_ROAD) ** 2);

  // 1. City outline: irregular convex polygon with the total area
  const total = targets.reduce((s, a) => s + a, 0);
  const seed = ctx.districts.map((d) => d.districtId).join('|');
  const boundary = cityOutline(Math.sqrt(total / Math.PI), seed);

  // 2. District cells: Voronoi treemap
  const cells = voronoiTreemap(boundary, targets);

  const result: TemplateResult = { districts: [], blocks: [], plazas: [] };
  ctx.districts.forEach((d, di) => {
    const outline = cells[di];
    const center = centroid(outline);
    const inner = insetConvex(outline, MAIN_ROAD / 2);
    const bound = inner.length >= 3 ? inner : outline;
    result.districts.push({
      districtId: d.districtId,
      name: d.name,
      color: d.color,
      x: center.x,
      z: center.z,
      radius: Math.sqrt(polygonArea(outline) / Math.PI),
      outlines: [outline],
    });

    // 3. Blocks (cells up to the street center lines): wedges around the square, or (few/very
    //    unequal blocks) plain split of the district
    const pieces = blocksAroundSquare(outline, center, blockAreas[di], ctx.squareRadius, hash01(d.districtId) * 2 * Math.PI);
    if (pieces) result.plazas.push({ x: center.x, z: center.z, radius: ctx.squareRadius });
    const blockCells = pieces ?? splitByAreas(outline, blockAreas[di], longestCuts);
    d.blocks.forEach((b, bi) => result.blocks.push({ source: b, districtId: d.districtId, color: d.color, cell: blockCells[bi], bound }));
  });

  return result;
}

/** Irregular convex outline of the city (hull of a jittered circle), about the requested radius. */
function cityOutline(radius: number, seed: string): Point[] {
  const sides = 14;
  const pts = Array.from({ length: sides }, (_, k) => {
    const a = ((k + (hash01(seed, k) - 0.5) * 0.5) / sides) * 2 * Math.PI;
    const r = radius * (0.92 + 0.22 * hash01(seed, 100 + k));
    return { x: r * Math.cos(a), z: r * Math.sin(a) };
  });
  return convexHull(pts);
}

/**
 * Cells of a power diagram inside the boundary with areas proportional to the targets: sites move to
 * the centroid of their cell and the weights grow/shrink with the area error (Nocaj & Brandes).
 * The first targets end up near the center. Deterministic.
 */
export function voronoiTreemap(boundary: Point[], targets: number[]): Point[][] {
  const n = targets.length;
  if (n === 1) return [boundary];
  const area = polygonArea(boundary);
  const total = targets.reduce((s, a) => s + a, 0);
  const wanted = targets.map((t) => (t / total) * area);
  const c0 = centroid(boundary);
  const radius = Math.sqrt(area / Math.PI);

  // biggest districts first on a sunflower spiral (center), weights proportional to the area
  const order = targets.map((_, i) => i).sort((a, b) => targets[b] - targets[a]);
  const sites = new Array<{ x: number; z: number; r: number }>(n);
  order.forEach((i, k) => {
    const rho = radius * 0.75 * Math.sqrt((k + 0.5) / n);
    const a = k * GOLDEN_ANGLE;
    sites[i] = { x: c0.x + rho * Math.cos(a), z: c0.z + rho * Math.sin(a), r: 0 };
  });
  let weights = wanted.map((w) => (w / Math.PI) * 0.2);

  let cells: Point[][] = [];
  for (let iter = 0; iter < 120; iter++) {
    // weights must not swallow a neighbor's site: w_i <= w_j + d_ij^2
    for (let i = 0; i < n; i++)
      for (let j = 0; j < n; j++)
        if (i !== j) {
          const d2 = (sites[i].x - sites[j].x) ** 2 + (sites[i].z - sites[j].z) ** 2;
          weights[i] = Math.min(weights[i], weights[j] + d2 * 0.98);
        }
    const minW = Math.min(...weights);
    weights = weights.map((w) => w - minW); // power diagram unchanged by a common shift
    sites.forEach((s, i) => (s.r = Math.sqrt(weights[i])));

    cells = sites.map((_, i) => powerCell(sites, i, boundary));
    for (let i = 0; i < n; i++) {
      const a = cells[i].length >= 3 ? polygonArea(cells[i]) : 0;
      if (a > 0) {
        const c = centroid(cells[i]);
        sites[i].x = c.x;
        sites[i].z = c.z;
      }
      // grow the weight (circle) of a cell that is too small, shrink one that is too big
      const current = Math.sqrt(Math.max(a, wanted[i] * 0.05) / Math.PI);
      const target = Math.sqrt(wanted[i] / Math.PI);
      const r = Math.sqrt(weights[i]) + (target - current) * 0.8;
      weights[i] = Math.max(r, 0) ** 2;
    }
  }
  // a cell may vanish only in pathological cases: give it a small piece of its biggest neighbor
  return cells.map((cell, i) => (cell.length >= 3 ? cell : splitFallback(cells, sites[i])));
}

function splitFallback(cells: Point[][], site: Point): Point[] {
  const host = cells.reduce((best, c) => (polygonArea(c) > polygonArea(best) ? c : best), cells[0]);
  const c = centroid(host);
  return clipHalfPlane(host, site.x - c.x || 1, site.z - c.z, (site.x - c.x || 1) * c.x + (site.z - c.z) * c.z);
}

/**
 * Blocks in wedges around a central square. Returns null when it does not fit (fewer than 3
 * blocks, or a block so big its wedge would be wider than 170°).
 */
function blocksAroundSquare(area: Point[], o: Point, weights: number[], squareRadius: number, start: number): Point[][] | null {
  if (weights.length < 3) return null;
  const groups = contiguousGroups(weights, Math.min(weights.length, MAX_WEDGES));
  if (groups.length < 3) return null;
  const groupWeights = groups.map((g) => g.reduce((s, i) => s + weights[i], 0));

  const angles = angularSplit(area, o, groupWeights, start);
  if (!angles) return null;

  const cells: Point[][] = new Array(weights.length);
  for (let k = 0; k < groups.length; k++) {
    const [a0, a1] = [angles[k], angles[k + 1]];
    const bis = (a0 + a1) / 2;
    const ux = Math.cos(bis);
    const uz = Math.sin(bis);
    // wedge, minus the square (half-plane across the bisector) and half a street on the sides
    let wedge = clipWedge(area, o, a0, a1);
    wedge = clipHalfPlane(wedge, -ux, -uz, -(ux * o.x + uz * o.z + squareRadius + STREET / 2));
    if (wedge.length < 3) return null;
    const pieces = splitByAreas(wedge, groups[k].map((i) => weights[i]), radialCuts(o));
    groups[k].forEach((bi, j) => (cells[bi] = pieces[j]));
  }
  return cells;
}

/** Part of a convex polygon between two rays from o (a1 - a0 <= 180°). */
function clipWedge(poly: Point[], o: Point, a0: number, a1: number): Point[] {
  const d0 = { x: Math.cos(a0), z: Math.sin(a0) };
  const d1 = { x: Math.cos(a1), z: Math.sin(a1) };
  // after the first ray: cross(d0, p - o) >= 0 ; before the second: cross(d1, p - o) <= 0
  const first = clipHalfPlane(poly, d0.z, -d0.x, d0.z * o.x - d0.x * o.z);
  return clipHalfPlane(first, -d1.z, d1.x, -d1.z * o.x + d1.x * o.z);
}

/** Angles (from start, increasing, last = start + 2pi) of wedges with areas proportional to the weights. */
function angularSplit(poly: Point[], o: Point, weights: number[], start: number): number[] | null {
  const full = polygonArea(poly);
  const total = weights.reduce((s, w) => s + w, 0);
  const areaUpTo = (phi: number) =>
    phi - start <= Math.PI ? polygonArea(clipWedge(poly, o, start, phi)) : full - polygonArea(clipWedge(poly, o, phi, start + 2 * Math.PI));

  const angles = [start];
  let acc = 0;
  for (let k = 0; k < weights.length - 1; k++) {
    acc += weights[k];
    const target = (acc / total) * full;
    let lo = angles[k];
    let hi = start + 2 * Math.PI;
    for (let iter = 0; iter < 40; iter++) {
      const mid = (lo + hi) / 2;
      if (areaUpTo(mid) < target) lo = mid;
      else hi = mid;
    }
    angles.push((lo + hi) / 2);
  }
  angles.push(start + 2 * Math.PI);
  for (let k = 0; k < weights.length; k++) if (angles[k + 1] - angles[k] > MAX_WEDGE) return null;
  return angles;
}

/** Indexes 0..n-1 in `count` contiguous groups of similar weight (none empty). */
function contiguousGroups(weights: number[], count: number): number[][] {
  const total = weights.reduce((s, w) => s + w, 0);
  const groups: number[][] = [];
  let current: number[] = [];
  let acc = 0;
  weights.forEach((w, i) => {
    const left = weights.length - i; // items not yet assigned, this one included
    const groupsLeft = count - groups.length;
    if (current.length > 0 && groupsLeft > 1 && (acc + w / 2 > (total * (groups.length + 1)) / count || left < groupsLeft)) {
      groups.push(current);
      current = [];
    }
    current.push(i);
    acc += w;
  });
  groups.push(current);
  return groups;
}
