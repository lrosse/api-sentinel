import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { apiErrorMessage } from '../shared/api-error';
import { IncidentListItem, IncidentStatus } from './incidents.models';
import { IncidentsService } from './incidents.service';

@Component({
  selector: 'app-incidents',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './incidents.html',
})
export class Incidents implements OnInit {
  private readonly incidentsService = inject(IncidentsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly incidents = signal<IncidentListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;
  protected statusFilter: '' | IncidentStatus = '';

  ngOnInit(): void {
    this.load();
  }

  protected filterChanged(): void {
    this.load();
  }

  protected statusLabel(status: IncidentStatus): string {
    switch (status) {
      case 'Open':
        return 'Aberto';
      case 'Recovered':
        return 'Recuperado';
      case 'Resolved':
        return 'Resolvido';
    }
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
    this.incidentsService
      .list(this.statusFilter || undefined)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (incidents) => this.incidents.set(incidents),
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível carregar os incidentes.')),
      });
  }
}
