import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, switchMap, takeWhile, timer } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { LastFmImport, LastFmStatus } from '../../core/api/api.models';
import { dateTimeLabel } from '../../core/auth/session-labels';
import { ErrorMessage } from '../../shared/error-message/error-message';

const ImportPollMs = 3000;

/**
 * Last.fm account of the user: profile (link required by the Last.fm terms), reading of the new
 * plays, and the import of the history while it runs (polled every 3 s).
 */
@Component({
  selector: 'app-lastfm-status',
  imports: [ErrorMessage],
  template: `
    <div class="lastfm">
      <p>
        Accesso con Last.fm come
        <a [href]="status().profileUrl" target="_blank" rel="noopener"><strong>{{ status().username }}</strong></a>.
        I tuoi dati restano finché non li cancelli.
      </p>

      @if (status().status === 'Suspended') {
        <app-error-message [code]="status().errorCode ?? 'LASTFM_AUTH_FAILED'" [compact]="true" />
      } @else {
        <p class="muted">Ultimo ascolto ricevuto {{ lastPlay() }}. Gli ascolti nuovi arrivano mentre ascolti.</p>
        @if (status().warning; as warning) {
          <div class="hint" role="status">
            <app-error-message [code]="warning" [compact]="true" />
            <a href="https://www.last.fm/settings/applications" target="_blank" rel="noopener">Apri le applicazioni collegate su Last.fm</a>
          </div>
        }
      }

      @if (history(); as h) {
        @switch (h.status) {
          @case ('Pending') { <p class="muted">Importazione dello storico in coda…</p> }
          @case ('InProgress') {
            <p>Importazione dello storico: {{ progress(h) }}</p>
            <progress max="100" [value]="h.progressPercent ?? 0"></progress>
          }
          @case ('Done') { <p class="muted">Storico importato: {{ h.newPlays }} ascolti.</p> }
          @case ('Failed') { <app-error-message [code]="h.errorCode ?? 'LASTFM_IMPORT_FAILED'" [compact]="true" /> }
        }
      }
      <p class="muted small">
        Se gli ascolti non arrivano, controlla su Last.fm che Spotify sia collegato (Impostazioni → Applicazioni).
        Gli ascolti offline o in sessione privata potrebbero non arrivare a Last.fm.
      </p>
    </div>
  `,
  styles: `
    .lastfm { padding: 10px 14px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); }
    p { margin: 6px 0; }
    .muted { color: var(--text-muted); }
    .small { font-size: 13px; }
    progress { width: 100%; }
    .hint a { font-size: 13px; }
  `,
})
export class LastFmStatusPanel {
  readonly status = input.required<LastFmStatus>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private polling?: Subscription;

  protected readonly history = signal<LastFmImport | null>(null);
  protected readonly lastPlay = computed(() => dateTimeLabel(this.status().lastScrobbleAt));

  constructor() {
    // Follows the import of the history until it ends
    effect(() => {
      const importId = this.status().historyImportId;
      this.polling?.unsubscribe();
      if (!importId) return;
      this.polling = timer(0, ImportPollMs)
        .pipe(
          switchMap(() => this.api.lastFmImport(importId)),
          takeWhile((i) => i.status === 'Pending' || i.status === 'InProgress', true),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe({ next: (i) => this.history.set(i), error: () => this.history.set(null) });
    });
  }

  protected progress(i: LastFmImport): string {
    const percent = i.progressPercent === null ? '' : ` (${Math.round(i.progressPercent)}%)`;
    return `${i.newPlays} ascolti${percent}`;
  }
}
