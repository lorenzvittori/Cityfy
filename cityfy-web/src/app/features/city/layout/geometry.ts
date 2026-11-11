// 2D geometry on the ground plane (x, z) for the city templates. Polygons are convex, points in
// order (either orientation).

import { Point } from './layout-types';

/** Keeps the part of a convex polygon where nx*x + nz*z <= c. */
export function clipHalfPlane(poly: Point[], nx: number, nz: number, c: number): Point[] {
  const out: Point[] = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i];
    const b = poly[(i + 1) % poly.length];
    const da = nx * a.x + nz * a.z - c;
    const db = nx * b.x + nz * b.z - c;
    if (da <= 0) out.push(a);
    if ((da < 0 && db > 0) || (da > 0 && db < 0)) {
      const t = da / (da - db);
      out.push({ x: a.x + (b.x - a.x) * t, z: a.z + (b.z - a.z) * t });
    }
  }
  return out;
}

export function polygonArea(poly: Point[]): number {
  let sum = 0;
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i];
    const b = poly[(i + 1) % poly.length];
    sum += a.x * b.z - b.x * a.z;
  }
  return Math.abs(sum) / 2;
}

function vertexMean(poly: Point[]): Point {
  return { x: poly.reduce((s, p) => s + p.x, 0) / poly.length, z: poly.reduce((s, p) => s + p.z, 0) / poly.length };
}

/** Center of mass of a polygon. */
export function centroid(poly: Point[]): Point {
  let a = 0;
  let cx = 0;
  let cz = 0;
  for (let i = 0; i < poly.length; i++) {
    const p = poly[i];
    const q = poly[(i + 1) % poly.length];
    const cross = p.x * q.z - q.x * p.z;
    a += cross;
    cx += (p.x + q.x) * cross;
    cz += (p.z + q.z) * cross;
  }
  if (Math.abs(a) < 1e-9) return poly.length ? vertexMean(poly) : { x: 0, z: 0 };
  return { x: cx / (3 * a), z: cz / (3 * a) };
}

/** Edges as half-planes (outward normal, offset) of a convex polygon. */
export function edgesOf(poly: Point[]): { nx: number; nz: number; c: number }[] {
  const center = vertexMean(poly);
  const edges = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i];
    const b = poly[(i + 1) % poly.length];
    let nx = b.z - a.z;
    let nz = a.x - b.x;
    const len = Math.hypot(nx, nz);
    if (len < 1e-9) continue;
    nx /= len;
    nz /= len;
    if (nx * (center.x - a.x) + nz * (center.z - a.z) > 0) {
      nx = -nx; // make it point outwards
      nz = -nz;
    }
    edges.push({ nx, nz, c: nx * a.x + nz * a.z });
  }
  return edges;
}

/** Convex polygon shrunk by d on every side (empty when it vanishes). */
export function insetConvex(poly: Point[], d: number): Point[] {
  if (poly.length < 3) return [];
  let out = poly;
  for (const e of edgesOf(poly)) {
    out = clipHalfPlane(out, e.nx, e.nz, e.c - d);
    if (out.length < 3) return [];
  }
  return out;
}

export function insideConvex(poly: Point[], p: Point, eps = 1e-6): boolean {
  if (poly.length < 3) return false;
  return edgesOf(poly).every((e) => e.nx * p.x + e.nz * p.z <= e.c + eps);
}

/** Intersection of two convex polygons. */
export function intersectConvex(a: Point[], b: Point[]): Point[] {
  if (a.length < 3 || b.length < 3) return [];
  let out = a;
  for (const e of edgesOf(b)) {
    out = clipHalfPlane(out, e.nx, e.nz, e.c);
    if (out.length < 3) return [];
  }
  return out;
}

/**
 * Convex polygons indexed on a uniform grid of their bounding boxes: which polygons contain a point.
 * Edges are precomputed (thousands of point queries on thousands of polygons stay fast).
 */
export class PolygonIndex<T> {
  private readonly items: { poly: Point[]; edges: ReturnType<typeof edgesOf>; value: T }[] = [];
  private readonly buckets = new Map<string, number[]>();
  private readonly cell: number;

