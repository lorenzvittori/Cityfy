import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AdminService } from '../api/admin.service';

/** Admin pages need an admin session (mock admin user): otherwise /admin/login. */
export const adminGuard: CanActivateFn = () => {
  const admin = inject(AdminService);
  const router = inject(Router);
  return admin.me().pipe(
    map(() => true),
    catchError(() => of(router.createUrlTree(['/admin/login']))),
  );
};
