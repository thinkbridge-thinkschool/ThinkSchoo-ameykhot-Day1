import { Routes } from '@angular/router';
import { QuotesListComponent } from './quotes-list/quotes-list.component';

export const routes: Routes = [
  { path: '', redirectTo: 'quotes', pathMatch: 'full' },
  { path: 'quotes', component: QuotesListComponent },
  {
    path: 'quotes/:id',
    loadComponent: () =>
      import('./quote-detail/quote-detail.component').then(m => m.QuoteDetailComponent),
  },
  {
    path: 'create-quote',
    loadComponent: () =>
      import('./create-quote-page/create-quote-page.component').then(m => m.CreateQuotePageComponent),
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./not-found/not-found.component').then(m => m.NotFoundComponent),
  },
];
