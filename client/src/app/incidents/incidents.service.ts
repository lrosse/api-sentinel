import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  IncidentDetail,
  IncidentListItem,
  IncidentStatus,
  ResolveIncidentInput,
} from './incidents.models';

@Injectable({ providedIn: 'root' })
export class IncidentsService {
  private readonly http = inject(HttpClient);

  list(status?: IncidentStatus): Observable<IncidentListItem[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<IncidentListItem[]>('/incidents', { params });
  }

  get(id: string): Observable<IncidentDetail> {
    return this.http.get<IncidentDetail>(`/incidents/${id}`);
  }

  resolve(id: string, input: ResolveIncidentInput): Observable<IncidentDetail> {
    return this.http.post<IncidentDetail>(`/incidents/${id}/resolve`, input);
  }
}
