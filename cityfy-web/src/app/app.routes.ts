import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'city' },
  { path: 'login', loadComponent: () => import('./features/login/login.page').then((m) => m.LoginPage), title: 'Cityfy · Accedi' },
  {
    path: 'city',
    canActivate: [authGuard],
    loadComponent: () => import('./features/city/city.page').then((m) => m.CityPage),
    title: 'Cityfy · La tua città',
  },
  {
    path: 'upload',
    canActivate: [authGuard],
    loadComponent: () => import('./features/upload/upload.page').then((m) => m.UploadPage),
    title: 'Cityfy · Carica export',
  },
  {
    path: 'admin/login',
    loadComponent: () => import('./features/admin/admin-login.page').then((m) => m.AdminLoginPage),
    title: 'Cityfy · Admin',
  },
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin/admin.page').then((m) => m.AdminPage),
    title: 'Cityfy · Admin',
  },
  { path: '**', redirectTo: 'city' },
];
