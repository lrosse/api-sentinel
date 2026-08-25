import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { apiErrorMessage } from '../shared/api-error';
import { IncidentDetail, IncidentEventType, IncidentStatus } from './incidents.models';
import { IncidentsService } from './incidents.service';

@Component({
  selector: 'app-incident-detail',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './incident-detail.html',
})
export class IncidentDetailPage implements OnInit {
  private readonly incidentsService = inject(IncidentsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly incidentId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly incident = signal<IncidentDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly resolving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;
  protected rootCause = '';

  ngOnInit(): void {
    this.load();
  }

  protected resolve(): void {
    this.resolving.set(true);
    this.error.set(null);
    this.incidentsService
      .resolve(this.incidentId, { rootCause: this.rootCause.trim() || null })
      .pipe(finalize(() => this.resolving.set(false)))
      .subscribe({
        next: (incident) => {
          this.incident.set(incident);
          this.rootCause = incident.rootCause ?? '';
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível resolver o incidente.')),
      });
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

  protected eventLabel(eventType: IncidentEventType): string {
    switch (eventType) {
      case 'Opened':
        return 'Incidente aberto';
      case 'EvidenceAdded':
        return 'Evidência adicionada';
      case 'Recovered':
        return 'Recuperação automática';
      case 'ResolvedManually':
        return 'Resolução confirmada';
      case 'CommentAdded':
        return 'Comentário';
    }
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }

  private load(): void {
    this.incidentsService
      .get(this.incidentId)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (incident) => {
          this.incident.set(incident);
          this.rootCause = incident.rootCause ?? '';
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Incidente não encontrado ou indisponível.')),
      });
  }
}
