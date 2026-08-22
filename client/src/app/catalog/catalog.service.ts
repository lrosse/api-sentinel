import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiEndpoint, ApiService, ApiServiceInput, EndpointInput } from './catalog.models';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  listServices(): Observable<ApiService[]> {
    return this.http.get<ApiService[]>('/api-services');
  }

  getService(id: string): Observable<ApiService> {
    return this.http.get<ApiService>(`/api-services/${id}`);
  }

  createService(input: ApiServiceInput): Observable<ApiService> {
    return this.http.post<ApiService>('/api-services', input);
  }

  updateService(id: string, input: ApiServiceInput): Observable<ApiService> {
    return this.http.put<ApiService>(`/api-services/${id}`, input);
  }

  deleteService(id: string): Observable<void> {
    return this.http.delete<void>(`/api-services/${id}`);
  }

  listEndpoints(apiServiceId: string): Observable<ApiEndpoint[]> {
    return this.http.get<ApiEndpoint[]>(`/api-services/${apiServiceId}/endpoints`);
  }

  createEndpoint(apiServiceId: string, input: EndpointInput): Observable<ApiEndpoint> {
    return this.http.post<ApiEndpoint>(`/api-services/${apiServiceId}/endpoints`, input);
  }

  updateEndpoint(id: string, input: EndpointInput): Observable<ApiEndpoint> {
    return this.http.put<ApiEndpoint>(`/endpoints/${id}`, input);
  }

  deleteEndpoint(id: string): Observable<void> {
    return this.http.delete<void>(`/endpoints/${id}`);
  }
}
