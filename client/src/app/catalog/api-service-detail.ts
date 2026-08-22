import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { apiErrorMessage } from '../shared/api-error';
import {
  ApiEndpoint,
  ApiService,
  ApiServiceInput,
  EndpointInput,
  EndpointMethod,
} from './catalog.models';
import { CatalogService } from './catalog.service';

@Component({
  selector: 'app-api-service-detail',
  imports: [FormsModule, RouterLink],
  templateUrl: './api-service-detail.html',
})
export class ApiServiceDetail implements OnInit {
  private readonly catalog = inject(CatalogService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly serviceId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly service = signal<ApiService | null>(null);
  protected readonly endpoints = signal<ApiEndpoint[]>([]);
  protected readonly loading = signal(true);
  protected readonly savingService = signal(false);
  protected readonly savingEndpoint = signal(false);
  protected readonly editingEndpointId = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;
  protected readonly methods: EndpointMethod[] = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

  protected name = '';
  protected description = '';
  protected tags = '';
  protected baseUrl = '';
  protected endpointPath = '';
  protected endpointMethod: EndpointMethod = 'GET';

  ngOnInit(): void {
    this.load();
  }

  protected saveService(): void {
    this.error.set(null);
    this.savingService.set(true);
    this.catalog
      .updateService(this.serviceId, this.serviceInput())
      .pipe(finalize(() => this.savingService.set(false)))
      .subscribe({
        next: (apiService) => this.service.set(apiService),
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível atualizar a API.')),
      });
  }

  protected saveEndpoint(): void {
    this.error.set(null);
    this.savingEndpoint.set(true);
    const endpointId = this.editingEndpointId();
    const request = endpointId
      ? this.catalog.updateEndpoint(endpointId, this.endpointInput())
      : this.catalog.createEndpoint(this.serviceId, this.endpointInput());

    request.pipe(finalize(() => this.savingEndpoint.set(false))).subscribe({
      next: (endpoint) => {
        this.endpoints.update((endpoints) => {
          const remaining = endpoints.filter((item) => item.id !== endpoint.id);
          return [...remaining, endpoint].sort((a, b) => a.path.localeCompare(b.path));
        });
        this.cancelEndpointEdit();
      },
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível salvar o endpoint.')),
    });
  }

  protected editEndpoint(endpoint: ApiEndpoint): void {
    this.editingEndpointId.set(endpoint.id);
    this.endpointPath = endpoint.path;
    this.endpointMethod = endpoint.method;
  }

  protected cancelEndpointEdit(): void {
    this.editingEndpointId.set(null);
    this.endpointPath = '';
    this.endpointMethod = 'GET';
  }

  protected deleteEndpoint(endpoint: ApiEndpoint): void {
    if (!window.confirm(`Excluir ${endpoint.method} ${endpoint.path}?`)) {
      return;
    }

    this.catalog.deleteEndpoint(endpoint.id).subscribe({
      next: () =>
        this.endpoints.update((endpoints) => endpoints.filter((item) => item.id !== endpoint.id)),
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível excluir o endpoint.')),
    });
  }

  protected deleteService(): void {
    const apiService = this.service();
    if (!apiService || !window.confirm(`Excluir “${apiService.name}” e todos os endpoints?`)) {
      return;
    }

    this.catalog.deleteService(this.serviceId).subscribe({
      next: () => void this.router.navigate(['/catalog']),
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível excluir a API.')),
    });
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }

  private load(): void {
    forkJoin({
      service: this.catalog.getService(this.serviceId),
      endpoints: this.catalog.listEndpoints(this.serviceId),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ service, endpoints }) => {
          this.service.set(service);
          this.endpoints.set(endpoints);
          this.name = service.name;
          this.description = service.description ?? '';
          this.tags = service.tags.join(', ');
          this.baseUrl = service.baseUrl;
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'API não encontrada ou indisponível.')),
      });
  }

  private serviceInput(): ApiServiceInput {
    return {
      name: this.name,
      description: this.description.trim() || null,
      tags: this.tags
        .split(',')
        .map((tag) => tag.trim())
        .filter(Boolean),
      baseUrl: this.baseUrl,
    };
  }

  private endpointInput(): EndpointInput {
    return { path: this.endpointPath, method: this.endpointMethod };
  }
}
