// Contract of CityFy.Api v1 (CityFy/CityFy.Api/Models/V1Dtos.cs).
// Hand-written for now: to be replaced by a client generated from the API OpenAPI document.

import { CityMap } from './city-map.models';

/**
 * anonymous: no account, the data is deleted at expiresAt unless the user comes back before.
 * lastfm: account (login with Last.fm), the data stays until the user deletes it.
 */
export type SessionKind = 'anonymous' | 'lastfm';

export interface Me {
  kind: SessionKind;
  userId: string;
  /** Last.fm user name for an account. */
  displayName: string | null;
  linkedAt: string | null;
  lastSyncAt: string | null;
  /** Only anonymous sessions; moves forward with every visit. */
  expiresAt: string | null;
  /** Only accounts. */
  lastFm?: LastFmStatus | null;
}

/** Reading of the Last.fm account. Suspended: errorCode LASTFM_AUTH_FAILED or LASTFM_USER_PRIVATE (log in again). */
export interface LastFmStatus {
  username: string;
  profileUrl: string;
  status: 'Active' | 'Suspended';
  errorCode: string | null;
  /** Newest play received. */
  lastScrobbleAt: string;
  lastPolledAt: string | null;
  historyImportId: string | null;
  /** Hint, not an error: LASTFM_SPOTIFY_NOT_SCROBBLING (no new play for days). */
  warning?: string | null;
}

/** Event "now-playing" of api/v1/live. artistMbid matches the artistId of the buildings. */
export interface NowPlaying {
  playing: boolean;
  artist: string | null;
  track: string | null;
  album: string | null;
  artistMbid: string | null;
  startedAt: string | null;
  /** Page of the track on Last.fm (attribution). */
  trackUrl: string | null;
}

/** Import of the Last.fm history. */
export interface LastFmImport {
  id: string;
  status: 'Pending' | 'InProgress' | 'Done' | 'Failed' | 'Cancelled';
  newPlays: number;
  progressPercent: number | null;
  progressDetail: string | null;
  startedAt: string;
  completedAt: string | null;
  errorCode: string | null;
}

export interface Session {
  kind: SessionKind;
  expiresAt: string | null;
}

/** Data of a generated city (districts > blocks > buildings); to is exclusive. */
export interface City {
  generatedAt: string;
  from: string | null;
  to: string | null;
  summary: CitySummary;
  /** ErrorCodes "CITY_*" warnings (ERRORI_UI.md §5). */
  warnings: string[];
  districts: District[];
}

export interface CitySummary {
  totalPlays: number;
  qualifiedPlays: number;
  listeningHours: number;
  totalArtists: number;
  placedArtists: number;
  coveragePercent: number;
  topGenres: string[];
}

export interface District {
  districtId: string;
  name: string;
  color: string;
  density: number;
  listeningIntensity: number;
  score: number;
  blocks: Block[];
}

export interface Block {
  blockId: string;
  genreId: number;
  name: string;
  score: number;
  buildings: Building[];
}

export interface Building {
  artistId: string;
  name: string;
  spotifyArtistUri: string | null;
  plays: number;
  qualifiedPlays: number;
  listeningMinutes: number;
  height: number;
  width: number;
  color: string;
  genres: string[];
}

// --- Generated cities ---------------------------------------------------------

export type CityLayoutType = 'organic' | 'grid' | 'custom';

/** Cancelled: stopped by the user or an administrator (errorCode PROCESS_CANCELLED). */
export type GenerationState = 'Pending' | 'InProgress' | 'Done' | 'Failed' | 'Cancelled';

/** A saved city without its content. name null: the UI composes a title (cityTitle). */
export interface GeneratedCity {
  cityId: string;
  name: string | null;
  from: string | null;
  /** Exclusive. */
  to: string | null;
  layout: string;
  recipe: string | null;
  genres: number[];
  subgenres: number[];
  status: GenerationState;
  errorCode: string | null;
  createdAt: string;
  completedAt: string | null;
  districtCount: number;
  artistCount: number;
  topGenres: string[];
}

/** city and map: null until status is Done. */
export interface GeneratedCityDetail {
  info: GeneratedCity;
  city: City | null;
  map: CityMap | null;
}

export interface GeneratedCityPage {
  items: GeneratedCity[];
  page: number;
  pageSize: number;
  total: number;
}

/** from/to: ISO dates, to exclusive. genres = district ids, subgenres = block ids; both empty = every genre. */
export interface CreateCityRequest {
  name: string | null;
  from: string | null;
  to: string | null;
  layout: CityLayoutType;
  /** Parameter string, only with layout "custom". */
  recipe: string | null;
  genres: number[];
  subgenres: number[];
}

/** A district (top-level genre) that can be chosen, with its subgenres. */
export interface GenreOption {
  genreId: number;
  name: string;
  color: string;
  qualifiedPlays: number;
  artistCount: number;
  subgenres: SubgenreOption[];
}

export interface SubgenreOption {
  genreId: number;
  name: string;
  qualifiedPlays: number;
  artistCount: number;
}

/** CANCELLED: stopped by an administrator (errorCode PROCESS_CANCELLED). */
export type ImportState = 'Pending' | 'InProgress' | 'Done' | 'Failed' | 'Cancelled';

export interface ImportStatus {
  id: string;
  status: ImportState;
  newPlays: number;
  startedAt: string;
  completedAt: string | null;
  errorCode: string | null;
}

/** What each service deleted (name -> count), e.g. Catalog { plays: 1234, cities: 4 }. */
export interface UserDataDeletionResult {
  userId: string;
  deleted: { service: string; deleted: Record<string, number> }[];
}

export type RunState ='Pending' | 'Building' | 'Done' | 'Failed' | 'Cancelled';

export interface PipelineRun {
  runId: string;
  status: RunState;
  newPlays: number;
  sources: string[];
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  errorCode: string | null;
}
