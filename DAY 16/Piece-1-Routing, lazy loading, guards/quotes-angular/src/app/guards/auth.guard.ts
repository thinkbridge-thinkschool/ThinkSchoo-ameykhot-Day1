import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth.service';

export const authGuard: CanActivateFn = (_route, _state) => {
  const router = inject(Router);
  const auth   = inject(AuthService);
  const token  = localStorage.getItem('auth_token');

  if (!token) {
    return router.parseUrl('/login?reason=unauthenticated');
  }

  // Decode JWT payload and check expiry
  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    const now     = Math.floor(Date.now() / 1000);
    if (payload.exp && payload.exp < now) {
      auth.logout(); // clears localStorage + updates the AuthService signal
      return router.parseUrl('/login?reason=expired');
    }
  } catch {
    auth.logout();
    return router.parseUrl('/login?reason=unauthenticated');
  }

  return true;
};
