import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { OrderSummaryDto, PagedResult } from '../../../core/models/models';

@Component({
  selector: 'app-order-history',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="container">
      <h1 class="page-title mb-4">My Orders</h1>

      <div *ngIf="loading" class="text-center py-5">
        <p>Loading your orders...</p>
      </div>

      <div *ngIf="!loading && orders.length === 0" class="card text-center py-5">
        <h2>No orders placed yet</h2>
        <p class="text-muted mb-4">You haven't placed any orders in our marketplace yet.</p>
        <div>
          <a routerLink="/products" class="btn btn-primary">Start Browsing</a>
        </div>
      </div>

      <div *ngIf="!loading && orders.length > 0" class="card">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>Order Number</th>
                <th>Date Placed</th>
                <th>Vendors</th>
                <th>Items Count</th>
                <th>Total</th>
                <th>Status</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let order of orders">
                <td><strong>{{ order.orderNumber }}</strong></td>
                <td>{{ order.createdAtUtc | date:'mediumDate' }}</td>
                <td>{{ order.totalVendors }} Seller(s)</td>
                <td>{{ order.totalItems }} unit(s)</td>
                <td><strong>\${{ order.totalAmount.toFixed(2) }}</strong></td>
                <td>
                  <span [ngClass]="getStatusBadgeClass(order.status)" class="badge">
                    {{ order.status }}
                  </span>
                </td>
                <td>
                  <a [routerLink]="['/track-order', order.id]" class="btn btn-secondary btn-sm">
                    View Details
                  </a>
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
  `]
})
export class OrderHistoryComponent implements OnInit {
  private api = inject(ApiService);
  orders: OrderSummaryDto[] = [];
  loading = true;

  ngOnInit() {
    this.api.get<PagedResult<OrderSummaryDto>>('orders/my-orders').subscribe({
      next: (res) => {
        this.orders = res.items;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  getStatusBadgeClass(status: string): string {
    switch (status) {
      case 'Confirmed':
      case 'Delivered':
        return 'badge-success';
      case 'Processing':
      case 'Shipped':
      case 'PartiallyShipped':
        return 'badge-info';
      case 'Pending':
      case 'InventoryReserved':
      case 'PaymentPending':
        return 'badge-warning';
      case 'Cancelled':
      case 'Failed':
        return 'badge-danger';
      default:
        return 'badge-neutral';
    }
  }
}
