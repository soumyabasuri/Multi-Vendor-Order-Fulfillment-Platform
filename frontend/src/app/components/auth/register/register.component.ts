import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="auth-wrapper">
      <div class="card auth-card">
        <h1 class="auth-title">Register Account</h1>
        <p class="auth-subtitle">Join as a Customer or apply as a Marketplace Seller.</p>

        <form (ngSubmit)="onSubmit()">
          <div class="grid-2">
            <div class="form-group">
              <label class="form-label">First Name</label>
              <input type="text" [(ngModel)]="form.firstName" name="firstName" required class="form-control" placeholder="John">
            </div>
            <div class="form-group">
              <label class="form-label">Last Name</label>
              <input type="text" [(ngModel)]="form.lastName" name="lastName" required class="form-control" placeholder="Doe">
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">Email Address</label>
            <input type="email" [(ngModel)]="form.email" name="email" required class="form-control" placeholder="john@example.com">
          </div>

          <div class="form-group">
            <label class="form-label">Password</label>
            <input type="password" [(ngModel)]="form.password" name="password" required class="form-control" placeholder="At least 8 chars, 1 upper, 1 number, 1 special">
          </div>

          <div class="form-group">
            <label class="form-label">Account Role</label>
            <select [(ngModel)]="form.role" name="role" class="form-control">
              <option value="Customer">Customer (Shop & Purchase)</option>
              <option value="Vendor">Vendor (Sell & Fulfill)</option>
            </select>
          </div>

          <button [disabled]="loading" type="submit" class="btn btn-primary btn-block">
            {{ loading ? 'Creating Account...' : 'Register' }}
          </button>
        </form>

        <div class="auth-footer mt-3 text-center">
          <span class="text-muted text-sm">Already have an account? </span>
          <a routerLink="/login" class="text-sm">Sign In</a>
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
      max-width: 460px;
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
export class RegisterComponent {
  private auth = inject(AuthService);
  private router = inject(Router);

  form = {
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    role: 'Customer'
  };
  loading = false;

  onSubmit() {
    this.loading = true;
    this.auth.register(this.form).subscribe({
      next: () => {
        this.loading = false;
        alert('Registration successful!');
        this.router.navigate(['/']);
      },
      error: (err) => {
        this.loading = false;
        const msg = err?.error?.errorMessage || err?.error?.title || err?.message || 'Registration failed.';
        alert(msg);
      }
    });
  }
}
