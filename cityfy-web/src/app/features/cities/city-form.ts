// Logic of the "new city" form (pure): date range, genre selection and number of districts.

import { ExtraGenres, GenreOption } from '../../core/api/api.models';

// --- Dates ----------------------------------------------------------------------

export type RangeShortcut = '30d' | '6m' | '1y' | 'all';

export const SHORTCUTS: { value: RangeShortcut; label: string }[] = [
  { value: '30d', label: '30 giorni' },
  { value: '6m', label: '6 mesi' },
  { value: '1y', label: '1 anno' },
  { value: 'all', label: 'Tutto' },
];

/** yyyy-mm-dd of a date (UTC). */
export function isoDay(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Form dates (both inclusive, yyyy-mm-dd or '') of a shortcut ending today. */
export function shortcutRange(shortcut: RangeShortcut, today: Date): { from: string; to: string } {
  if (shortcut === 'all') return { from: '', to: '' };
  const from = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
  if (shortcut === '30d') from.setUTCDate(from.getUTCDate() - 30);
  if (shortcut === '6m') from.setUTCMonth(from.getUTCMonth() - 6);
  if (shortcut === '1y') from.setUTCFullYear(from.getUTCFullYear() - 1);
  return { from: isoDay(from), to: isoDay(today) };
}

/** API range from the form dates: from at 00:00 UTC, to exclusive (the day after the last one). */
export function apiRange(from: string, to: string): { from: string | null; to: string | null } {
  const end = to ? new Date(`${to}T00:00:00Z`) : null;
  end?.setUTCDate(end.getUTCDate() + 1);
  return { from: from ? `${from}T00:00:00Z` : null, to: end ? end.toISOString() : null };
}

/** Form date (inclusive) of an API "to" (exclusive). */
export function formTo(apiTo: string | null): string {
  return apiTo ? isoDay(new Date(Date.parse(apiTo) - 1)) : '';
}

/** Validation of the form dates (the backend checks again: CITY_INVALID_RANGE). */
export function rangeError(from: string, to: string, today: Date): string | null {
  if (from && from > isoDay(today)) return "La data di inizio non può essere nel futuro.";
  if (from && to && from > to) return "La data di inizio deve venire prima della fine.";
  return null;
}

// --- Genres -----------------------------------------------------------------------

/**
 * Chosen genres: a whole district (genres) or single subgenres (blocks). Nothing chosen = every
 * genre. Checking a district replaces its single subgenres; unchecking one subgenre of a whole
 * district keeps the others as single subgenres.
 */
export interface GenreSelection {
  genres: ReadonlySet<number>;
  subgenres: ReadonlySet<number>;
}

export const NO_SELECTION: GenreSelection = { genres: new Set(), subgenres: new Set() };

export type CheckState = 'checked' | 'partial' | 'unchecked';

export function districtState(option: GenreOption, s: GenreSelection): CheckState {
  if (s.genres.has(option.genreId)) return 'checked';
  return option.subgenres.some((g) => s.subgenres.has(g.genreId)) ? 'partial' : 'unchecked';
}

export function subgenreChecked(option: GenreOption, subgenreId: number, s: GenreSelection): boolean {
  return s.genres.has(option.genreId) || s.subgenres.has(subgenreId);
}

export function toggleDistrict(option: GenreOption, s: GenreSelection): GenreSelection {
  const genres = new Set(s.genres);
  const subgenres = new Set(s.subgenres);
  option.subgenres.forEach((g) => subgenres.delete(g.genreId));
  if (genres.has(option.genreId)) genres.delete(option.genreId);
  else genres.add(option.genreId);
  return { genres, subgenres };
}

export function toggleSubgenre(option: GenreOption, subgenreId: number, s: GenreSelection): GenreSelection {
  const genres = new Set(s.genres);
  const subgenres = new Set(s.subgenres);
  if (genres.has(option.genreId)) {
    // whole district -> all its subgenres but this one
    genres.delete(option.genreId);
    option.subgenres.filter((g) => g.genreId !== subgenreId).forEach((g) => subgenres.add(g.genreId));
  } else if (subgenres.has(subgenreId)) subgenres.delete(subgenreId);
  else {
    subgenres.add(subgenreId);
    // every subgenre checked -> the whole district
    if (option.subgenres.every((g) => subgenres.has(g.genreId))) {
      option.subgenres.forEach((g) => subgenres.delete(g.genreId));
      genres.add(option.genreId);
    }
  }
  return { genres, subgenres };
}

/** Selection of a request (e.g. "duplicate and edit"). */
export function selectionOf(genres: number[], subgenres: number[]): GenreSelection {
  return { genres: new Set(genres), subgenres: new Set(subgenres) };
}

export function isEmpty(s: GenreSelection): boolean {
  return s.genres.size === 0 && s.subgenres.size === 0;
}

// --- Districts ----------------------------------------------------------------------

/** Defaults when GET cities/options does not answer (same as the backend configuration). */
export const DEFAULT_DISTRICTS = 12;
export const WARN_DISTRICTS = 20;

/**
 * How many districts the city shows. all = every macro-genre (sent as 0). max null = the default
 * of the backend. No maximum: beyond warnDistricts the form only warns.
 */
export interface DistrictChoice {
  all: boolean;
  max: number | null;
  extra: ExtraGenres;
}

export const DEFAULT_DISTRICT_CHOICE: DistrictChoice = { all: false, max: null, extra: 'merge' };

/** With "merge" one district is "Altri generi": at least one real genre beside it (same rule as the backend). */
export function minDistricts(extra: ExtraGenres): number {
  return extra === 'merge' ? 2 : 1;
}

/** Validation of the number (the backend checks again: CITY_INVALID_DISTRICTS). */
export function districtsError(choice: DistrictChoice): string | null {
  if (choice.all || choice.max === null) return null;
  const min = minDistricts(choice.extra);
  if (!Number.isInteger(choice.max) || choice.max < min) return `Scegli almeno ${min} quartier${min === 1 ? 'e' : 'i'}.`;
  return null;
}

/** The request fields: 0 = all, null = the backend default. */
export function districtsRequest(choice: DistrictChoice): { maxDistricts: number | null; extraGenres: ExtraGenres } {
  return { maxDistricts: choice.all ? 0 : choice.max, extraGenres: choice.extra };
}

/** Choice of query parameters ("duplicate and edit"): districts=0 is "all", extra=drop. */
export function districtChoiceOf(districts: string | null, extra: string | null): DistrictChoice {
  const n = districts ? Number(districts) : NaN;
  return {
    all: n === 0,
    max: Number.isInteger(n) && n > 0 ? n : null,
    extra: extra === 'drop' ? 'drop' : 'merge',
  };
}

/**
 * What the city will look like: candidates = macro-genres involved (the chosen ones, or every one
 * listened in the period when nothing is chosen); shown = districts on the map ("Altri generi"
 * included); extra = macro-genres merged or dropped; crowded = beyond the warning threshold.
 */
export interface DistrictPreview {
  candidates: number;
  shown: number;
  extra: number;
  crowded: boolean;
}

export function districtPreview(
  options: GenreOption[],
  selection: GenreSelection,
  choice: DistrictChoice,
  defaultMax: number,
  warnDistricts: number,
): DistrictPreview {
  const candidates = isEmpty(selection) ? options.length : options.filter((o) => districtState(o, selection) !== 'unchecked').length;
  const max = choice.all ? 0 : (choice.max ?? defaultMax);
  const limited = max > 0 && candidates > max;
  const kept = choice.extra === 'merge' ? max - 1 : max;
  const shown = limited ? max : candidates;
  return { candidates, shown, extra: limited ? candidates - kept : 0, crowded: shown > warnDistricts };
}
