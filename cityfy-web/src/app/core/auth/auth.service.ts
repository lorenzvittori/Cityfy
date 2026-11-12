import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Me } from '../api/api.models';

/** Session state. The session itself is an HttpOnly cookie set by the API: the UI only asks /me. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);

  readonly me = signal<Me | null>(null);
  readonly isLoggedIn = computed(() => this.me() !== null);

  /** Loads /me once; false when there is no valid session (401 UNAUTHENTICATED). */
  ensureSession(): Observable<boolean> {
    if (this.me()) return of(true);
    return this.api.me().pipe(
      tap((me) => this.me.set(me)),
      map(() => true),
      catchError(() => of(false)),
    );
  }

  login(returnUrl = '/cities'): void {
    window.location.href = this.api.loginUrl(returnUrl);
  }

  logout(): Observable<void> {
    return this.api.logout().pipe(tap(() => this.me.set(null)));
  }

  /** Called when any request answers 401: the cookie expired. */
  clear(): void {
    this.me.set(null);
  }
}
