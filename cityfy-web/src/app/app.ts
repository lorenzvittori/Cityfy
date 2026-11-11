import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, switchMap, take, timer } from 'rxjs';
import { ApiService } from './core/api/api.service';
import { AuthService } from './core/auth/auth.service';
import { ApiError } from './core/errors/api-error';
import { ErrorMessage } from './shared/error-message/error-message';

type SyncState = { kind: 'idle' } | { kind: 'running' } | { kind: 'done'; newPlays: number } | { kind: 'error'; code: string };

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ErrorMessage],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sync = signal<SyncState>({ kind: 'idle' });

  /** Reads the latest plays from Spotify now (it also happens every 30 minutes on its own). */
  protected syncNow(): void {
    this.sync.set({ kind: 'running' });
    this.api
      .sync()
      .pipe(
        switchMap(({ jobId }) => timer(1000, 2000).pipe(switchMap(() => this.api.syncStatus(jobId)))),
        filter((s) => s.status === 'DONE' || s.status === 'FAILED'),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (s) =>
          this.sync.set(s.status === 'DONE' ? { kind: 'done', newPlays: s.newPlays } : { kind: 'error', code: s.errorCode ?? 'SPOTIFY_RETRIEVE_FAILED' }),
        error: (e: ApiError) => this.sync.set({ kind: 'error', code: e.code }),
      });
  }

  protected logout(): void {
    this.auth.logout().subscribe({ complete: () => this.router.navigate(['/login']) });
  }
}
