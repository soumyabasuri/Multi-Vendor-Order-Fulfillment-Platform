import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <nav class="navbar">
      <div class="nav-container">
        <a routerLink="/" class="brand">
          <span class="brand-badge">MVP</span>
          <span class="brand-text">MultiVendor Platform</span>
        </a>

        <div class="nav-links">
          <a routerLink="/products" routerLinkActive="active" class="nav-item">Browse Catalog</a>

          <ng-container *ngIf="auth.isAuthenticated()">
            <a routerLink="/cart" routerLinkActive="active" class="nav-item">Cart</a>
            <a routerLink="/my-orders" routerLinkActive="active" class="nav-item">My Orders</a>

            <ng-container *ngIf="auth.isVendor()">
              <a routerLink="/vendor/dashboard" routerLinkActive="active" class="nav-item">Vendor Hub</a>
              <a routerLink="/vendor/orders" routerLinkActive="active" class="nav-item">Fulfillment</a>
            </ng-container>

            <ng-container *ngIf="auth.isAdmin()">
              <a routerLink="/admin/dashboard" routerLinkActive="active" class="nav-item">Admin Dashboard</a>
            </ng-container>
          </ng-container>
        </div>

        <div class="nav-actions">
          <ng-container *ngIf="auth.isAuthenticated(); else guestBlock">
            <span class="user-greeting">
              {{ auth.currentUser()?.firstName }}
              <span class="role-tag">{{ auth.currentUser()?.roles?.[0] }}</span>
            </span>
            <button (click)="auth.logout()" class="btn btn-secondary btn-sm">Sign Out</button>
          </ng-container>
          <ng-template #guestBlock>
            <a routerLink="/login" class="btn btn-secondary btn-sm">Sign In</a>
            <a routerLink="/register" class="btn btn-primary btn-sm">Register</a>
          </ng-template>
        </div>
      </div>
    </nav>
  `,
  styles: [`
    .navbar {
      background: #ffffff;
      border-bottom: 1px solid var(--border);
      position: sticky;
      top: 0;
      z-index: 100;
      box-shadow: 0 1px 2px 0 rgb(0 0 0 / 0.05);
    }
    .nav-container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0.75rem 1.5rem;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      text-decoration: none;
      font-weight: 700;
      font-size: 1.1rem;
      color: var(--text-main);
    }
    .brand-badge {
      background: var(--primary);
      color: #fff;
      font-size: 0.75rem;
      padding: 0.2rem 0.5rem;
      border-radius: 4px;
      letter-spacing: 0.05em;
    }
    .nav-links {
      display: flex;
      gap: 1.5rem;
    }
    .nav-item {
      text-decoration: none;
      color: var(--text-muted);
      font-weight: 500;
      font-size: 0.875rem;
      transition: color 0.15s;
    }
    .nav-item:hover, .nav-item.active {
      color: var(--primary);
    }
    .nav-actions {
      display: flex;
      align-items: center;
      gap: 1rem;
    }
    .user-greeting {
      font-size: 0.875rem;
      font-weight: 600;
    }
    .role-tag {
      font-size: 0.7rem;
      background: var(--primary-light);
      color: var(--primary);
      padding: 0.15rem 0.4rem;
      border-radius: 4px;
      margin-left: 0.25rem;
      font-weight: 500;
    }
  `]
})
export class NavbarComponent {
  auth = inject(AuthService);
}
