import { Routes } from '@angular/router';

/**
 * Feature routes are lazy-loaded standalone components. Placeholder sections reuse
 * ComingSoon and receive their heading via route `data.title` (bound to the `title` input
 * by withComponentInputBinding in app.config).
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    title: 'Dashboard · Vizfolio',
    loadComponent: () =>
      import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'portfolios',
    title: 'Portfolios · Vizfolio',
    loadComponent: () =>
      import('./features/portfolios/portfolios').then((m) => m.Portfolios),
  },
  {
    path: 'accounts',
    title: 'Accounts · Vizfolio',
    loadComponent: () =>
      import('./features/accounts/accounts').then((m) => m.Accounts),
  },
  {
    path: 'accounts/:accountId',
    title: 'Account · Vizfolio',
    loadComponent: () =>
      import('./features/accounts/detail/account').then((m) => m.Account),
  },
  {
    path: 'performance',
    title: 'Performance · Vizfolio',
    loadComponent: () =>
      import('./features/performance/performance').then((m) => m.Performance),
  },
  {
    path: 'settings',
    title: 'Settings · Vizfolio',
    loadComponent: () =>
      import('./features/settings/settings').then((m) => m.Settings),
  },
  { path: '**', redirectTo: 'dashboard' },
];
