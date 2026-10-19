import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const expectedRole = route.data?.['role'] as string | undefined;
  if (expectedRole && !authService.hasRole(expectedRole)) {
    router.navigate(['/']);
    return false;
  }

  return true;
};
