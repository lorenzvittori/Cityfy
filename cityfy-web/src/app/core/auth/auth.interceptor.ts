import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** A 401 on any call (except /me, used to probe the session) means the session expired: back to /login. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !req.url.endsWith('/me')) {
        auth.clear();
        router.navigate(['/login'], { queryParams: { returnUrl: router.url, error: 'UNAUTHENTICATED' } });
      }
      return throwError(() => error);
    }),
  );
};
