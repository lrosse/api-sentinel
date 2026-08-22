import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiMonitor, CheckRun, MonitorInput } from './monitoring.models';

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
}
