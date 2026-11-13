import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Me } from '../api/api.models';

/**
 * Session state. The session itself is an HttpOnly cookie set by the API: the UI only asks /me.
 * Without an account the session is anonymous and lasts one day after the last visit.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);

  readonly me = signal<Me | null>(null);
  readonly isLoggedIn = computed(() => this.me() !== null);
  readonly isAnonymous = computed(() => this.me()?.kind === 'anonymous');
  readonly isAccount = computed(() => this.me()?.kind === 'lastfm');

  /**
   * "Accedi con Last.fm": full-page navigation. With an anonymous session open, its data moves to
   * the account (the API does it at the callback).
   */
  loginWithLastFm(returnUrl = '/cities'): void {
    window.location.href = this.api.lastFmLoginUrl(returnUrl);
  }

  logout(): Observable<void> {
    return this.api.logout().pipe(tap(() => this.me.set(null)));
  }

  /** Loads /me once; false when there is no valid session (401 UNAUTHENTICATED / SESSION_EXPIRED). */
  ensureSession(): Observable<boolean> {
    if (this.me()) return of(true);
    return this.api.me().pipe(
      tap((me) => this.me.set(me)),
      map(() => true),
      catchError(() => of(false)),
    );
  }

  /** "Inizia senza account": opens an anonymous session (or keeps the current one) and loads /me. */
  start(): Observable<Me> {
    return this.api.startSession().pipe(
      switchMap(() => this.api.me()),
      tap((me) => this.me.set(me)),
    );
  }

  /** Called when any request answers 401: the session expired. */
  clear(): void {
    this.me.set(null);
  }
}
