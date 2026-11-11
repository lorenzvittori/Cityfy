import { City, District } from '../../../core/api/api.models';
import { SCALE, computeCityLayout, packShelf } from './city-layout';

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

describe('packShelf', () => {
  it('wraps to a new row when the row is full', () => {
    const items = [1, 2, 3].map(() => ({ width: 10, depth: 5 }));
    const { placed, width, depth } = packShelf(items, 2, 25);

    expect(placed.map((p) => [p.x, p.z])).toEqual([[0, 0], [12, 0], [0, 7]]);
    expect(width).toBe(22);
    expect(depth).toBe(12);
  });
});

describe('computeCityLayout', () => {
  it('places every artist once, scaled', () => {
    const layout = computeCityLayout(city(district('rock', [[40, 20], [10]]), district('jazz', [[4]])));

    expect(layout.buildings.map((b) => b.artistId).sort()).toEqual(['jazz-0-0', 'rock-0-0', 'rock-0-1', 'rock-1-0']);
    expect(layout.buildings.find((b) => b.artistId === 'rock-0-0')!.height).toBe(40 * SCALE);
    expect(layout.districts.map((d) => d.districtId)).toEqual(['rock', 'jazz']);
  });

  it('keeps buildings inside their district and never overlapping', () => {
    const layout = computeCityLayout(city(district('a', [[10, 10, 10, 10, 10], [10, 10]]), district('b', [[10, 10, 10]])));

    for (const b of layout.buildings) {
      const d = layout.districts.find((x) => x.districtId === b.districtId)!;
      expect(Math.abs(b.x - d.x)).toBeLessThanOrEqual(d.width / 2);
      expect(Math.abs(b.z - d.z)).toBeLessThanOrEqual(d.depth / 2);
    }
    const positions = new Set(layout.buildings.map((b) => `${b.x.toFixed(2)},${b.z.toFixed(2)}`));
    expect(positions.size).toBe(layout.buildings.length);
  });

  it('centers the city on the origin', () => {
    const layout = computeCityLayout(city(district('a', [[10]]), district('b', [[10]]), district('c', [[10]])));
    const xs = layout.districts.flatMap((d) => [d.x - d.width / 2, d.x + d.width / 2]);

    expect(Math.min(...xs) + Math.max(...xs)).toBeCloseTo(0, 5);
  });

  it('handles an empty city', () => {
    const layout = computeCityLayout(city());

    expect(layout.buildings).toEqual([]);
    expect(layout.width).toBe(0);
  });
});
