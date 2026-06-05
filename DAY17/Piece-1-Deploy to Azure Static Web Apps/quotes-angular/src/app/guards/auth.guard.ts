import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);

  return auth.checkAuth().pipe(
    map(isAuth => {
      if (isAuth) return true;
      auth.login(); // redirects to /.auth/login/aad
      return false;
    })
  );
};
