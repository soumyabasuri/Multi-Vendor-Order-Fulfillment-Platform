import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { OrderDto } from '../../../core/models/models';

@Component({
  selector: 'app-order-tracking',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="container tracking-container">
      <div *ngIf="loading" class="text-center py-5">
        <p>Retrieving order details...</p>
      </div>

      <div *ngIf="!loading && !order" class="card text-center py-5">
        <h2>Order Not Found</h2>
        <a routerLink="/products" class="btn btn-primary mt-3">Back to Products</a>
      </div>

      <div *ngIf="!loading && order">
        <div class="card order-summary-card">
          <div class="order-header">
            <div>
              <span class="text-muted">Order Reference:</span>
              <h1 class="order-number">{{ order.orderNumber }}</h1>
              <span class="order-date">Placed on {{ order.createdAtUtc | date:'medium' }}</span>
            </div>
            <div class="text-right">
              <span [ngClass]="getStatusBadgeClass(order.status)" class="badge badge-lg">
                {{ order.status }}
              </span>
              <div class="order-total-price mt-2">\${{ order.totalAmount.toFixed(2) }}</div>
            </div>
          </div>
        </div>

        <h2 class="section-title">Multi-Vendor Fulfillment Breakdown</h2>
        <p class="section-subtitle">
          Because items originate from separate independent sellers, your order is split into separate vendor fulfillment packages:
        </p>

        <!-- Vendor Orders Cards -->
        <div *ngFor="let vo of order.vendorOrders" class="card vendor-fulfillment-card">
          <div class="card-header">
            <div>
              <span class="badge badge-neutral">Vendor: {{ vo.vendorName }}</span>
              <h3 class="vendor-order-title">Package Fulfillment ID: {{ vo.id.substring(0, 8) }}</h3>
            </div>
            <span [ngClass]="getStatusBadgeClass(vo.status)" class="badge">
              {{ vo.status }}
            </span>
          </div>

          <div class="shipment-info" *ngIf="vo.trackingNumber">
            <span class="tracking-pill">Carrier Tracking: <strong>{{ vo.trackingNumber }}</strong></span>
            <span *ngIf="vo.shippedAtUtc" class="text-muted text-sm">Shipped on: {{ vo.shippedAtUtc | date:'short' }}</span>
          </div>

          <!-- Items in this vendor order -->
          <div class="table-responsive mt-3">
            <table class="table">
              <thead>
                <tr>
                  <th>Product Name</th>
                  <th>SKU</th>
                  <th>Quantity</th>
                  <th>Unit Price</th>
                  <th>Subtotal</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let item of vo.items">
                  <td><strong>{{ item.productName }}</strong></td>
                  <td><code>{{ item.sku }}</code></td>
                  <td>{{ item.quantity }}</td>
                  <td>\${{ item.unitPrice.toFixed(2) }}</td>
                  <td>\${{ item.totalPrice.toFixed(2) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Shipping Address Details -->
        <div class="card">
          <h3 class="card-title">Destination Delivery Address</h3>
          <p>
            {{ order.shippingAddress.recipientName }} ({{ order.shippingAddress.recipientPhone }})<br>
            {{ order.shippingAddress.street }}<br>
            {{ order.shippingAddress.city }}, {{ order.shippingAddress.state }} {{ order.shippingAddress.postalCode }}<br>
            {{ order.shippingAddress.country }}
          </p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .tracking-container { max-width: 900px; }
    .order-summary-card { padding: 1.5rem; }
    .order-header { display: flex; justify-content: space-between; align-items: flex-start; }
    .order-number { font-size: 1.5rem; font-weight: 700; margin: 0.25rem 0; }
    .order-date { font-size: 0.85rem; color: var(--text-muted); }
    .badge-lg { font-size: 0.9rem; padding: 0.35rem 0.8rem; }
    .order-total-price { font-size: 1.5rem; font-weight: 700; color: var(--primary); }
    .section-title { font-size: 1.25rem; font-weight: 600; margin-top: 2rem; margin-bottom: 0.25rem; }
    .section-subtitle { font-size: 0.85rem; color: var(--text-muted); margin-bottom: 1rem; }
    .vendor-fulfillment-card { margin-bottom: 1.5rem; }
    .vendor-order-title { font-size: 1rem; font-weight: 600; margin-top: 0.35rem; }
    .shipment-info { display: flex; gap: 1rem; align-items: center; padding: 0.5rem 0; }
    .tracking-pill { background: var(--primary-light); color: var(--primary); padding: 0.25rem 0.6rem; border-radius: 4px; font-size: 0.85rem; }
    .text-sm { font-size: 0.8rem; }
    .text-right { text-align: right; }
  `]
})
export class OrderTrackingComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private api = inject(ApiService);

  order: OrderDto | null = null;
  loading = true;

  ngOnInit() {
    const orderId = this.route.snapshot.paramMap.get('id');
    if (orderId) {
      this.loadOrder(orderId);
    }
  }

  loadOrder(orderId: string) {
    this.loading = true;
    this.api.get<OrderDto>(`orders/${orderId}`).subscribe({
      next: (order) => {
        this.order = order;
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
      case 'Preparing':
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