  constructor(entries: { poly: Point[]; value: T }[]) {
    const sizes = entries
      .filter((e) => e.poly.length >= 3)
      .map((e) => Math.max(...e.poly.map((p) => p.x)) - Math.min(...e.poly.map((p) => p.x)))
      .sort((a, b) => a - b);
    this.cell = Math.max(sizes[Math.floor(sizes.length / 2)] ?? 1, 1);
    for (const e of entries) {
      if (e.poly.length < 3) continue;
      const i = this.items.push({ poly: e.poly, edges: edgesOf(e.poly), value: e.value }) - 1;
      const xs = e.poly.map((p) => p.x);
      const zs = e.poly.map((p) => p.z);
      for (let cx = Math.floor(Math.min(...xs) / this.cell); cx <= Math.floor(Math.max(...xs) / this.cell); cx++)
        for (let cz = Math.floor(Math.min(...zs) / this.cell); cz <= Math.floor(Math.max(...zs) / this.cell); cz++) {
          const key = `${cx},${cz}`;
          const bucket = this.buckets.get(key);
          if (bucket) bucket.push(i);
          else this.buckets.set(key, [i]);
        }
    }
  }

  /** Values of the polygons containing p (strictly inside by eps when eps < 0). */
  at(p: Point, eps = 1e-6): T[] {
    const bucket = this.buckets.get(`${Math.floor(p.x / this.cell)},${Math.floor(p.z / this.cell)}`) ?? [];
    return bucket
      .map((i) => this.items[i])
      .filter((it) => it.edges.every((e) => e.nx * p.x + e.nz * p.z <= e.c + eps))
      .map((it) => it.value);
  }
}

/** Convex hull (monotone chain), counter-clockwise. */
export function convexHull(points: Point[]): Point[] {
  const pts = [...points].sort((a, b) => a.x - b.x || a.z - b.z);
  if (pts.length < 3) return pts;
  const cross = (o: Point, a: Point, b: Point) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
  const lower: Point[] = [];
  for (const p of pts) {
    while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop();
    lower.push(p);
  }
  const upper: Point[] = [];
  for (const p of pts.reverse()) {
    while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop();
    upper.push(p);
  }
  return [...lower.slice(0, -1), ...upper.slice(0, -1)];
}

/** Regular polygon around a center (circle approximation). */
export function circlePolygon(center: Point, r: number, sides = 32, start = 0): Point[] {
  return Array.from({ length: sides }, (_, k) => {
    const a = start + (k / sides) * 2 * Math.PI;
    return { x: center.x + r * Math.cos(a), z: center.z + r * Math.sin(a) };
  });
}

/** Square of side s around a point (last-resort block outline). */
export function squareAround(c: Point, side: number): Point[] {
  const h = side / 2;
  return [{ x: c.x - h, z: c.z - h }, { x: c.x + h, z: c.z - h }, { x: c.x + h, z: c.z + h }, { x: c.x - h, z: c.z + h }];
}

/**
 * Cell of site i in the power diagram of the sites, intersected with a convex polygon.
 * Power of p for site j: |p - c_j|^2 - r_j^2.
 */
export function powerCell(sites: { x: number; z: number; r: number }[], i: number, clip: Point[]): Point[] {
  let cell = clip;
  const a = sites[i];
  for (let j = 0; j < sites.length && cell.length >= 3; j++) {
    if (j === i) continue;
    const b = sites[j];
    // |p-a|^2 - ra^2 <= |p-b|^2 - rb^2  <=>  2(b-a).p <= |b|^2 - |a|^2 + ra^2 - rb^2
    const nx = b.x - a.x;
    const nz = b.z - a.z;
    if (Math.abs(nx) < 1e-9 && Math.abs(nz) < 1e-9) continue;
    const c = (b.x * b.x + b.z * b.z - a.x * a.x - a.z * a.z + a.r * a.r - b.r * b.r) / 2;
    cell = clipHalfPlane(cell, nx, nz, c);
  }
  return cell.length >= 3 ? cell : [];
}

