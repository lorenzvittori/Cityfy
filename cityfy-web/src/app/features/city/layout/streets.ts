// Streets of the city, derived from the geometry (pure, no three.js).
//
// Every road runs along the border between two cells: the edge shared by two district outlines is
// the center line of a main road, the edge shared by two block cells of the same district is the
// center line of a street. Shared edges are found by probing just across each edge, then collinear
// pieces are merged into segments between intersections.
// On every segment: center line markings, zebra crossings near both ends, street lamps on the
// sidewalks, traffic lights (main roads) or stop/give-way signs (streets) at the ends.

import { PolygonIndex, hash01, insideConvex } from './geometry';
import { BlockPart, MAIN_ROAD, Point, RoadKind, RoadSegment, SIDEWALK, SignKind, Streets } from './layout-types';

const PROBE = 0.4;            // distance of the probe across an edge
const MIN_EDGE = 0.5;         // shorter edges are not roads
const LAMP_SPACING = 14;      // between lamps along a road (alternating sides)
const CROSSING_DEPTH = 1.6;   // zebra crossing size along the road

export interface RoadCell {
  poly: Point[];
  /** Cells of the same group are separated by roads of this level (e.g. blocks of one district). */
  group: number;
}

/** Center lines of the roads between cells of the same group (merged, between intersections). */
export function sharedEdges(cells: RoadCell[], width: number, kind: RoadKind): RoadSegment[] {
  const index = new PolygonIndex(cells.map((c, i) => ({ poly: c.poly, value: i })));
  const pieces: { a: Point; b: Point }[] = [];

  cells.forEach((cell, i) => {
    const poly = cell.poly;
    if (poly.length < 3) return;
    const cx = poly.reduce((s, p) => s + p.x, 0) / poly.length;
    const cz = poly.reduce((s, p) => s + p.z, 0) / poly.length;
    for (let k = 0; k < poly.length; k++) {
      const a = poly[k];
      const b = poly[(k + 1) % poly.length];
      const len = Math.hypot(b.x - a.x, b.z - a.z);
      if (len < MIN_EDGE) continue;
      let nx = (b.z - a.z) / len;
      let nz = -(b.x - a.x) / len;
      const mx = (a.x + b.x) / 2;
      const mz = (a.z + b.z) / 2;
      if (nx * (cx - mx) + nz * (cz - mz) > 0) [nx, nz] = [-nx, -nz]; // outwards
      const probe = { x: mx + nx * PROBE, z: mz + nz * PROBE };
      const neighbor = index.at(probe, -1e-6).some((j) => j !== i && cells[j].group === cell.group);
      if (neighbor) pieces.push({ a, b });
    }
  });

  return mergeCollinear(pieces).map((s) => ({ ...s, width, kind }));
}

/** Unites overlapping collinear pieces (each shared edge is seen from both sides). */
export function mergeCollinear(pieces: { a: Point; b: Point }[]): { a: Point; b: Point }[] {
  const lines = new Map<string, { ux: number; uz: number; c: number; intervals: [number, number][] }>();
  for (const { a, b } of pieces) {
    let ux = b.x - a.x;
    let uz = b.z - a.z;
    const len = Math.hypot(ux, uz);
    ux /= len;
    uz /= len;
    if (ux < -1e-9 || (Math.abs(ux) <= 1e-9 && uz < 0)) [ux, uz] = [-ux, -uz]; // one direction per line
    const c = -uz * a.x + ux * a.z; // offset along the normal
    const key = `${Math.round(Math.atan2(uz, ux) * 500)}|${Math.round(c * 10)}`;
    let line = lines.get(key);
    if (!line) lines.set(key, (line = { ux, uz, c, intervals: [] }));
    const ta = ux * a.x + uz * a.z;
    const tb = ux * b.x + uz * b.z;
    line.intervals.push([Math.min(ta, tb), Math.max(ta, tb)]);
  }

  const out: { a: Point; b: Point }[] = [];
  for (const { ux, uz, c, intervals } of lines.values()) {
    intervals.sort((p, q) => p[0] - q[0]);
    let [s, e] = intervals[0];
    const flush = () => {
      if (e - s >= MIN_EDGE)
        out.push({ a: { x: ux * s - uz * c, z: uz * s + ux * c }, b: { x: ux * e - uz * c, z: uz * e + ux * c } });
    };
    for (const [s2, e2] of intervals.slice(1)) {
      if (s2 <= e + 0.05) e = Math.max(e, e2);
      else {
        flush();
        [s, e] = [s2, e2];
      }
    }
    flush();
  }
  return out;
}

/** White paint on the asphalt: a thin rectangle (center, angle of its length, length, width). */
export interface Stripe {
  x: number;
  z: number;
  angle: number;
  length: number;
  width: number;
}

const DASH = 1.4;
const DASH_GAP = 1.4;
const LINE = 0.12;

