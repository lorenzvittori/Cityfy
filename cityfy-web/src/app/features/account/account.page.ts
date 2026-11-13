import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { dateTimeLabel } from '../../core/auth/session-labels';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';
import { LastFmStatusPanel } from './lastfm-status';

type DeleteState = { kind: 'idle' } | { kind: 'confirm' } | { kind: 'deleting' } | { kind: 'error'; error: ApiError };

/**
 * The user's data: when an anonymous session expires (and how to keep the data: log in with Last.fm),
 * the Last.fm reading of an account, what Cityfy keeps and "Cancella i miei dati"
 * (two steps: the button opens a confirmation that lists what goes away). After the deletion the
 * session is closed and the user lands on the welcome page with a confirmation.
 */
@Component({
  selector: 'app-account-page',
  imports: [ErrorMessage, LastFmStatusPanel],
  template: `
    <section class="account">
      <h1>I tuoi dati</h1>
      @if (auth.me(); as me) {
        @if (me.kind === 'anonymous') {
          <p class="expiry">
            Stai usando Cityfy senza account.
            @if (expiry(); as when) {
              Se non torni entro <strong>{{ when }}</strong>, i tuoi dati verranno cancellati.
            }
            Ogni visita sposta la scadenza a un giorno dopo.
          </p>
          <p>
            Per tenerli, <button type="button" (click)="auth.loginWithLastFm('/account')">accedi con Last.fm</button>:
            i dati di questa sessione passano al tuo account.
          </p>
        } @else if (me.lastFm; as lastFm) {
          <app-lastfm-status [status]="lastFm" />
        }

        <h2>Cosa conserviamo</h2>
        <ul>
          @if (me.kind === 'lastfm') {
            <li>i tuoi ascolti, letti da Last.fm o dall'export che hai caricato;</li>
          } @else {
            <li>i tuoi ascolti, letti dall'export che hai caricato;</li>
          }
          <li>i file dell'export che hai caricato;</li>
          <li>le città costruite e lo storico delle costruzioni;</li>
          @if (me.kind === 'lastfm') {
            <li>il tuo nome utente Last.fm e la chiave per leggere i tuoi ascolti (cifrata). Nessuna email o password.</li>
          }
        </ul>
      }

      <div class="danger">
        <h2>Cancella i miei dati</h2>
        <p>
          Elimina subito e definitivamente tutto quello che Cityfy conserva su di te{{ auth.isAnonymous() ? ', senza aspettare la scadenza' : '' }}.
          L'operazione non si può annullare: potrai sempre ricominciare.
        </p>

        @switch (state().kind) {
          @case ('idle') {
            <button type="button" class="danger-button" (click)="state.set({ kind: 'confirm' })">Cancella i miei dati…</button>
          }
          @case ('confirm') {
            <div class="confirm" role="alertdialog" aria-labelledby="confirm-title">
              <p id="confirm-title">
                <strong>Sei sicuro?</strong> Verranno cancellati ascolti, città e file caricati{{ auth.isAccount() ? ', e il collegamento con Last.fm' : '' }}.
                Poi uscirai dall'app.
              </p>
              <div class="buttons">
                <button type="button" class="danger-button" (click)="delete()">Sì, cancella tutto</button>
                <button type="button" (click)="state.set({ kind: 'idle' })">Annulla</button>
              </div>
            </div>
          }
          @case ('deleting') {
            <p class="muted">Cancellazione in corso…</p>
          }
          @case ('error') {
            @if (state(); as s) {
              @if (s.kind === 'error') {
                <app-error-message [code]="s.error.code" [traceId]="s.error.traceId" (retry)="delete()" />
              }
            }
          }
        }
      </div>
    </section>
  `,
  styles: `
    .account { max-width: 640px; margin: 32px auto; padding: 0 20px; }
    h1 { margin: 0 0 6px; }
    h2 { font-size: 17px; margin: 24px 0 8px; }
    ul { margin: 0; padding-left: 20px; line-height: 1.7; }
    .muted { color: var(--text-muted); }
    .expiry { padding: 10px 14px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); }
    .danger { margin-top: 32px; padding: 4px 20px 20px; border: 1px solid var(--error-border); border-radius: 10px; background: var(--error-bg); }
    .danger h2 { color: var(--danger); }
    .danger-button { border-color: var(--danger); background: var(--danger); color: #fff; font-weight: 600; }
    .confirm p { margin: 0 0 12px; }
    .buttons { display: flex; gap: 10px; }
  `,
})
export class AccountPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected readonly state = signal<DeleteState>({ kind: 'idle' });
  protected readonly expiry = computed(() => {
    const expiresAt = this.auth.me()?.expiresAt;
    return expiresAt ? dateTimeLabel(expiresAt) : null;
  });

  /** /me again: expiry and state of the Last.fm reading change while the app is open. */
  ngOnInit(): void {
    this.api.me().subscribe({ next: (me) => this.auth.me.set(me), error: () => undefined });
  }

  protected delete(): void {
    this.state.set({ kind: 'deleting' });
    this.api.deleteMyData().subscribe({
      next: () => {
        this.auth.clear();
        this.router.navigate(['/welcome'], { queryParams: { deleted: 1 } });
      },
      error: (error: ApiError) => this.state.set({ kind: 'error', error }),
    });
  }
}
