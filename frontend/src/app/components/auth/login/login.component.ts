import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="auth-wrapper">
      <div class="card auth-card">
        <h1 class="auth-title">Sign In</h1>
        <p class="auth-subtitle">Access your customer or seller dashboard.</p>

        <form (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label class="form-label">Email Address</label>
            <input type="email" [(ngModel)]="email" name="email" required class="form-control" placeholder="user@platform.local">
          </div>

          <div class="form-group">
            <label class="form-label">Password</label>
            <input type="password" [(ngModel)]="password" name="password" required class="form-control" placeholder="••••••••">
          </div>

          <button [disabled]="loading" type="submit" class="btn btn-primary btn-block">
            {{ loading ? 'Authenticating...' : 'Sign In' }}
          </button>
        </form>

        <div class="auth-footer mt-3 text-center">
          <span class="text-muted text-sm">Don't have an account? </span>
          <a routerLink="/register" class="text-sm">Create Account</a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-wrapper {
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: calc(100vh - 150px);
      padding: 1.5rem;
    }
    .auth-card {
      width: 100%;
      max-width: 400px;
      padding: 2rem;
    }
    .auth-title { font-size: 1.5rem; font-weight: 700; margin-bottom: 0.25rem; }
    .auth-subtitle { font-size: 0.85rem; color: var(--text-muted); margin-bottom: 1.5rem; }
    .btn-block { width: 100%; padding: 0.75rem; margin-top: 1rem; }
    .mt-3 { margin-top: 1rem; }
    .text-center { text-align: center; }
    .text-sm { font-size: 0.85rem; }
  `]
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  email = 'customer@platform.local';
  password = 'Password123!';
  loading = false;

  onSubmit() {
    this.loading = true;
    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: (res) => {
        this.loading = false;
        const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading = false;
        alert(err?.error?.errorMessage || 'Login failed. Check your credentials.');
      }
    });
  }
}