// ---------------------------------------------------------------------------
// Area partition: recursive bisection of a convex polygon (treemap on polygons)
// ---------------------------------------------------------------------------

/** Family of parallel or rotating cut lines: s in [0,1] -> half-plane n.p <= c whose area grows with s. */
export type CutFamily = (poly: Point[]) => (s: number) => { nx: number; nz: number; c: number };

/** Axis-aligned cuts across the longest side: rectangles stay rectangles (grid city). */
export const axisCuts: CutFamily = (poly) => {
  const xs = poly.map((p) => p.x);
  const zs = poly.map((p) => p.z);
  const [minX, maxX, minZ, maxZ] = [Math.min(...xs), Math.max(...xs), Math.min(...zs), Math.max(...zs)];
  return maxX - minX >= maxZ - minZ
    ? (s) => ({ nx: 1, nz: 0, c: minX + s * (maxX - minX) })
    : (s) => ({ nx: 0, nz: 1, c: minZ + s * (maxZ - minZ) });
};

/** Cuts across the longest direction of the polygon (any angle): irregular but compact pieces. */
export const longestCuts: CutFamily = (poly) => {
  let best = { ux: 1, uz: 0, min: 0, max: 0 };
  for (let k = 0; k < 12; k++) {
    const a = (k / 12) * Math.PI;
    const ux = Math.cos(a);
    const uz = Math.sin(a);
    const proj = poly.map((p) => ux * p.x + uz * p.z);
    const [min, max] = [Math.min(...proj), Math.max(...proj)];
    if (max - min > best.max - best.min) best = { ux, uz, min, max };
  }
  return (s) => ({ nx: best.ux, nz: best.uz, c: best.min + s * (best.max - best.min) });
};

/**
 * Cuts seen from a center: rays from the center (angular split) or lines across the radius
 * (rings), whichever splits the longest dimension. The polygon must lie in a wedge < 180°.
 */
export function radialCuts(o: Point): CutFamily {
  return (poly) => {
    const c = centroid(poly);
    const bis = Math.atan2(c.z - o.z, c.x - o.x);
    const ux = Math.cos(bis);
    const uz = Math.sin(bis);
    let aMin = Infinity, aMax = -Infinity, dMin = Infinity, dMax = -Infinity;
    for (const p of poly) {
      let a = Math.atan2(p.z - o.z, p.x - o.x) - bis;
      a = Math.atan2(Math.sin(a), Math.cos(a)); // (-pi, pi]
      const d = ux * (p.x - o.x) + uz * (p.z - o.z);
      aMin = Math.min(aMin, a);
      aMax = Math.max(aMax, a);
      dMin = Math.min(dMin, d);
      dMax = Math.max(dMax, d);
    }
    const arc = ((dMin + dMax) / 2) * (aMax - aMin);
    if (arc >= dMax - dMin) {
      // ray at angle phi: keep the points before it (cross(dir, p - o) <= 0)
      return (s) => {
        const phi = bis + aMin + s * (aMax - aMin);
        const nx = -Math.sin(phi);
        const nz = Math.cos(phi);
        return { nx, nz, c: nx * o.x + nz * o.z };
      };
    }
    return (s) => ({ nx: ux, nz: uz, c: ux * o.x + uz * o.z + dMin + s * (dMax - dMin) });
  };
}

/**
 * Splits a convex polygon into convex pieces with areas proportional to the weights, keeping the
 * order (first weights on the side where the cuts start). Recursive halving: pieces stay compact.
 */
