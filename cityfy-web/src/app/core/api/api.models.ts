// Contract of CityFy.Api v1 (CityFy/CityFy.Api/Models/V1Dtos.cs).
// Hand-written for now: to be replaced by a client generated from the API OpenAPI document.

export type Period = 'all' | '1y' | '6m' | '30d';

export interface Me {
  userId: string;
  displayName: string | null;
  linkedAt: string | null;
  lastSyncAt: string | null;
}

export interface City {
  period: string;
  generatedAt: string;
  from: string | null;
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

/** CANCELLED: stopped by an administrator (errorCode PROCESS_CANCELLED). */
export type ImportState = 'Pending' | 'WORKING_IN_PROGRESS' | 'DONE' | 'FAILED' | 'CANCELLED';

export interface ImportStatus {
  id: string;
  status: ImportState;
  newPlays: number;
  startedAt: string;
  completedAt: string | null;
  errorCode: string | null;
}

export type RunState = 'Pending' | 'Building' | 'Done' | 'Failed' | 'Cancelled';

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
