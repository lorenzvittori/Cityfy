import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/errors/error-messages';

/**
 * Error message reused everywhere (ERRORI_UI.md §7.1): message chosen by code, the matching
 * action, and a collapsed "Dettagli" with the traceId for support.
 */
@Component({
  selector: 'app-error-message',
  imports: [RouterLink],
  template: `
    <div class="error" [class.compact]="compact()" role="alert">
      <p>{{ info().message }}</p>
      <div class="actions">
        @switch (info().action) {
          @case ('retry') { <button type="button" (click)="retry.emit()">Riprova</button> }
          @case ('login') { <button type="button" (click)="auth.login()">Accedi con Spotify</button> }
          @case ('relink') { <button type="button" (click)="auth.login()">Ricollega Spotify</button> }
          @case ('export-guide') { <a routerLink="/upload" fragment="guida">Come scaricare l'export giusto</a> }
          @case ('upload') { <a routerLink="/upload">Carica l'export</a> }
          @case ('home') { <a routerLink="/city">Torna alla città</a> }
        }
      </div>
      @if (traceId()) {
        <details>
          <summary>Dettagli</summary>
          <code>{{ code() }} · {{ traceId() }}</code>
        </details>
      }
    </div>
  `,
  styleUrl: './error-message.scss',
})
export class ErrorMessage {
  readonly code = input.required<string>();
  readonly traceId = input<string | null>(null);
  readonly compact = input(false);
  readonly retry = output<void>();

  protected readonly auth = inject(AuthService);
  protected readonly info = computed(() => errorMessage(this.code()));
}
