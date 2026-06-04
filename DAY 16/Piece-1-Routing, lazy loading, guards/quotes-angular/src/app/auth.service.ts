import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http      = inject(HttpClient);
  private readonly TOKEN_KEY = 'auth_token';

  readonly token      = signal<string | null>(localStorage.getItem(this.TOKEN_KEY));
  readonly isLoggedIn = computed(() => this.token() !== null);

  // Decode the JWT payload to get the logged-in user's email
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
    return this.http.post<LoginResponse>('/api/auth/login', { email, password }).pipe(
      tap(res => {
        localStorage.setItem(this.TOKEN_KEY, res.access_token);
        this.token.set(res.access_token);
      })
    );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.token.set(null);
  }
}
