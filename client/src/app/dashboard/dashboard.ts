import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { apiErrorMessage } from '../shared/api-error';
import { DashboardApiService } from './dashboard.models';
import { DashboardService } from './dashboard.service';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit {
  private readonly dashboard = inject(DashboardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly apiServices = signal<DashboardApiService[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;

  ngOnInit(): void {
    this.load();
  }

  protected refresh(): void {
    this.load();
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.dashboard
      .getSummary()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (apiServices) => this.apiServices.set(apiServices),
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível carregar o dashboard.')),
      });
  }
}
