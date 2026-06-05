import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../environments/environment';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http       = inject(HttpClient);
  private readonly TOKEN_KEY  = 'auth_token';
  private readonly base       = environment.apiBase;
  private readonly isBrowser  = isPlatformBrowser(inject(PLATFORM_ID));

  readonly token      = signal<string | null>(
    this.isBrowser ? localStorage.getItem(this.TOKEN_KEY) : null
  );
  readonly isLoggedIn = computed(() => this.token() !== null);

  readonly currentUserEmail = computed<string | null>(() => {
    const token = this.token();
    if (!token) return null;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return (
        payload['email'] ??
        payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] ??
        payload['sub'] ??
        null
      );
    } catch {
      return null;
    }
  });

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.base}/api/auth/login`, { email, password }).pipe(
      tap(res => {
        if (this.isBrowser) localStorage.setItem(this.TOKEN_KEY, res.access_token);
        this.token.set(res.access_token);
      })
    );
  }

  logout(): void {
    if (this.isBrowser) localStorage.removeItem(this.TOKEN_KEY);
    this.token.set(null);
  }
}
