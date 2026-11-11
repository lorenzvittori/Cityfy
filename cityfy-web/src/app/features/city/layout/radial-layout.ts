// Template "Raggiera": districts are slices around a big central roundabout, like a star.
// Each district gets an angle proportional to its buildings (with a minimum, so small districts are
// not slivers) and a length that gives it the ground it needs: the outline is a flower of slices of
// different length. Slices wider than 60° are split in pieces (radial avenues between them).
// Inside a slice the blocks are cut by rays from the center and by rings, whichever splits the
// longest side: avenues radiate from the roundabout, ring roads cross them.

import { District } from '../../../core/api/api.models';
import { clipHalfPlane, insetConvex, radialCuts, splitByAreas } from './geometry';
import { LayoutContext, MAIN_ROAD, Point, RoadSegment, STREET, TemplateResult, blockArea } from './layout-types';

const MIN_SHARE = 0.04;          // minimum share of the circle for a district
const MAX_PIECE = Math.PI / 3;   // widest slice, wider ones are split (convex pieces)
const ARC_STEP = Math.PI / 24;   // arc approximation

export function radialLayout(ctx: LayoutContext): TemplateResult {
  const origin: Point = { x: 0, z: 0 };
  const hub = Math.max(ctx.squareRadius * 2.2, 9);
  const blockAreas = ctx.districts.map((d) => d.blocks.map((b) => blockArea(b.buildings.length, ctx)));
  const areas = blockAreas.map((a) => a.reduce((s, x) => s + x, 0));
  const total = areas.reduce((s, a) => s + a, 0);
  const shares = areas.map((a) => Math.max(a / total, MIN_SHARE));
  const shareSum = shares.reduce((s, x) => s + x, 0);

  const avenues: RoadSegment[] = [];
  const result: TemplateResult = { districts: [], blocks: [], plazas: [{ x: 0, z: 0, radius: hub }], avenues };
  let angle = -Math.PI / 2;
  const inner = hub + MAIN_ROAD;

  ctx.districts.forEach((d: District, di) => {
    const theta = (2 * Math.PI * shares[di]) / shareSum;

    // Pieces <= 60° (convex), blocks split in contiguous groups of similar area. With fewer blocks
    // than pieces a piece may get up to 90°; what is left of the angle stays as open ground.
    const groups = groupByArea(blockAreas[di], Math.ceil(theta / MAX_PIECE - 1e-9));
    const pieceAngle = Math.min(theta / groups.length, MAX_PIECE * 1.5);
    const outlines: Point[][] = [];
    let a0 = angle + (theta - pieceAngle * groups.length) / 2;
    let longest = inner;
    groups.forEach((group) => {
      const groupArea = group.reduce((s, i) => s + blockAreas[di][i], 0);
      const a1 = a0 + pieceAngle;
      // blocks start where the slice is at least 3 lots wide (narrow slices: a longer avenue first)
      const start = Math.max(hub + STREET, (3 * ctx.pitch + STREET + MAIN_ROAD) / pieceAngle);
      // ground of the slice between start and R: angle/2 (R^2 - start^2) = area (+ roads)
      const radius = Math.sqrt((2 * groupArea * 1.25) / pieceAngle + start * start) + MAIN_ROAD / 2;
      longest = Math.max(longest, radius);
      const slice = sliceOutline(origin, a0, a1, radius);
      const bis = (a0 + a1) / 2;
      const ux = Math.cos(bis);
      const uz = Math.sin(bis);

      // asphalt: the slice from its ring road on, plus an avenue from the roundabout to it
      // (the ground between the avenues stays green)
      outlines.push(clipHalfPlane(slice, -ux, -uz, -(start - MAIN_ROAD)));
      outlines.push(avenue(ux, uz, hub * 0.8, start - MAIN_ROAD / 2, MAIN_ROAD / 2));
      // center lines of the avenue and of the ring road in front of the slice
      const ring = start - MAIN_ROAD / 2;
      const chord = (a: number) => {
        const r = ring / Math.cos(a - bis);
        return { x: r * Math.cos(a), z: r * Math.sin(a) };
      };
      avenues.push({ a: { x: ux * hub, z: uz * hub }, b: { x: ux * ring, z: uz * ring }, width: MAIN_ROAD, kind: 'main' });
      avenues.push({ a: chord(a0), b: chord(a1), width: MAIN_ROAD, kind: 'main' });

      // blocks: cells up to the street center lines; buildable bound inside the main roads
      const bound = clipHalfPlane(insetConvex(slice, MAIN_ROAD / 2), -ux, -uz, -start);
      const region = clipHalfPlane(slice, -ux, -uz, -ring);
      const cells = splitByAreas(region.length >= 3 ? region : slice, group.map((i) => blockAreas[di][i]), radialCuts(origin));
      group.forEach((bi, k) =>
        result.blocks.push({ source: d.blocks[bi], districtId: d.districtId, color: d.color, cell: cells[k], bound: bound.length >= 3 ? bound : cells[k] }),
      );
      a0 = a1;
    });

    const mid = angle + theta / 2;
    const labelR = (inner + longest) / 2;
    result.districts.push({
      districtId: d.districtId,
      name: d.name,
      color: d.color,
      x: labelR * Math.cos(mid),
      z: labelR * Math.sin(mid),
      radius: Math.sqrt(areas[di] / Math.PI),
      outlines,
    });
    angle += theta;
  });

  return result;
}

/** Straight road along a direction from the center, between two distances. */
function avenue(ux: number, uz: number, from: number, to: number, halfWidth: number): Point[] {
  const [px, pz] = [-uz * halfWidth, ux * halfWidth];
  const end = Math.max(to, from + 0.1);
  return [
    { x: ux * from + px, z: uz * from + pz },
    { x: ux * end + px, z: uz * end + pz },
    { x: ux * end - px, z: uz * end - pz },
    { x: ux * from - px, z: uz * from - pz },
  ];
}

/** Slice of disc from the center between two angles (convex when a1 - a0 <= 180°). */
function sliceOutline(o: Point, a0: number, a1: number, r: number): Point[] {
  const steps = Math.max(1, Math.ceil((a1 - a0) / ARC_STEP));
  const pts: Point[] = [o];
  for (let k = 0; k <= steps; k++) {
    const a = a0 + ((a1 - a0) * k) / steps;
    pts.push({ x: o.x + r * Math.cos(a), z: o.z + r * Math.sin(a) });
  }
  return pts;
}

/** Splits indexes 0..n-1 in `count` contiguous groups of similar total area (none empty). */
function groupByArea(areas: number[], count: number): number[][] {
  const groups: number[][] = [];
  const total = areas.reduce((s, a) => s + a, 0);
  let current: number[] = [];
  let acc = 0;
  areas.forEach((a, i) => {
    const remainingGroups = count - groups.length;
    const remainingItems = areas.length - i;
    if (current.length > 0 && remainingGroups > 1 && (acc + a / 2 > (total * (groups.length + 1)) / count || remainingItems < remainingGroups)) {
      groups.push(current);
      current = [];
    }
    current.push(i);
    acc += a;
  });
  groups.push(current);
  return groups;
}
