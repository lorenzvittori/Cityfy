import { GenreOption } from '../../core/api/api.models';
import {
  NO_SELECTION,
  apiRange,
  districtChoiceOf,
  districtPreview,
  districtState,
  districtsError,
  districtsRequest,
  formTo,
  rangeError,
  selectionOf,
  shortcutRange,
  subgenreChecked,
  toggleDistrict,
  toggleSubgenre,
} from './city-form';
import { cityTitle, districtsLabel, periodLabel } from './city-labels';

const rock: GenreOption = {
  genreId: 1,
  name: 'rock',
  color: '#f00',
  qualifiedPlays: 10,
  artistCount: 3,
  subgenres: [
    { genreId: 11, name: 'grunge', qualifiedPlays: 6, artistCount: 2 },
    { genreId: 12, name: 'punk', qualifiedPlays: 4, artistCount: 1 },
  ],
};

describe('city titles', () => {
  it('uses the name of the user, else genres, period and map type', () => {
    expect(cityTitle({ name: 'Mia', topGenres: ['rock'], from: null, to: null, layout: 'grid' })).toBe('Mia');
    expect(cityTitle({ name: null, topGenres: ['rock', 'jazz', 'pop'], from: '2024-01-01T00:00:00Z', to: '2025-01-01T00:00:00Z', layout: 'grid' })).toBe(
      'Rock e Jazz · gen 2024 – dic 2024 · Griglia',
    );
    expect(cityTitle({ name: null, topGenres: [], from: null, to: null, layout: 'organic' })).toBe('La tua città · Tutto il periodo · Organica');
  });

  it('describes open ranges', () => {
    expect(periodLabel('2024-03-01T00:00:00Z', null)).toBe('dal mar 2024');
    expect(periodLabel(null, '2024-04-01T00:00:00Z')).toBe('fino a mar 2024');
  });
});

describe('city form dates', () => {
  const today = new Date('2026-11-12T10:00:00Z');

  it('fills the dates of a shortcut', () => {
    expect(shortcutRange('30d', today)).toEqual({ from: '2026-10-13', to: '2026-11-12' });
    expect(shortcutRange('1y', today)).toEqual({ from: '2025-11-12', to: '2026-11-12' });
    expect(shortcutRange('all', today)).toEqual({ from: '', to: '' });
  });

  it('sends the end of the range as the next day (exclusive) and reads it back', () => {
    const range = apiRange('2024-01-01', '2024-12-31');
    expect(range).toEqual({ from: '2024-01-01T00:00:00Z', to: '2025-01-01T00:00:00.000Z' });
    expect(formTo(range.to)).toBe('2024-12-31');
    expect(apiRange('', '')).toEqual({ from: null, to: null });
  });

  it('rejects inverted or future ranges', () => {
    expect(rangeError('2025-01-01', '2024-01-01', today)).not.toBeNull();
    expect(rangeError('2027-01-01', '', today)).not.toBeNull();
    expect(rangeError('2024-01-01', '2024-01-01', today)).toBeNull();
  });
});

describe('genre selection', () => {
  it('checks a whole district, then single subgenres when one is unchecked', () => {
    let s = toggleDistrict(rock, NO_SELECTION);
    expect(districtState(rock, s)).toBe('checked');
    expect(subgenreChecked(rock, 12, s)).toBe(true);

    s = toggleSubgenre(rock, 11, s);
    expect([...s.genres]).toEqual([]);
    expect([...s.subgenres]).toEqual([12]);
    expect(districtState(rock, s)).toBe('partial');
  });

  it('becomes the whole district when every subgenre is checked', () => {
    const s = toggleSubgenre(rock, 12, toggleSubgenre(rock, 11, NO_SELECTION));

    expect([...s.genres]).toEqual([1]);
    expect([...s.subgenres]).toEqual([]);
  });

  it('unchecks a district with its subgenres', () => {
    const s = toggleDistrict(rock, toggleSubgenre(rock, 11, NO_SELECTION));

    expect(districtState(rock, s)).toBe('checked');
    expect(districtState(rock, toggleDistrict(rock, s))).toBe('unchecked');
  });
});

describe('districts', () => {
  const genre = (genreId: number): GenreOption => ({ genreId, name: `g${genreId}`, color: '#000', qualifiedPlays: 1, artistCount: 1, subgenres: [] });
  const five = [1, 2, 3, 4, 5].map(genre);

  it('reads the query parameters of "duplicate and edit"', () => {
    expect(districtChoiceOf(null, null)).toEqual({ all: false, max: null, extra: 'merge' });
    expect(districtChoiceOf('0', 'drop')).toEqual({ all: true, max: null, extra: 'drop' });
    expect(districtChoiceOf('8', 'other')).toEqual({ all: false, max: 8, extra: 'merge' });
  });

  it('sends 0 for all and null for the default', () => {
    expect(districtsRequest({ all: true, max: 5, extra: 'drop' })).toEqual({ maxDistricts: 0, extraGenres: 'drop' });
    expect(districtsRequest({ all: false, max: null, extra: 'merge' })).toEqual({ maxDistricts: null, extraGenres: 'merge' });
  });

  it('needs one real genre beside "Altri generi", and has no maximum', () => {
    expect(districtsError({ all: false, max: 1, extra: 'merge' })).not.toBeNull();
    expect(districtsError({ all: false, max: 1, extra: 'drop' })).toBeNull();
    expect(districtsError({ all: false, max: 2.5, extra: 'drop' })).not.toBeNull();
    expect(districtsError({ all: false, max: 500, extra: 'merge' })).toBeNull();
  });

  it('previews merged and dropped genres', () => {
    // 5 macro-genres, max 3: merge keeps 2 + "Altri generi" (3 merged), drop keeps 3 (2 dropped)
    expect(districtPreview(five, NO_SELECTION, { all: false, max: 3, extra: 'merge' }, 12, 20)).toEqual({ candidates: 5, shown: 3, extra: 3, crowded: false });
    expect(districtPreview(five, NO_SELECTION, { all: false, max: 3, extra: 'drop' }, 12, 20)).toEqual({ candidates: 5, shown: 3, extra: 2, crowded: false });
    expect(districtPreview(five, NO_SELECTION, { all: true, max: null, extra: 'merge' }, 12, 4)).toEqual({ candidates: 5, shown: 5, extra: 0, crowded: true });
  });

  it('counts only the chosen macro-genres, and uses the default when no number is set', () => {
    const chosen = selectionOf([1, 2], []);
    expect(districtPreview(five, chosen, { all: false, max: null, extra: 'merge' }, 12, 20).candidates).toBe(2);
    expect(districtPreview([...five, rock], selectionOf([], [11]), { all: false, max: null, extra: 'merge' }, 12, 20).candidates).toBe(1);
  });

  it('describes the choice of a saved city', () => {
    expect(districtsLabel(null, null, 12)).toBe('12 quartieri, altri generi uniti');
    expect(districtsLabel(8, 'drop', 12)).toBe('8 quartieri, altri generi esclusi');
    expect(districtsLabel(0, 'merge', 12)).toBe('Tutti i quartieri');
  });
});
