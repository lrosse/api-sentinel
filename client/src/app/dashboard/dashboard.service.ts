import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { DashboardApiService } from './dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getSummary(): Observable<DashboardApiService[]> {
    return this.http.get<DashboardApiService[]>('/dashboard/summary');
  }
}
