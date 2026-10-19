import { Injectable, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap, throwError } from 'rxjs';
import { ApiService } from './api.service';
import { AuthResponse, UserDto } from '../models/models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly userSignal = signal<UserDto | null>(this.getStoredUser());

  readonly currentUser = this.userSignal.asReadonly();
  readonly isAuthenticated = computed(() => !!this.userSignal());
  readonly isCustomer = computed(() => this.hasRole('Customer'));
  readonly isVendor = computed(() => this.hasRole('Vendor'));
  readonly isAdmin = computed(() => this.hasRole('Admin'));

  constructor(private api: ApiService, private router: Router) {}

  login(credentials: { email: string; password: string }) {
    return this.api.post<AuthResponse>('auth/login', credentials).pipe(
      tap(res => this.setSession(res))
    );
  }

  register(data: any) {
    return this.api.post<AuthResponse>('auth/register', data).pipe(
      tap(res => this.setSession(res))
    );
  }

  refreshToken(): Observable<AuthResponse> {
    const refreshToken = localStorage.getItem('refresh_token');
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token stored.'));
    }

    return this.api.post<AuthResponse>('auth/refresh-token', { refreshToken }).pipe(
      tap(res => this.setSession(res))
    );
  }

  logout() {
    const refreshToken = localStorage.getItem('refresh_token');
    if (refreshToken) {
      this.api.post('auth/revoke-token', { refreshToken }).subscribe({
        error: () => {} // Non-blocking
      });
    }

    localStorage.removeItem('access_token');
    localStorage.removeItem('refresh_token');
    localStorage.removeItem('user_profile');
    this.userSignal.set(null);
    this.router.navigate(['/login']);
  }

  hasRole(role: string): boolean {
    const user = this.userSignal();
    return !!user && user.roles.includes(role);
  }

  private setSession(auth: AuthResponse) {
    localStorage.setItem('access_token', auth.accessToken);
    localStorage.setItem('refresh_token', auth.refreshToken);
    localStorage.setItem('user_profile', JSON.stringify(auth.user));
    this.userSignal.set(auth.user);
  }

  private getStoredUser(): UserDto | null {
    const stored = localStorage.getItem('user_profile');
    if (!stored) return null;
    try {
      return JSON.parse(stored) as UserDto;
    } catch {
      return null;
    }
  }
}
