import { Component, computed, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { dateTimeLabel } from './core/auth/session-labels';
import { LiveService } from './core/live/live.service';
import { ThemeService } from './core/theme/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly live = inject(LiveService);
  private readonly router = inject(Router);

  constructor() {
    // The live connection (track playing now) only for accounts: it comes from Last.fm
    effect(() => (this.auth.isAccount() ? this.live.connect() : this.live.disconnect()));
  }

  protected readonly theme = inject(ThemeService);
  protected readonly themeLabel = computed(
    () => ({ system: 'Tema: automatico (come il sistema)', light: 'Tema: chiaro', dark: 'Tema: scuro' })[this.theme.preference()],
  );
  protected readonly themeIcon = computed(() => ({ system: '◐', light: '☀', dark: '☾' })[this.theme.preference()]);

  /** Anonymous session: "Senza account · fino a domani alle 15:40" (moves forward with every visit). */
  protected readonly userLabel = computed(() => {
    const me = this.auth.me();
    if (!me) return '';
    if (me.kind === 'anonymous') return me.expiresAt ? `Senza account · fino a ${dateTimeLabel(me.expiresAt)}` : 'Senza account';
    return me.displayName ?? me.userId;
  });

  /** Only accounts log out: an anonymous session without its cookie could not be reopened. */
  protected logout(): void {
    this.auth.logout().subscribe({ complete: () => this.router.navigate(['/welcome']) });
  }
}
