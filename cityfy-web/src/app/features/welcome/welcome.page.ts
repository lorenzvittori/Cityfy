import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';

/**
 * Entry page: no account needed. "Inizia" opens an anonymous session (the data lives one day after
 * the last visit); "Accedi con Last.fm" opens an account (attribution required by the Last.fm terms). Arrives here with ?error=SESSION_EXPIRED when the session expired, ?deleted=1
 * after "Cancella i miei dati". A visitor who still has a session goes straight to the cities.
 */
@Component({
  selector: 'app-welcome-page',
  imports: [ErrorMessage],
  template: `
    <section class="welcome">
      <h1>Cityfy</h1>
      <p class="lead">La tua musica, trasformata in una città: ogni genere un quartiere, ogni artista un palazzo.</p>

      @if (deleted()) {
        <p class="notice" role="status">I tuoi dati sono stati cancellati. Puoi ricominciare quando vuoi.</p>
      }

      @if (error(); as code) {
        @if (code !== 'UNAUTHENTICATED') {
          <app-error-message [code]="code" [compact]="true" />
        }
      }

      <button type="button" class="primary" [disabled]="starting()" (click)="start()">Inizia senza account</button>
      @if (startError(); as e) {
        <app-error-message [code]="e.code" [traceId]="e.traceId" [compact]="true" (retry)="start()" />
      }
      <p class="hint">
        Non serve registrarsi: carichi l'export di Spotify e vedi la tua città. I dati restano finché torni almeno una
        volta al giorno; dopo un giorno senza visite vengono cancellati.
      </p>

      <div class="or"><span>oppure</span></div>

      <button type="button" (click)="loginWithLastFm()">Accedi con Last.fm</button>
      <p class="hint">
        Collega Spotify a Last.fm dalle impostazioni di Last.fm: Cityfy importa tutto lo storico e riceve gli ascolti
        nuovi mentre ascolti, da qualunque dispositivo. I dati restano finché non li cancelli.
      </p>
      <p class="powered">powered by <a href="https://www.last.fm" target="_blank" rel="noopener">AudioScrobbler</a></p>
    </section>
  `,
  styleUrl: './welcome.page.scss',
})
export class WelcomePage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly params = toSignal(
    this.route.queryParamMap.pipe(map((p) => ({ error: p.get('error'), returnUrl: p.get('returnUrl'), deleted: p.has('deleted') }))),
  );

  protected readonly error = computed(() => this.params()?.error ?? null);
  protected readonly deleted = computed(() => this.params()?.deleted ?? false);
  protected readonly starting = signal(false);
  protected readonly startError = signal<ApiError | null>(null);

  ngOnInit(): void {
    this.auth
      .ensureSession()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((ok) => {
        if (ok) this.router.navigateByUrl(this.params()?.returnUrl ?? '/cities');
      });
  }

  protected loginWithLastFm(): void {
    this.auth.loginWithLastFm(this.params()?.returnUrl ?? '/cities');
  }

  protected start(): void {
    this.starting.set(true);
    this.startError.set(null);
    this.auth.start().subscribe({
      next: () => this.router.navigateByUrl(this.params()?.returnUrl ?? '/upload'),
      error: (e: ApiError) => {
        this.starting.set(false);
        this.startError.set(e);
      },
    });
  }
}
