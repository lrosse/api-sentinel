import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { Login } from './auth/login';
import { Register } from './auth/register';
import { Catalog } from './catalog/catalog';
import { ApiServiceDetail } from './catalog/api-service-detail';
import { EndpointDetail } from './monitoring/endpoint-detail';
import { Dashboard } from './dashboard/dashboard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'catalog', component: Catalog, canActivate: [authGuard] },
  { path: 'dashboard', component: Dashboard, canActivate: [authGuard] },
  { path: 'api-services/:id', component: ApiServiceDetail, canActivate: [authGuard] },
  { path: 'endpoints/:id', component: EndpointDetail, canActivate: [authGuard] },
  { path: '**', redirectTo: 'dashboard' },
];
