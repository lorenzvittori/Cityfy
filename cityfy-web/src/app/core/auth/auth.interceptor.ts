import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * A 401 means there is no session any more: back to the welcome page with the reason
 * (SESSION_EXPIRED: the anonymous session expired and its data was deleted), or to the admin login.
 * Excluded: the calls used to probe a session (/me) and the admin login itself.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      const isProbe = req.url.endsWith('/me') || req.url.endsWith('/admin/login');
      if (error instanceof HttpErrorResponse && error.status === 401 && !isProbe) {
        if (req.url.includes('/api/admin/')) {
          router.navigate(['/admin/login']);
        } else {
          auth.clear();
          const code = typeof error.error?.code === 'string' ? error.error.code : 'UNAUTHENTICATED';
          router.navigate(['/welcome'], { queryParams: { returnUrl: router.url, error: code } });
        }
      }
      return throwError(() => error);
    }),
  );
};
