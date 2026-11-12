import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ErrorMessage } from '../../shared/error-message/error-message';

/** Login with Spotify. Errors of the Spotify login come back here as ?error=CODE (ERRORI_UI.md §4). */
@Component({
  selector: 'app-login-page',
  imports: [ErrorMessage],
  template: `
    <section class="login">
      <h1>Cityfy</h1>
      <p class="lead">La tua musica, trasformata in una città: ogni genere un quartiere, ogni artista un palazzo.</p>

      @if (deleted()) {
        <p class="notice" role="status">I tuoi dati sono stati cancellati. Puoi ricominciare quando vuoi accedendo di nuovo.</p>
      }

      @if (error(); as code) {
        @if (code !== 'UNAUTHENTICATED') {
          <app-error-message [code]="code" [compact]="true" />
        }
      }

      <button type="button" class="primary" (click)="login()">Accedi con Spotify</button>
      <p class="hint">
        Leggiamo solo gli ascolti recenti e il tuo nome profilo. Per tutta la cronologia potrai caricare l'export di Spotify.
      </p>
    </section>
  `,
  styleUrl: './login.page.scss',
})
export class LoginPage {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly params = toSignal(
    this.route.queryParamMap.pipe(map((p) => ({ error: p.get('error'), returnUrl: p.get('returnUrl'), deleted: p.has('deleted') }))),
  );

  protected readonly error = computed(() => this.params()?.error ?? null);
  /** Back from "Cancella i miei dati". */
  protected readonly deleted = computed(() => this.params()?.deleted ?? false);

  protected login(): void {
    this.auth.login(this.params()?.returnUrl ?? '/cities');
  }
}
