import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';

type DeleteState = { kind: 'idle' } | { kind: 'confirm' } | { kind: 'deleting' } | { kind: 'error'; error: ApiError };

/**
 * The user's account: what Cityfy keeps and "Cancella i miei dati" (two steps: the button opens a
 * confirmation that lists what goes away). After the deletion the session is closed and the user
 * lands on the login page with a confirmation.
 */
@Component({
  selector: 'app-account-page',
  imports: [ErrorMessage],
  template: `
    <section class="account">
      <h1>Il tuo account</h1>
      @if (auth.me(); as me) {
        <p class="muted">Accesso con Spotify come <strong>{{ me.displayName ?? me.userId }}</strong> ({{ me.userId }})</p>
      }

      <h2>Cosa conserviamo</h2>
      <ul>
        <li>i tuoi ascolti, letti da Spotify o dall'export che hai caricato;</li>
        <li>i file dell'export che hai caricato;</li>
        <li>le città costruite e lo storico delle costruzioni;</li>
        <li>il collegamento con il tuo account Spotify (per leggere gli ascolti recenti).</li>
      </ul>

      <div class="danger">
        <h2>Cancella i miei dati</h2>
        <p>
          Elimina definitivamente tutto quello che Cityfy conserva su di te. L'operazione non si può annullare:
          potrai sempre ricominciare accedendo di nuovo con Spotify.
        </p>

        @switch (state().kind) {
          @case ('idle') {
            <button type="button" class="danger-button" (click)="state.set({ kind: 'confirm' })">Cancella i miei dati…</button>
          }
          @case ('confirm') {
            <div class="confirm" role="alertdialog" aria-labelledby="confirm-title">
              <p id="confirm-title"><strong>Sei sicuro?</strong> Verranno cancellati ascolti, città, file caricati e collegamento con Spotify. Poi uscirai dall'app.</p>
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
    .danger { margin-top: 32px; padding: 4px 20px 20px; border: 1px solid var(--error-border); border-radius: 10px; background: var(--error-bg); }
    .danger h2 { color: var(--danger); }
    .danger-button { border-color: var(--danger); background: var(--danger); color: #fff; font-weight: 600; }
    .confirm p { margin: 0 0 12px; }
    .buttons { display: flex; gap: 10px; }
  `,
})
export class AccountPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected readonly state = signal<DeleteState>({ kind: 'idle' });

  protected delete(): void {
    this.state.set({ kind: 'deleting' });
    this.api.deleteMyData().subscribe({
      next: () => {
        this.auth.clear();
        this.router.navigate(['/login'], { queryParams: { deleted: 1 } });
      },
      error: (error: ApiError) => this.state.set({ kind: 'error', error }),
    });
  }
}
