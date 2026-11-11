import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminService } from '../../core/api/admin.service';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';

/** Login of the mock admin user (Admin:Username / Admin:Password of CityFy.Api, Development only). */
@Component({
  selector: 'app-admin-login-page',
  imports: [FormsModule, ErrorMessage],
  template: `
    <form class="admin-login" (ngSubmit)="submit()">
      <h1>Cityfy · Admin</h1>
      @if (error(); as code) {
        <app-error-message [code]="code" [compact]="true" />
      }
      <label>Utente <input name="username" [(ngModel)]="username" autocomplete="username" required /></label>
      <label>Password <input name="password" type="password" [(ngModel)]="password" autocomplete="current-password" required /></label>
      <button type="submit" class="primary" [disabled]="busy()">Entra</button>
      <p class="hint">Utente di sviluppo definito nella configurazione dell'API (sezione <code>Admin</code>).</p>
    </form>
  `,
  styles: `
    .admin-login { max-width: 360px; margin: 12vh auto 0; padding: 0 20px; display: flex; flex-direction: column; gap: 14px; }
    h1 { margin: 0 0 6px; }
    label { display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
    input { font: inherit; padding: 8px 10px; border: 1px solid #cfc5b2; border-radius: 8px; }
    .hint { font-size: 12px; color: #777; }
  `,
})
export class AdminLoginPage {
  private readonly admin = inject(AdminService);
  private readonly router = inject(Router);

  protected username = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    this.busy.set(true);
    this.error.set(null);
    this.admin.login(this.username, this.password).subscribe({
      next: () => this.router.navigate(['/admin']),
      error: (e: ApiError) => {
        this.error.set(e.code);
        this.busy.set(false);
      },
    });
  }
}
