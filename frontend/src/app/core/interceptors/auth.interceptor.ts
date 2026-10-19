import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = localStorage.getItem('access_token');

  let authReq = req;
  if (token && !req.headers.has('Authorization')) {
    authReq = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      const isAuthEndpoint = req.url.includes('auth/login') ||
                             req.url.includes('auth/register') ||
                             req.url.includes('auth/refresh-token');

      if (error.status === 401 && !isAuthEndpoint) {
        const storedRefreshToken = localStorage.getItem('refresh_token');
        if (storedRefreshToken) {
          return authService.refreshToken().pipe(
            switchMap(newAuth => {
              const retriedReq = req.clone({
                setHeaders: {
                  Authorization: `Bearer ${newAuth.accessToken}`
                }
              });
              return next(retriedReq);
            }),
            catchError(refreshErr => {
              authService.logout();
              alert('Your session has expired. Please sign in again.');
              return throwError(() => refreshErr);
            })
          );
        } else {
          authService.logout();
          alert('Please sign in to continue.');
        }
      }

      return throwError(() => error);
    })
  );
};
