import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApiMonitor,
  CheckRun,
  ContractChange,
  MonitorInput,
  SchemaSnapshot,
} from './monitoring.models';

@Injectable({ providedIn: 'root' })
export class MonitoringService {
  private readonly http = inject(HttpClient);

  listMonitors(endpointId: string): Observable<ApiMonitor[]> {
    return this.http.get<ApiMonitor[]>(`/endpoints/${endpointId}/monitors`);
  }

  createMonitor(endpointId: string, input: MonitorInput): Observable<ApiMonitor> {
    return this.http.post<ApiMonitor>(`/endpoints/${endpointId}/monitors`, input);
  }

  updateMonitor(id: string, input: MonitorInput): Observable<ApiMonitor> {
    return this.http.put<ApiMonitor>(`/monitors/${id}`, input);
  }

  deleteMonitor(id: string): Observable<void> {
    return this.http.delete<void>(`/monitors/${id}`);
  }

  runMonitor(id: string): Observable<CheckRun> {
    return this.http.post<CheckRun>(`/monitors/${id}/run`, null);
  }

  listRuns(id: string, limit = 50): Observable<CheckRun[]> {
    return this.http.get<CheckRun[]>(`/monitors/${id}/runs`, { params: { limit } });
  }

  listContractChanges(id: string, limit = 50): Observable<ContractChange[]> {
    return this.http.get<ContractChange[]>(`/monitors/${id}/contract-changes`, {
      params: { limit },
    });
  }

  getLatestSchemaSnapshot(id: string): Observable<SchemaSnapshot | null> {
    return this.http.get<SchemaSnapshot | null>(`/monitors/${id}/schema-snapshot/latest`);
  }
}