export function splitByAreas(poly: Point[], weights: number[], family: CutFamily): Point[][] {
  if (weights.length === 0) return [];
  if (weights.length === 1 || poly.length < 3) return weights.map(() => poly);

  const total = weights.reduce((s, w) => s + w, 0);
  let k = 1;
  let best = Infinity;
  let acc = 0;
  for (let i = 1; i < weights.length; i++) {
    acc += weights[i - 1];
    const diff = Math.abs(acc - total / 2);
    if (diff < best) {
      best = diff;
      k = i;
    }
  }
  const ratio = weights.slice(0, k).reduce((s, w) => s + w, 0) / total;

  const cut = family(poly);
  const full = polygonArea(poly);
  let lo = 0;
  let hi = 1;
  for (let iter = 0; iter < 40; iter++) {
    const mid = (lo + hi) / 2;
    const { nx, nz, c } = cut(mid);
    if (polygonArea(clipHalfPlane(poly, nx, nz, c)) / full < ratio) lo = mid;
    else hi = mid;
  }
  const { nx, nz, c } = cut((lo + hi) / 2);
  const a = clipHalfPlane(poly, nx, nz, c);
  const b = clipHalfPlane(poly, -nx, -nz, -c);
  return [
    ...splitByAreas(a.length >= 3 ? a : poly, weights.slice(0, k), family),
    ...splitByAreas(b.length >= 3 ? b : poly, weights.slice(k), family),
  ];
}

// ---------------------------------------------------------------------------
// Lots inside a block
// ---------------------------------------------------------------------------

export interface Lots {
  points: Point[];
  rotation: number;
}

/**
 * Grid of n lots in a convex block, aligned with its longest side, spread over the whole block
 * (spacing between pitch and 1.6 pitch), sorted center first.
 */
export function lotsInBlock(block: Point[], n: number, pitch: number, buildingWidth: number): Lots {
  let rotation = 0;
  let longest = -1;
  for (let i = 0; i < block.length; i++) {
    const a = block[i];
    const b = block[(i + 1) % block.length];
    const len = Math.hypot(b.x - a.x, b.z - a.z);
    if (len > longest) {
      longest = len;
      rotation = Math.atan2(b.z - a.z, b.x - a.x);
    }
  }
  const ux = Math.cos(rotation);
  const uz = Math.sin(rotation);
  const center = block.length >= 3 ? centroid(block) : { x: 0, z: 0 };
  const byDistance = (pts: Point[]) =>
    pts.sort((p, q) => Math.hypot(p.x - center.x, p.z - center.z) - Math.hypot(q.x - center.x, q.z - center.z));

  const grid = (step: number, ou: number, ov: number, area: Point[] | null, extent: number): Point[] => {
    const edges = area ? edgesOf(area) : [];
    const pts: Point[] = [];
    for (let i = -extent; i <= extent; i++)
      for (let j = -extent; j <= extent; j++) {
        const u = (i + ou) * step;
        const v = (j + ov) * step;
        const p = { x: center.x + u * ux - v * uz, z: center.z + u * uz + v * ux };
        if (edges.every((e) => e.nx * p.x + e.nz * p.z <= e.c + 1e-6)) pts.push(p);
      }
    return pts;
  };

  // 1. inside the block, widest spacing that still holds n lots: first with the building footprint
  //    fully inside, then (narrow blocks) with only the building center inside
  const radius = Math.max(0, ...block.map((p) => Math.hypot(p.x - center.x, p.z - center.z)));
  const minStep = buildingWidth * 1.05;
  for (const area of [insetConvex(block, buildingWidth / 2), block]) {
    if (area.length < 3) continue;
    let step = Math.min(pitch * 1.6, Math.max(pitch, Math.sqrt(polygonArea(area) / n) * 0.95));
    for (; step >= minStep - 1e-9; step *= 0.92) {
      const extent = Math.ceil(radius / step) + 1;
      let best: Point[] = [];
      for (const [ou, ov] of [[0, 0], [0.5, 0], [0, 0.5], [0.5, 0.5]]) {
        const pts = grid(step, ou, ov, area, extent);
        if (pts.length > best.length) best = pts;
      }
      if (best.length >= n) return { points: byDistance(best).slice(0, n), rotation };
    }
  }

  // 2. fallback (block too small): grid around the center, ignoring the outline
  const extent = Math.ceil(Math.sqrt(n)) + 1;
  return { points: byDistance(grid(pitch, 0, 0, null, extent)).slice(0, n), rotation };
}

/** Deterministic pseudo-random in [0, 1) from a string (stable across renders). */
export function hash01(text: string, salt = 0): number {
  let h = 2166136261 ^ salt;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  h = Math.imul(h ^ (h >>> 15), 2246822507);
  h = Math.imul(h ^ (h >>> 13), 3266489909);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
