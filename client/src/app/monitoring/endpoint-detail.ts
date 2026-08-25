import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, map, switchMap } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ApiEndpoint, ApiService } from '../catalog/catalog.models';
import { CatalogService } from '../catalog/catalog.service';
import { apiErrorMessage } from '../shared/api-error';
import {
  ApiMonitor,
  CheckRun,
  ContractChange,
  ContractChangeType,
  MonitorInput,
  SchemaSnapshot,
} from './monitoring.models';
import { MonitoringService } from './monitoring.service';

@Component({
  selector: 'app-endpoint-detail',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './endpoint-detail.html',
})
export class EndpointDetail implements OnInit {
  private readonly catalog = inject(CatalogService);
  private readonly monitoring = inject(MonitoringService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly endpointId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly endpoint = signal<ApiEndpoint | null>(null);
  protected readonly apiService = signal<ApiService | null>(null);
  protected readonly monitors = signal<ApiMonitor[]>([]);
  protected readonly runsByMonitor = signal<Record<string, CheckRun[]>>({});
  protected readonly latestResultByMonitor = signal<Record<string, CheckRun>>({});
  protected readonly contractChangesByMonitor = signal<Record<string, ContractChange[]>>({});
  protected readonly latestSnapshotByMonitor = signal<Record<string, SchemaSnapshot | null>>({});
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly editingMonitorId = signal<string | null>(null);
  protected readonly runningMonitorIds = signal<ReadonlySet<string>>(new Set());
  protected readonly error = signal<string | null>(null);
  protected readonly currentUser = this.auth.currentUser;
  protected readonly intervalOptions = [60, 300, 900, 1800, 3600, 21600, 43200, 86400];

  protected timeoutMs = 5_000;
  protected expectedStatusCode = 200;
  protected maxLatencyMs: number | null = null;
  protected consecutiveFailuresThreshold = 3;
  protected intervalSeconds = 300;
  protected enabled = true;
  protected ignoredPaths = '';

  ngOnInit(): void {
    this.load();
  }

  protected saveMonitor(): void {
    this.error.set(null);
    this.saving.set(true);
    const monitorId = this.editingMonitorId();
    const request = monitorId
      ? this.monitoring.updateMonitor(monitorId, this.monitorInput())
      : this.monitoring.createMonitor(this.endpointId, this.monitorInput());

    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (monitor) => {
        this.monitors.update((monitors) => {
          const remaining = monitors.filter((item) => item.id !== monitor.id);
          return [...remaining, monitor].sort((a, b) => a.timeoutMs - b.timeoutMs);
        });
        if (!monitorId) {
          this.runsByMonitor.update((runs) => ({ ...runs, [monitor.id]: [] }));
          this.contractChangesByMonitor.update((changes) => ({ ...changes, [monitor.id]: [] }));
          this.latestSnapshotByMonitor.update((snapshots) => ({
            ...snapshots,
            [monitor.id]: null,
          }));
        }
        this.cancelEdit();
      },
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível salvar o monitor.')),
    });
  }

  protected editMonitor(monitor: ApiMonitor): void {
    this.editingMonitorId.set(monitor.id);
    this.timeoutMs = monitor.timeoutMs;
    this.expectedStatusCode = monitor.expectedStatusCode;
    this.maxLatencyMs = monitor.maxLatencyMs;
    this.consecutiveFailuresThreshold = monitor.consecutiveFailuresThreshold;
    this.intervalSeconds = monitor.intervalSeconds;
    this.enabled = monitor.enabled;
    this.ignoredPaths = monitor.ignoredPaths.join(', ');
  }

  protected cancelEdit(): void {
    this.editingMonitorId.set(null);
    this.timeoutMs = 5_000;
    this.expectedStatusCode = 200;
    this.maxLatencyMs = null;
    this.consecutiveFailuresThreshold = 3;
    this.intervalSeconds = 300;
    this.enabled = true;
    this.ignoredPaths = '';
  }

  protected deleteMonitor(monitor: ApiMonitor): void {
    if (!window.confirm('Excluir este monitor e todo o histórico de execuções?')) {
      return;
    }

    this.monitoring.deleteMonitor(monitor.id).subscribe({
      next: () => {
        this.monitors.update((monitors) => monitors.filter((item) => item.id !== monitor.id));
        this.runsByMonitor.update((runs) => {
          const updated = { ...runs };
          delete updated[monitor.id];
          return updated;
        });
        this.contractChangesByMonitor.update((changes) => {
          const updated = { ...changes };
          delete updated[monitor.id];
          return updated;
        });
        this.latestSnapshotByMonitor.update((snapshots) => {
          const updated = { ...snapshots };
          delete updated[monitor.id];
          return updated;
        });
        if (this.editingMonitorId() === monitor.id) {
          this.cancelEdit();
        }
      },
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível excluir o monitor.')),
    });
  }

  protected runNow(monitor: ApiMonitor): void {
    this.error.set(null);
    this.setRunning(monitor.id, true);
    this.monitoring
      .runMonitor(monitor.id)
      .pipe(finalize(() => this.setRunning(monitor.id, false)))
      .subscribe({
        next: (run) => {
          this.latestResultByMonitor.update((results) => ({ ...results, [monitor.id]: run }));
          this.runsByMonitor.update((runs) => ({
            ...runs,
            [monitor.id]: [run, ...(runs[monitor.id] ?? []).filter((item) => item.id !== run.id)],
          }));
          this.loadContract(monitor.id);
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível executar o monitor.')),
      });
  }

  protected runsFor(monitorId: string): CheckRun[] {
    return this.runsByMonitor()[monitorId] ?? [];
  }

  protected latestResultFor(monitorId: string): CheckRun | null {
    return this.latestResultByMonitor()[monitorId] ?? null;
  }

  protected contractChangesFor(monitorId: string): ContractChange[] {
    return this.contractChangesByMonitor()[monitorId] ?? [];
  }

  protected latestContractChangeFor(monitorId: string): ContractChange | null {
    return this.contractChangesFor(monitorId)[0] ?? null;
  }

  protected latestSnapshotFor(monitorId: string): SchemaSnapshot | null {
    return this.latestSnapshotByMonitor()[monitorId] ?? null;
  }

  protected changeTypeLabel(type: ContractChangeType): string {
    switch (type) {
      case 'Added':
        return 'Adicionado';
      case 'Removed':
        return 'Removido';
      case 'TypeChanged':
        return 'Tipo alterado';
    }
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }

  private load(): void {
    this.catalog
      .getEndpoint(this.endpointId)
      .pipe(
        switchMap((endpoint) =>
          forkJoin({
            apiService: this.catalog.getService(endpoint.apiServiceId),
            monitors: this.monitoring.listMonitors(endpoint.id),
          }).pipe(map((result) => ({ endpoint, ...result }))),
        ),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ endpoint, apiService, monitors }) => {
          this.endpoint.set(endpoint);
          this.apiService.set(apiService);
          this.monitors.set(monitors);
          for (const monitor of monitors) {
            this.loadRuns(monitor.id);
            this.loadContract(monitor.id);
          }
        },
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Endpoint não encontrado ou indisponível.')),
      });
  }

  private loadRuns(monitorId: string): void {
    this.monitoring.listRuns(monitorId).subscribe({
      next: (runs) => this.runsByMonitor.update((current) => ({ ...current, [monitorId]: runs })),
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível carregar o histórico.')),
    });
  }

  private loadContract(monitorId: string): void {
    forkJoin({
      changes: this.monitoring.listContractChanges(monitorId),
      snapshot: this.monitoring.getLatestSchemaSnapshot(monitorId),
    }).subscribe({
      next: ({ changes, snapshot }) => {
        this.contractChangesByMonitor.update((current) => ({
          ...current,
          [monitorId]: changes,
        }));
        this.latestSnapshotByMonitor.update((current) => ({
          ...current,
          [monitorId]: snapshot,
        }));
      },
      error: (error: unknown) =>
        this.error.set(apiErrorMessage(error, 'Não foi possível carregar o contrato.')),
    });
  }

  private setRunning(monitorId: string, running: boolean): void {
    this.runningMonitorIds.update((current) => {
      const updated = new Set(current);
      if (running) {
        updated.add(monitorId);
      } else {
        updated.delete(monitorId);
      }
      return updated;
    });
  }

  private monitorInput(): MonitorInput {
    return {
      timeoutMs: Number(this.timeoutMs),
      expectedStatusCode: Number(this.expectedStatusCode),
      maxLatencyMs: this.maxLatencyMs ? Number(this.maxLatencyMs) : null,
      consecutiveFailuresThreshold: Number(this.consecutiveFailuresThreshold),
      intervalSeconds: Number(this.intervalSeconds),
      enabled: this.enabled,
      ignoredPaths: this.ignoredPaths
        .split(',')
        .map((path) => path.trim())
        .filter(Boolean),
    };
  }
}
