import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { VendorDto, CategoryDto, PagedResult } from '../../../core/models/models';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="container">
      <h1 class="page-title mb-4">Platform Administrator Operations</h1>

      <div class="grid-2 mb-4">
        <!-- New Category Creation Form -->
        <div class="card">
          <h2 class="card-title">Create Product Category</h2>
          <form (ngSubmit)="createCategory()">
            <div class="form-group">
              <label class="form-label">Category Name</label>
              <input type="text" [(ngModel)]="newCategoryName" name="name" required class="form-control" placeholder="e.g. Consumer Electronics">
            </div>
            <div class="form-group">
              <label class="form-label">Description</label>
              <input type="text" [(ngModel)]="newCategoryDesc" name="desc" class="form-control" placeholder="Brief description of products in this category">
            </div>
            <button type="submit" class="btn btn-primary btn-sm">Add Category</button>
          </form>
        </div>

        <!-- System Observability Status -->
        <div class="card">
          <h2 class="card-title">Platform Architecture Diagnostics</h2>
          <div class="diagnostic-item">
            <span>Distributed Messaging:</span>
            <span class="badge badge-success">Apache Kafka Online</span>
          </div>
          <div class="diagnostic-item">
            <span>Transactional Outbox:</span>
            <span class="badge badge-success">Background Poller Active</span>
          </div>
          <div class="diagnostic-item">
            <span>Distributed Cache:</span>
            <span class="badge badge-success">Redis Cache-Aside</span>
          </div>
          <div class="diagnostic-item">
            <span>Concurrency Strategy:</span>
            <span class="badge badge-info">PostgreSQL FOR UPDATE + Atomic</span>
          </div>
        </div>
      </div>

      <!-- Pending Vendor Applications -->
      <div class="card">
        <h2 class="card-title">Vendor Seller Applications</h2>
        <div *ngIf="loading" class="text-center py-4">
          <p>Loading vendor applications...</p>
        </div>

        <div *ngIf="!loading && vendors.length === 0" class="text-center py-4 text-muted">
          No vendor registrations found.
        </div>

        <div *ngIf="!loading && vendors.length > 0" class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>Business Name</th>
                <th>Contact Email</th>
                <th>Phone</th>
                <th>Commission Rate</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let vendor of vendors">
                <td><strong>{{ vendor.businessName }}</strong></td>
                <td>{{ vendor.contactEmail }}</td>
                <td>{{ vendor.contactPhone || 'N/A' }}</td>
                <td>{{ (vendor.commissionRate * 100).toFixed(1) }}%</td>
                <td>
                  <span [ngClass]="vendor.status === 'Approved' ? 'badge-success' : (vendor.status === 'PendingApproval' ? 'badge-warning' : 'badge-danger')" class="badge">
                    {{ vendor.status }}
                  </span>
                </td>
                <td>
                  <div *ngIf="vendor.status === 'PendingApproval'" class="action-buttons">
                    <button (click)="reviewVendor(vendor.id, true)" class="btn btn-success btn-sm">Approve</button>
                    <button (click)="reviewVendor(vendor.id, false)" class="btn btn-danger btn-sm">Reject</button>
                  </div>
                  <span *ngIf="vendor.status !== 'PendingApproval'" class="text-muted text-sm">Reviewed</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .mb-4 { margin-bottom: 1.5rem; }
    .diagnostic-item { display: flex; justify-content: space-between; padding: 0.5rem 0; border-bottom: 1px solid var(--border); font-size: 0.875rem; }
    .action-buttons { display: flex; gap: 0.5rem; }
    .text-sm { font-size: 0.8rem; }
  `]
})
export class AdminDashboardComponent implements OnInit {
  private api = inject(ApiService);

  vendors: VendorDto[] = [];
  loading = true;
  newCategoryName = '';
  newCategoryDesc = '';

  ngOnInit() {
    this.loadVendors();
  }

  loadVendors() {
    this.loading = true;
    this.api.get<PagedResult<VendorDto>>('vendors', { pageNumber: 1, pageSize: 50 }).subscribe({
      next: (res) => {
        this.vendors = res.items;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  reviewVendor(vendorId: string, approved: boolean) {
    this.api.post(`vendors/${vendorId}/review`, { approved }).subscribe({
      next: () => {
        alert(`Vendor ${approved ? 'approved' : 'rejected'} successfully!`);
        this.loadVendors();
      },
      error: () => alert('Failed to review vendor.')
    });
  }

  createCategory() {
    if (!this.newCategoryName) return;

    this.api.post('categories', {
      name: this.newCategoryName,
      description: this.newCategoryDesc
    }).subscribe({
      next: () => {
        alert('Category created successfully!');
        this.newCategoryName = '';
        this.newCategoryDesc = '';
      },
      error: () => alert('Failed to create category.')
    });
  }
}
