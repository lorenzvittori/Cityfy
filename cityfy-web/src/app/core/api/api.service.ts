import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { ApiError } from '../errors/api-error';
import { City, ImportStatus, Me, Period, PipelineRun, UserDataDeletionResult } from './api.models';
import { localizeCity } from './city-labels';

/** Calls to CityFy.Api (/api/v1, same origin through the dev-server proxy). Errors are ApiError. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1';

  me(): Observable<Me> {
    return this.wrap(this.http.get<Me>(`${this.base}/me`));
  }

  logout(): Observable<void> {
    return this.wrap(this.http.post<void>(`${this.base}/auth/logout`, null));
  }

  /** Full-page navigation to Spotify: the login is not an XHR. */
  loginUrl(returnUrl: string): string {
    return `${this.base}/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  }

  city(period: Period): Observable<City> {
    return this.wrap(this.http.get<City>(`${this.base}/city`, { params: new HttpParams().set('period', period) })).pipe(map(localizeCity));
  }

  runs(limit = 5): Observable<PipelineRun[]> {
    return this.wrap(this.http.get<PipelineRun[]>(`${this.base}/pipeline/runs`, { params: new HttpParams().set('limit', limit) }));
  }

  /** Rebuilds the user's city now, from all the plays already imported (any source). */
  rebuild(): Observable<PipelineRun> {
    return this.wrap(this.http.post<PipelineRun>(`${this.base}/city/rebuild`, null));
  }

  /** Deletes everything Cityfy keeps about the user; the session is closed by the server. */
  deleteMyData(): Observable<UserDataDeletionResult> {
    return this.wrap(this.http.delete<UserDataDeletionResult>(`${this.base}/me/data`));
  }

  upload(file: File): Observable<{ uploadId: string }> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.wrap(this.http.post<{ uploadId: string }>(`${this.base}/uploads`, form));
  }

  uploadStatus(uploadId: string): Observable<ImportStatus> {
    return this.wrap(this.http.get<ImportStatus>(`${this.base}/uploads/${encodeURIComponent(uploadId)}`));
  }

  sync(): Observable<{ jobId: string }> {
    return this.wrap(this.http.post<{ jobId: string }>(`${this.base}/spotify/sync`, null));
  }

  syncStatus(jobId: string): Observable<ImportStatus> {
    return this.wrap(this.http.get<ImportStatus>(`${this.base}/spotify/sync/${encodeURIComponent(jobId)}`));
  }

  private wrap<T>(request: Observable<T>): Observable<T> {
    return request.pipe(catchError((e) => throwError(() => ApiError.from(e))));
  }
}