/** Distance from the intersection center to the middle of the zebra crossing. */
const END_GAP = MAIN_ROAD / 2 + SIDEWALK + CROSSING_DEPTH / 2 + 0.4;

/**
 * Horizontal markings: double continuous center line on main roads, dashed center line on
 * streets (both stop before the crossings), zebra stripes across the road at the crossings.
 */
export function roadMarkings(streets: Streets): Stripe[] {
  const out: Stripe[] = [];
  for (const road of streets.roads) {
    const len = Math.hypot(road.b.x - road.a.x, road.b.z - road.a.z);
    if (len < 2 * END_GAP + 2) continue;
    const ux = (road.b.x - road.a.x) / len;
    const uz = (road.b.z - road.a.z) / len;
    const angle = Math.atan2(uz, ux);
    const from = END_GAP + CROSSING_DEPTH / 2 + 0.6;
    const to = len - from;
    if (to <= from) continue;
    const at = (t: number, off: number) => ({ x: road.a.x + ux * t - uz * off, z: road.a.z + uz * t + ux * off });

    if (road.kind === 'main') {
      for (const off of [-0.15, 0.15]) out.push({ ...at((from + to) / 2, off), angle, length: to - from, width: LINE });
    } else {
      for (let t = from; t + DASH <= to; t += DASH + DASH_GAP) out.push({ ...at(t + DASH / 2, 0), angle, length: DASH, width: LINE });
    }
  }

  // Zebra: stripes along the road, side by side across it
  for (const c of streets.crossings) {
    const count = Math.max(2, Math.floor((c.width - 0.4) / 0.8));
    const [px, pz] = [-Math.sin(c.angle), Math.cos(c.angle)];
    for (let k = 0; k < count; k++) {
      const off = (k - (count - 1) / 2) * 0.8;
      out.push({ x: c.x + px * off, z: c.z + pz * off, angle: c.angle, length: CROSSING_DEPTH, width: 0.42 });
    }
  }
  return out;
}

/**
 * Markings and furniture along the roads. Lamps, lights and signs are placed only where there is a
 * sidewalk (inside a block part, outside its plate).
 */
export function streetFurniture(roads: RoadSegment[], parts: BlockPart[]): Streets {
  const sidewalks = new PolygonIndex(parts.map((p) => ({ poly: p.outline, value: p })));
  const onSidewalk = (p: Point) => sidewalks.at(p).some((part) => !insideConvex(part.plate, p));

  const streets: Streets = { roads, crossings: [], lamps: [], trafficLights: [], signs: [] };

  roads.forEach((road, ri) => {
    const len = Math.hypot(road.b.x - road.a.x, road.b.z - road.a.z);
    const ux = (road.b.x - road.a.x) / len;
    const uz = (road.b.z - road.a.z) / len;
    const [lx, lz] = [-uz, ux]; // left normal
    const half = road.width / 2;
    const angle = Math.atan2(uz, ux);
    const at = (t: number, side: number, offset: number): Point => ({
      x: road.a.x + ux * t + lx * side * offset,
      z: road.a.z + uz * t + lz * side * offset,
    });
    const curb = half + SIDEWALK * 0.5;
    const endGap = END_GAP; // clear of the crossing road
    if (len < 2 * endGap + 2) return; // too short: just asphalt

    // Zebra crossings near both ends
    for (const t of [endGap, len - endGap]) {
      const c = at(t, 0, 0);
      streets.crossings.push({ x: c.x, z: c.z, angle, width: road.width });
    }

    // Lamps on alternate sides, facing the road
    let side = ri % 2 ? 1 : -1;
    for (let t = endGap + 3; t <= len - endGap - 3; t += LAMP_SPACING) {
      const p = at(t, side, curb);
      if (onSidewalk(p)) streets.lamps.push({ ...p, angle: Math.atan2(-lz * side, -lx * side) });
      side = -side;
    }

    // Ends: drivers keep right. Approaching b (direction +u) the right side is -left, and vice versa.
    const ends = [
      { t: len - endGap - CROSSING_DEPTH, side: -1, faces: angle + Math.PI },
      { t: endGap + CROSSING_DEPTH, side: 1, faces: angle },
    ];
    ends.forEach((end, k) => {
      const p = at(end.t, end.side, curb);
      if (!onSidewalk(p)) return;
      if (road.kind === 'main') {
        streets.trafficLights.push({ ...p, angle: end.faces });
      } else {
        const kind: SignKind = hash01(`${p.x.toFixed(1)},${p.z.toFixed(1)}`) < 0.5 ? 'stop' : 'yield';
        streets.signs.push({ ...p, angle: end.faces, kind });
      }
      // pedestrian crossing sign on the other side of main roads, next to the zebra
      if (road.kind === 'main') {
        const q = at(k === 0 ? len - endGap : endGap, -end.side, curb);
        if (onSidewalk(q)) streets.signs.push({ ...q, angle: end.faces, kind: 'crossing' });
      }
    });
  });

  return streets;
}
