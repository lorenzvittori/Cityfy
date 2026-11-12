import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { ApiError } from '../errors/api-error';
import { UserDataDeletionResult } from './api.models';

// Admin panel API (CityFy/CityFy.Api/Controllers/AdminController.cs).

export interface ServiceHealth {
  name: string;
  status: 'Healthy' | 'Unhealthy' | 'Unreachable';
  elapsedMs: number | null;
  detail: string | null;
}

export interface ServicesOverview {
  services: ServiceHealth[];
  links: { dashboard: string | null; rabbitMq: string | null };
}

/** Progress of a running import/retrieve: percent null = cannot be measured (indeterminate). */
export interface Progress {
  progressPercent: number | null;
  progressDetail: string | null;
  updatedAt: string | null;
}

export interface MbImportStatus {
  state: 'Idle' | 'Running' | 'Done' | 'Failed' | 'Cancelled';
  startedAt: string | null;
  completedAt: string | null;
  error: string | null;
  /** Starting, Download, Extracting, Reading tables, Writing database. */
  progress: { phase: string; percent: number | null; detail: string | null; updatedAt: string } | null;
  result: {
    dumpName: string | null;
    genres: number;
    genreRelations: number;
    artists: number;
    artistGenres: number;
    spotifyLinks: number;
    duration: string;
  } | null;
}

export interface AdminRun {
  runId: string;
  userId: string;
  status: string;
  triggers: { source: string; sourceRef: string; newPlays: number; at: string }[];
  periods: string[];
  createdAt: string;
  dueAt: string;
  startedAt: string | null;
  completedAt: string | null;
  errorCode: string | null;
}

export interface AdminAccount {
  userId: string;
  displayName: string | null;
  linkedAt: string;
  lastRetrieveAt: string | null;
  lastPlayedAt: string | null;
}

export interface AdminUpload extends Progress {
  uploadId: string;
  userId: string | null;
  status: string;
  inserted: number;
  startedAt: string;
  completedAt: string | null;
  errorCode: string | null;
}

export interface AdminSpotifyJob extends Progress {
  jobId: string;
  userId: string | null;
  status: string;
  inserted: number;
  startedAt: string;
  completedAt: string | null;
  errorCode: string | null;
}

export interface TableInfo {
  name: string;
  rows: number;
}

export interface TablePage {
  name: string;
  total: number;
  page: number;
  pageSize: number;
  columns: string[];
  rows: Record<string, unknown>[];
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/admin';

  login(username: string, password: string) {
    return this.wrap(this.http.post<{ username: string }>(`${this.base}/login`, { username, password }));
  }
  me() {
    return this.wrap(this.http.get<{ username: string }>(`${this.base}/me`));
  }
  logout() {
    return this.wrap(this.http.post<void>(`${this.base}/logout`, null));
  }

  services() {
    return this.wrap(this.http.get<ServicesOverview>(`${this.base}/services`));
  }

  mbStatus() {
    return this.wrap(this.http.get<MbImportStatus>(`${this.base}/musicbrainz/import`));
  }
  mbStart() {
    return this.wrap(this.http.post<MbImportStatus>(`${this.base}/musicbrainz/import`, null));
  }

  runs(limit = 50) {
    return this.wrap(this.http.get<AdminRun[]>(`${this.base}/pipeline/runs`, { params: new HttpParams().set('limit', limit) }));
  }
  rebuild(userId: string) {
    return this.wrap(this.http.post<AdminRun>(`${this.base}/pipeline/rebuild/${encodeURIComponent(userId)}`, null));
  }

  accounts() {
    return this.wrap(this.http.get<AdminAccount[]>(`${this.base}/spotify/accounts`));
  }
  sync(userId: string) {
    return this.wrap(this.http.post<{ jobId: string }>(`${this.base}/spotify/accounts/${encodeURIComponent(userId)}/sync`, null));
  }
  spotifyJobs(limit = 30) {
    return this.wrap(this.http.get<AdminSpotifyJob[]>(`${this.base}/spotify/jobs`, { params: new HttpParams().set('limit', limit) }));
  }
  uploads(page = 1) {
    return this.wrap(this.http.get<AdminUpload[]>(`${this.base}/uploads`, { params: new HttpParams().set('page', page) }));
  }

  collections() {
    return this.wrap(this.http.get<TableInfo[]>(`${this.base}/db/mongo`));
  }
  collection(name: string, page: number, pageSize = 25) {
    return this.wrap(this.http.get<TablePage>(`${this.base}/db/mongo/${encodeURIComponent(name)}`, { params: this.paging(page, pageSize) }));
  }
  tables() {
    return this.wrap(this.http.get<TableInfo[]>(`${this.base}/db/sql`));
  }
  table(name: string, page: number, pageSize = 25) {
    return this.wrap(this.http.get<TablePage>(`${this.base}/db/sql/${encodeURIComponent(name)}`, { params: this.paging(page, pageSize) }));
  }

  /** Deletes everything Cityfy keeps about a user (USER_DATA_DELETION_INCOMPLETE: retry). */
  deleteUserData(userId: string) {
    return this.wrap(this.http.delete<UserDataDeletionResult>(`${this.base}/users/${encodeURIComponent(userId)}/data`));
  }

  // --- Cancellation: 409 PROCESS_NOT_CANCELLABLE if already finished
  cancelImport() {
    return this.wrap(this.http.post<unknown>(`${this.base}/musicbrainz/import/cancel`, null));
  }
  cancelUpload(uploadId: string) {
    return this.wrap(this.http.post<unknown>(`${this.base}/uploads/${encodeURIComponent(uploadId)}/cancel`, null));
  }
  cancelSpotifyJob(jobId: string) {
    return this.wrap(this.http.post<unknown>(`${this.base}/spotify/jobs/${encodeURIComponent(jobId)}/cancel`, null));
  }
  cancelRun(runId: string) {
    return this.wrap(this.http.post<unknown>(`${this.base}/pipeline/runs/${encodeURIComponent(runId)}/cancel`, null));
  }

  private paging(page: number, pageSize: number) {
    return new HttpParams().set('page', page).set('pageSize', pageSize);
  }

  private wrap<T>(request: Observable<T>): Observable<T> {
    return request.pipe(catchError((e) => throwError(() => ApiError.from(e))));
  }
}
