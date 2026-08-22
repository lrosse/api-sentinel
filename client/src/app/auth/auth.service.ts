import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface CurrentUser {
  id: string;
  email: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly currentUser = signal<CurrentUser | null>(null);

  register(email: string, password: string): Observable<CurrentUser> {
    return this.http.post<CurrentUser>('/auth/register', { email, password });
  }

  login(email: string, password: string): Observable<CurrentUser> {
    return this.http
      .post<CurrentUser>('/auth/login', { email, password })
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  me(): Observable<CurrentUser> {
    return this.http
      .get<CurrentUser>('/auth/me')
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  logout(): Observable<void> {
    return this.http
      .post<void>('/auth/logout', null)
      .pipe(tap(() => this.currentUser.set(null)));
  }
}
