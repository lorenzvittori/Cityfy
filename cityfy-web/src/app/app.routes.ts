import { Routes } from '@angular/router';
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
  { path: '**', redirectTo: 'city' },
];
