import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { apiErrorMessage } from '../shared/api-error';
import { ApiServiceInput } from './catalog.models';
import { CatalogService } from './catalog.service';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, RouterLink],
  templateUrl: './catalog.html',
})
export class Catalog implements OnInit {
  private readonly catalog = inject(CatalogService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly services = signal<import('./catalog.models').ApiService[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;

  protected name = '';
  protected description = '';
  protected tags = '';
  protected baseUrl = '';

  ngOnInit(): void {
    this.loadServices();
  }

  protected createService(): void {
    this.error.set(null);
    this.saving.set(true);
    this.catalog
      .createService(this.serviceInput())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (apiService) => {
          this.services.update((services) =>
            [...services, apiService].sort((a, b) => a.name.localeCompare(b.name)),
          );
          this.name = '';
          this.description = '';
          this.tags = '';
          this.baseUrl = '';
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível criar a API.')),
      });
  }

  protected deleteService(id: string, name: string): void {
    if (!window.confirm(`Excluir “${name}” e todos os endpoints associados?`)) {
      return;
    }

    this.catalog.deleteService(id).subscribe({
      next: () => this.services.update((services) => services.filter((item) => item.id !== id)),
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

  private loadServices(): void {
    this.loading.set(true);
    this.catalog
      .listServices()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (services) => this.services.set(services),
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível carregar o catálogo.')),
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
}
