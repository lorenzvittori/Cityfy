import { City, District } from '../../../core/api/api.models';
import { CITY_TEMPLATES, Point, SCALE, SUBBLOCK_MAX, apportion, computeCityLayout } from './city-layout';
import { mergeCollinear, roadMarkings } from './streets';
import { axisCuts, clipHalfPlane, convexHull, insetConvex, insideConvex, polygonArea, powerCell, radialCuts, splitByAreas } from './geometry';
import { voronoiTreemap } from './organic-layout';

function district(id: string, artists: number[][], color = '#ff0000'): District {
  return {
    districtId: id,
    name: id,
    color,
    density: 1,
    listeningIntensity: 50,
    score: 1,
    blocks: artists.map((counts, b) => ({
      blockId: `${id}__b${b}`,
      genreId: b,
      name: `genre ${b}`,
      score: 1,
      buildings: counts.map((h, i) => ({
        artistId: `${id}-${b}-${i}`,
        name: `artist ${i}`,
        spotifyArtistUri: null,
        plays: 1,
        qualifiedPlays: 1,
        listeningMinutes: 1,
        height: h,
        width: 12,
        color,
        genres: [],
      })),
    })),
  };
}

function city(...districts: District[]): City {
  return {
    period: 'all',
    generatedAt: '',
    from: null,
    warnings: [],
    summary: { totalPlays: 0, qualifiedPlays: 0, listeningHours: 0, totalArtists: 0, placedArtists: 0, coveragePercent: 0, topGenres: [] },
    districts,
  };
}

const square = (s: number): Point[] => [{ x: -s, z: -s }, { x: s, z: -s }, { x: s, z: s }, { x: -s, z: s }];

/** A realistic-ish city: one dominant district, a few medium, many small genres. */
function bigCity(): City {
  const sizes = [120, 60, 35, 20, 12, 8, 5, 3];
  return city(
    ...sizes.map((n, d) =>
      district(`d${d}`, Array.from({ length: Math.max(1, Math.round(n / 6)) }, (_, b) => Array.from({ length: 1 + ((b * 7 + d) % 9) }, (_, i) => 4 + ((i * 13) % 36)))),
    ),
  );
}

describe('geometry', () => {
  it('clips a square with a half-plane', () => {
    expect(polygonArea(clipHalfPlane(square(1), 1, 0, 0))).toBeCloseTo(2, 6); // x <= 0
  });

  it('insets a convex polygon on every side', () => {
    expect(polygonArea(insetConvex(square(2), 0.5))).toBeCloseTo(9, 6);
    expect(insetConvex(square(1), 2)).toEqual([]);
  });

  it('power cell of two equal circles is split on the bisector', () => {
    const cell = powerCell([{ x: -1, z: 0, r: 1 }, { x: 1, z: 0, r: 1 }], 0, square(4));
    expect(Math.max(...cell.map((p) => p.x))).toBeCloseTo(0, 6);
  });

  it('convex hull drops the inner points', () => {
    expect(convexHull([...square(1), { x: 0, z: 0 }, { x: 0.5, z: 0.2 }])).toHaveLength(4);
  });

  it('splits a polygon in pieces of the requested areas', () => {
    for (const family of [axisCuts, radialCuts({ x: 0, z: -10 })]) {
      const pieces = splitByAreas(square(4), [5, 3, 1, 1], family);
      expect(pieces.map((p) => polygonArea(p) / 64)).toEqual([0.5, 0.3, 0.1, 0.1].map((x) => expect.closeTo(x, 4)));
    }
  });

  it('merges the two sides of a shared edge and overlapping pieces into one segment', () => {
    const merged = mergeCollinear([
      { a: { x: 0, z: 0 }, b: { x: 4, z: 0 } },
      { a: { x: 4, z: 0 }, b: { x: 0, z: 0 } }, // same edge seen from the neighbor
      { a: { x: 3, z: 0 }, b: { x: 9, z: 0 } }, // T-junction: continues
      { a: { x: 0, z: 5 }, b: { x: 2, z: 5 } }, // another line
    ]);

    expect(merged).toHaveLength(2);
    const long = merged.find((s) => s.a.z === 0)!;
    expect(Math.abs(long.b.x - long.a.x)).toBeCloseTo(9, 6);
  });

  it('paints a double line on main roads, dashes on streets, zebra stripes at the crossings', () => {
    const road = (kind: 'main' | 'street') => ({ a: { x: 0, z: 0 }, b: { x: 40, z: 0 }, width: kind === 'main' ? 6 : 3, kind });
    const none = { crossings: [], lamps: [], trafficLights: [], signs: [] };

    const main = roadMarkings({ ...none, roads: [road('main')] });
    expect(main).toHaveLength(2);
    expect(main.map((s) => s.z).sort()).toEqual([-0.15, 0.15]);

    const street = roadMarkings({ ...none, roads: [road('street')] });
    expect(street.length).toBeGreaterThan(5);
    expect(street.every((s) => s.z === 0 && s.length < 2)).toBe(true);

    const zebra = roadMarkings({ ...none, roads: [], crossings: [{ x: 0, z: 0, angle: 0, width: 6 }] });
    expect(zebra.length).toBeGreaterThanOrEqual(6);
  });

  it('apportions items by weight, summing exactly', () => {
    expect(apportion(10, [1, 1, 1])).toEqual([4, 3, 3]);
    expect(apportion(5, [0, 0])).toEqual([5, 0]);
  });

  it('Voronoi treemap: cells tile the boundary with about the requested areas', () => {
    const targets = [40, 20, 15, 10, 8, 4, 2, 1];
    const cells = voronoiTreemap(square(10), targets);
    const areas = cells.map(polygonArea);

    expect(areas.reduce((s, a) => s + a, 0)).toBeCloseTo(400, 3);
    areas.forEach((a, i) => expect(a / 400).toBeCloseTo(targets[i] / 100, 1));
  });
});

