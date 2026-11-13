import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'cities' },

  { path: 'welcome', loadComponent: () => import('./features/welcome/welcome.page').then((m) => m.WelcomePage), title: 'Cityfy' },
  // old address of the login page (bookmarks)
  { path: 'login', redirectTo: 'welcome' },
  {
    path: 'cities',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cities/cities.page').then((m) => m.CitiesPage),
    title: 'Cityfy · Le tue città',
  },
  {
    path: 'cities/new',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cities/new-city.page').then((m) => m.NewCityPage),
    title: 'Cityfy · Nuova città',
  },
  {
    path: 'cities/:cityId',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cities/city-detail.page').then((m) => m.CityDetailPage),
    title: 'Cityfy · La tua città',
  },
  // old address of the (single) city
  { path: 'city', redirectTo: 'cities' },
  {
    path: 'upload',
    canActivate: [authGuard],
    loadComponent: () => import('./features/upload/upload.page').then((m) => m.UploadPage),
    title: 'Cityfy · Carica export',
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
    title: 'Cityfy · I tuoi dati',
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
  { path: '**', redirectTo: 'cities' },
];
