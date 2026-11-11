import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * A 401 means the session expired: back to the right login page.
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
          router.navigate(['/login'], { queryParams: { returnUrl: router.url, error: 'UNAUTHENTICATED' } });
        }
      }
      return throwError(() => error);
    }),
  );
};