describe.each(CITY_TEMPLATES.map((t) => t.value))('computeCityLayout (%s)', (template) => {
  it('places every artist once, scaled', () => {
    const layout = computeCityLayout(city(district('rock', [[40, 20], [10]]), district('jazz', [[4]])), template);

    expect(layout.buildings.map((b) => b.artistId).sort()).toEqual(['jazz-0-0', 'rock-0-0', 'rock-0-1', 'rock-1-0']);
    expect(layout.buildings.find((b) => b.artistId === 'rock-0-0')!.height).toBe(40 * SCALE);
    expect(layout.districts.map((d) => d.districtId)).toEqual(['rock', 'jazz']);
  });

  it('places every artist of a big city once, inside its block, never overlapping', () => {
    const c = bigCity();
    const layout = computeCityLayout(c, template);
    const expected = c.districts.flatMap((d) => d.blocks.flatMap((b) => b.buildings.map((x) => x.artistId)));

    expect(layout.buildings.map((b) => b.artistId).sort()).toEqual([...expected].sort());

    // on a plate of its own genre (sidewalks and streets stay free)
    const blocks = new Map(layout.blocks.map((b) => [b.blockId, b]));
    for (const b of layout.buildings) expect(blocks.get(b.blockId)!.parts.some((p) => insideConvex(p.plate, b, 1e-3))).toBe(true);

    const positions = new Set(layout.buildings.map((b) => `${b.x.toFixed(2)},${b.z.toFixed(2)}`));
    expect(positions.size).toBe(layout.buildings.length);
  });

  it('splits big genres in sub-blocks with inner streets', () => {
    const layout = computeCityLayout(city(district('rock', [Array.from({ length: 40 }, () => 10)])), template);

    expect(layout.blocks[0].parts).toHaveLength(Math.ceil(40 / SUBBLOCK_MAX));
    expect(layout.streets.roads.some((r) => r.kind === 'street')).toBe(true);
  });

  it('sub-blocks never overlap (streets between them)', () => {
    const layout = computeCityLayout(bigCity(), template);
    const parts = layout.blocks.flatMap((b) => b.parts);

    for (const a of parts) {
      const inner = insetConvex(a.outline, 0.01);
      for (const b of parts) if (b !== a) expect(inner.some((p) => insideConvex(b.outline, p, -1e-6))).toBe(false);
    }
  });

  it('keeps the blocks out of the roundabouts', () => {
    const layout = computeCityLayout(bigCity(), template);

    for (const p of layout.plazas)
      for (const b of layout.blocks) for (const part of b.parts) expect(insideConvex(part.outline, p)).toBe(false);
  });

  it('has roads with markings and furniture on the sidewalks', () => {
    const layout = computeCityLayout(bigCity(), template);
    const { roads, crossings, lamps, signs } = layout.streets;
    const parts = layout.blocks.flatMap((b) => b.parts);
    const onSidewalk = (p: Point) => parts.some((x) => insideConvex(x.outline, p) && !insideConvex(x.plate, p));

    expect(roads.some((r) => r.kind === 'main')).toBe(true);
    expect(roads.some((r) => r.kind === 'street')).toBe(true);
    expect(crossings.length).toBeGreaterThan(0);
    expect(lamps.length).toBeGreaterThan(0);
    for (const f of [...lamps, ...signs, ...layout.streets.trafficLights]) expect(onSidewalk(f)).toBe(true);
  });

  it('is compact: the blocks cover a good part of the city', () => {
    const layout = computeCityLayout(bigCity(), template);
    const blocks = layout.blocks.reduce((s, b) => s + b.parts.reduce((t, p) => t + polygonArea(p.outline), 0), 0);
    const ground = layout.districts.reduce((s, d) => s + d.outlines.reduce((t, o) => t + polygonArea(o), 0), 0);

    // radial: narrow slices lose more ground to the roads around them
    expect(blocks / ground).toBeGreaterThan(template === 'radial' ? 0.35 : 0.45);
  });

  it('is deterministic', () => {
    expect(computeCityLayout(bigCity(), template)).toEqual(computeCityLayout(bigCity(), template));
  });

  it('handles an empty city', () => {
    const layout = computeCityLayout(city(), template);

    expect(layout.buildings).toEqual([]);
    expect(layout.width).toBe(0);
  });
});
