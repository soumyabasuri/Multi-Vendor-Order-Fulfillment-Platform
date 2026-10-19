import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { VendorDto, CategoryDto } from '../../../core/models/models';

@Component({
  selector: 'app-vendor-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="container">
      <div class="vendor-header">
        <div>
          <h1 class="page-title">Vendor Seller Hub</h1>
          <p class="page-subtitle">Manage catalog items, monitor inventory, and fulfill assigned orders.</p>
        </div>
        <div *ngIf="vendor">
          <span [ngClass]="vendor.status === 'Approved' ? 'badge-success' : 'badge-warning'" class="badge badge-lg">
            Status: {{ vendor.status }}
          </span>
        </div>
      </div>

      <!-- Stats Overview Cards -->
      <div *ngIf="vendor" class="grid-3 mb-4">
        <div class="card stat-card">
          <span class="stat-label">Business Name</span>
          <span class="stat-value">{{ vendor.businessName }}</span>
          <span class="stat-sub">Owner Account: {{ vendor.contactEmail }}</span>
        </div>
        <div class="card stat-card">
          <span class="stat-label">Platform Commission</span>
          <span class="stat-value">{{ (vendor.commissionRate * 100).toFixed(1) }}%</span>
          <span class="stat-sub">Net Payout: {{ ((1 - vendor.commissionRate) * 100).toFixed(1) }}%</span>
        </div>
        <div class="card stat-card">
          <span class="stat-label">Quick Actions</span>
          <div class="mt-2">
            <a routerLink="/vendor/orders" class="btn btn-primary btn-sm">View Fulfillment Orders</a>
          </div>
        </div>
      </div>

      <!-- Add New Product Form -->
      <div class="card">
        <h2 class="card-title">List New Product in Catalog</h2>
        <p class="text-muted mb-3" style="font-size: 0.85rem;">
          Once added, inventory will be automatically tracked in the high-concurrency reservation engine.
        </p>

        <form (ngSubmit)="createProduct()">
          <div class="grid-2">
            <div class="form-group">
              <label class="form-label">Product Name</label>
              <input type="text" [(ngModel)]="newProduct.name" name="name" required class="form-control" placeholder="e.g. Ergonomic Office Chair">
            </div>
            <div class="form-group">
              <label class="form-label">Category</label>
              <select [(ngModel)]="newProduct.categoryId" name="categoryId" required class="form-control">
                <option value="">Select Category</option>
                <option *ngFor="let c of categories" [value]="c.id">{{ c.name }}</option>
              </select>
            </div>
          </div>

          <div class="grid-3">
            <div class="form-group">
              <label class="form-label">Stock Keeping Unit (SKU)</label>
              <input type="text" [(ngModel)]="newProduct.sku" name="sku" required class="form-control" placeholder="CHAIR-BLK-01">
            </div>
            <div class="form-group">
              <label class="form-label">Base Retail Price ($)</label>
              <input type="number" step="0.01" [(ngModel)]="newProduct.basePrice" name="basePrice" required class="form-control" placeholder="199.99">
            </div>
            <div class="form-group">
              <label class="form-label">Initial Physical Inventory</label>
              <input type="number" [(ngModel)]="newProduct.initialStock" name="initialStock" required class="form-control" placeholder="50">
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">Description</label>
            <textarea [(ngModel)]="newProduct.description" name="description" rows="3" class="form-control" placeholder="Provide detailed technical specifications and features..."></textarea>
          </div>

          <button [disabled]="saving" type="submit" class="btn btn-primary">
            {{ saving ? 'Publishing Product...' : 'Publish Product to Catalog' }}
          </button>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .vendor-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .badge-lg { font-size: 0.9rem; padding: 0.4rem 0.8rem; }
    .stat-card { padding: 1.25rem; }
    .stat-label { font-size: 0.75rem; text-transform: uppercase; font-weight: 600; color: var(--text-muted); }
    .stat-value { font-size: 1.5rem; font-weight: 700; color: var(--text-main); margin: 0.25rem 0; }
    .stat-sub { font-size: 0.8rem; color: var(--text-muted); }
    .mb-4 { margin-bottom: 1.5rem; }
    .mb-3 { margin-bottom: 1rem; }
    .mt-2 { margin-top: 0.5rem; }
  `]
})
export class VendorDashboardComponent implements OnInit {
  private api = inject(ApiService);

  vendor: VendorDto | null = null;
  categories: CategoryDto[] = [];
  saving = false;

  newProduct = {
    name: '',
    categoryId: '',
    sku: '',
    basePrice: 49.99,
    initialStock: 25,
    description: ''
  };

  ngOnInit() {
    this.api.get<VendorDto>('vendors/me').subscribe({
      next: (v) => this.vendor = v,
      error: () => {}
    });

    this.api.get<CategoryDto[]>('categories').subscribe({
      next: (c) => this.categories = c,
      error: () => {}
    });
  }

  createProduct() {
    this.saving = true;
    this.api.post('products', this.newProduct).subscribe({
      next: () => {
        this.saving = false;
        alert('Product published successfully!');
        this.newProduct = {
          name: '',
          categoryId: this.categories[0]?.id || '',
          sku: '',
          basePrice: 49.99,
          initialStock: 25,
          description: ''
        };
      },
      error: (err) => {
        this.saving = false;
        alert(err?.error?.errorMessage || 'Failed to publish product.');
      }
    });
  }
}
