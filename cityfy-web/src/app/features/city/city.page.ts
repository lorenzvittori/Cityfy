import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription, filter, switchMap, take, timer } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { Building, City, Period, PipelineRun } from '../../core/api/api.models';
import { ApiError } from '../../core/errors/api-error';
import { errorMessage } from '../../core/errors/error-messages';
import { AuthService } from '../../core/auth/auth.service';
import { ErrorMessage } from '../../shared/error-message/error-message';
import { CityScene } from './city-scene/city-scene';

/**
 * The user's city. States (ERRORI_UI.md §5):
 *   ready     -> scene + legend, warnings on top
 *   building  -> CITY_NOT_READY with a Pending/Building run: "in costruzione", polls until Done
 *   failed    -> CITY_NOT_READY with a Failed run: message of its errorCode
 *   welcome   -> CITY_NOT_READY without runs: first-time empty state
 *   error     -> any other error (generic message + retry)
 */
type ViewState =
  | { kind: 'loading' }
  | { kind: 'ready'; city: City }
  | { kind: 'building'; run: PipelineRun }
  | { kind: 'failed'; run: PipelineRun }
  | { kind: 'welcome' }
  | { kind: 'error'; error: ApiError };

const RUN_POLL_MS = 4000;

@Component({
  selector: 'app-city-page',
  imports: [CityScene, ErrorMessage, RouterLink],
  templateUrl: './city.page.html',
  styleUrl: './city.page.scss',
})
export class CityPage {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  protected readonly periods: { value: Period; label: string }[] = [
    { value: 'all', label: 'Tutto' },
    { value: '1y', label: '1 anno' },
    { value: '6m', label: '6 mesi' },
    { value: '30d', label: '30 giorni' },
  ];

  protected readonly period = signal<Period>('all');
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly selectedArtistId = signal<string | null>(null);
  protected readonly message = errorMessage;

  protected readonly selected = computed(() => {
    const s = this.state();
    const id = this.selectedArtistId();
    if (s.kind !== 'ready' || !id) return null;
    for (const d of s.city.districts)
      for (const b of d.blocks) {
        const building = b.buildings.find((x) => x.artistId === id);
        if (building) return { building, district: d.name, genre: b.name, color: d.color };
      }
    return null;
  });

  private polling?: Subscription;

  constructor() {
    this.load();
  }

  protected setPeriod(period: Period): void {
    this.period.set(period);
    this.selectedArtistId.set(null);
    this.load();
  }

  protected load(): void {
    this.polling?.unsubscribe();
    this.state.set({ kind: 'loading' });
    this.api
      .city(this.period())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (city) => this.state.set({ kind: 'ready', city }),
        error: (e: ApiError) => (e.code === 'CITY_NOT_READY' ? this.explainMissingCity() : this.state.set({ kind: 'error', error: e })),
      });
  }

  /** No city yet: in progress, failed or never started? */
  private explainMissingCity(): void {
    this.api.runs(1).subscribe({
      next: ([latest]) => {
        if (!latest) this.state.set({ kind: 'welcome' });
        else if (latest.status === 'Failed') this.state.set({ kind: 'failed', run: latest });
        else if (latest.status === 'Done') this.load(); // built meanwhile (or for another period: shows CITY_NO_PLAYS)
        else this.waitForRun(latest);
      },
      error: (e: ApiError) => this.state.set({ kind: 'error', error: e }),
    });
  }

  private waitForRun(run: PipelineRun): void {
    this.state.set({ kind: 'building', run });
    this.polling = timer(RUN_POLL_MS, RUN_POLL_MS)
      .pipe(
        switchMap(() => this.api.runs(1)),
        filter(([latest]) => !!latest),
        filter(([latest]) => {
          if (latest.status === 'Pending' || latest.status === 'Building') {
            this.state.set({ kind: 'building', run: latest });
            return false;
          }
          return true;
        }),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ([latest]) => (latest.status === 'Done' ? this.load() : this.state.set({ kind: 'failed', run: latest })),
        error: (e: ApiError) => this.state.set({ kind: 'error', error: e }),
      });
  }

  protected select(artistId: string): void {
    this.selectedArtistId.set(this.selectedArtistId() === artistId ? null : artistId);
  }

  /** spotify:artist:{id} -> web page (works without the Spotify app). */
  protected spotifyUrl(uri: string): string {
    return `https://open.spotify.com/artist/${encodeURIComponent(uri.replace('spotify:artist:', ''))}`;
  }

  protected minutes(b: Building): string {
    return b.listeningMinutes >= 60 ? `${Math.round(b.listeningMinutes / 60)} ore` : `${Math.round(b.listeningMinutes)} min`;
  }
}
