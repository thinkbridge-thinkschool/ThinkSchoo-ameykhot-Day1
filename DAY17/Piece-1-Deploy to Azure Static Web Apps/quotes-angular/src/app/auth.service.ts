import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, tap } from 'rxjs';

interface SwaClientPrincipal {
  userId: string;
  userRoles: string[];
  claims: Array<{ typ: string; val: string }>;
  identityProvider: string;
  userDetails: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly clientPrincipal  = signal<SwaClientPrincipal | null>(null);
  readonly isLoggedIn        = computed(() => this.clientPrincipal() !== null);
  readonly currentUserEmail  = computed(() => this.clientPrincipal()?.userDetails ?? null);

  checkAuth(): Observable<boolean> {
    return this.http.get<{ clientPrincipal: SwaClientPrincipal | null }>('/.auth/me').pipe(
      tap(res => this.clientPrincipal.set(res.clientPrincipal)),
      map(res => res.clientPrincipal !== null),
      catchError(() => {
        // /.auth/me is unavailable in plain `ng serve` dev — treat as authenticated
        this.clientPrincipal.set({
          userId: 'dev-user',
          userRoles: ['authenticated'],
          claims: [],
          identityProvider: 'dev',
          userDetails: 'dev@localhost',
        });
        return of(true);
      })
    );
  }

  login(): void {
    window.location.href = '/.auth/login/aad?post_login_redirect_uri=/quotes';
  }

  logout(): void {
    this.clientPrincipal.set(null);
    window.location.href = '/.auth/logout?post_logout_redirect_uri=/login';
  }
}
