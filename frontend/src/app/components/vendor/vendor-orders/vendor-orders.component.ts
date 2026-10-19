import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { VendorOrderDto, PagedResult } from '../../../core/models/models';

@Component({
  selector: 'app-vendor-orders',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="container">
      <h1 class="page-title mb-4">Vendor Fulfillment Orders</h1>

      <div *ngIf="loading" class="text-center py-5">
        <p>Loading fulfillment orders...</p>
      </div>

      <div *ngIf="!loading && orders.length === 0" class="card text-center py-5">
        <h2>No pending fulfillment orders</h2>
        <p class="text-muted">Orders confirmed by customers containing your products will appear here.</p>
      </div>

      <div *ngIf="!loading && orders.length > 0">
        <div *ngFor="let order of orders" class="card order-card">
          <div class="card-header">
            <div>
              <span class="badge badge-neutral">Fulfillment Order: {{ order.id.substring(0, 8) }}</span>
              <div class="order-amounts mt-1">
                Subtotal: <strong>\${{ order.subtotalAmount.toFixed(2) }}</strong> |
                Platform Fee: \${{ order.commissionAmount.toFixed(2) }} |
                Net Payout: <strong class="text-success">\${{ order.payoutAmount.toFixed(2) }}</strong>
              </div>
            </div>

            <div class="text-right">
              <span [ngClass]="getStatusBadgeClass(order.status)" class="badge">
                {{ order.status }}
              </span>
              <div *ngIf="order.trackingNumber" class="text-muted text-sm mt-1">
                Tracking: <code>{{ order.trackingNumber }}</code>
              </div>
            </div>
          </div>

          <!-- Items list -->
          <div class="table-responsive">
            <table class="table">
              <thead>
                <tr>
                  <th>Product</th>
                  <th>SKU</th>
                  <th>Qty</th>
                  <th>Unit Price</th>
                  <th>Total</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let item of order.items">
                  <td><strong>{{ item.productName }}</strong></td>
                  <td><code>{{ item.sku }}</code></td>
                  <td>{{ item.quantity }}</td>
                  <td>\${{ item.unitPrice.toFixed(2) }}</td>
                  <td>\${{ item.totalPrice.toFixed(2) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Action buttons for fulfillment -->
          <div class="fulfillment-actions mt-3">
            <button
              *ngIf="order.status === 'Preparing' || order.status === 'Pending'"
              (click)="openShipModal(order)"
              class="btn btn-primary btn-sm">
              Create Shipment & Dispatch
            </button>

            <span *ngIf="order.status === 'Shipped'" class="badge badge-info">
              In Transit
            </span>

            <span *ngIf="order.status === 'Delivered'" class="badge badge-success">
              Fulfillment Complete
            </span>
          </div>
        </div>
      </div>

      <!-- Quick Shipping Modal -->
      <div *ngIf="selectedOrderForShipment" class="modal-backdrop">
        <div class="card modal-card">
          <h2 class="card-title">Dispatch Package</h2>
          <p class="text-muted mb-3" style="font-size: 0.85rem;">
            Assign carrier and tracking number for Order {{ selectedOrderForShipment.id.substring(0, 8) }}.
          </p>

          <div class="form-group">
            <label class="form-label">Carrier</label>
            <select [(ngModel)]="shippingCarrier" class="form-control">
              <option value="FedEx">FedEx Express</option>
              <option value="UPS">UPS Ground</option>
              <option value="DHL">DHL Express</option>
              <option value="USPS">USPS Priority Mail</option>
            </select>
          </div>

          <div class="modal-actions">
            <button (click)="selectedOrderForShipment = null" class="btn btn-secondary">Cancel</button>
            <button (click)="confirmShipment()" class="btn btn-primary">Dispatch Shipment</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .mb-4 { margin-bottom: 1.5rem; }
    .mt-1 { margin-top: 0.25rem; }
    .mt-3 { margin-top: 0.75rem; }
    .order-card { margin-bottom: 1.5rem; }
    .order-amounts { font-size: 0.9rem; color: var(--text-muted); }
    .text-success { color: #16a34a; }
    .fulfillment-actions { display: flex; justify-content: flex-end; }
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }
    .modal-card { width: 100%; max-width: 480px; box-shadow: var(--shadow-lg); }
    .modal-actions { display: flex; justify-content: flex-end; gap: 0.75rem; margin-top: 1.5rem; }
  `]
})
export class VendorOrdersComponent implements OnInit {
  private api = inject(ApiService);

  orders: VendorOrderDto[] = [];
  loading = true;
  selectedOrderForShipment: VendorOrderDto | null = null;
  shippingCarrier = 'FedEx';

  ngOnInit() {
    this.loadOrders();
  }

  loadOrders() {
    this.loading = true;
    this.api.get<PagedResult<VendorOrderDto>>('orders/vendor-orders').subscribe({
      next: (res) => {
        this.orders = res.items;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  openShipModal(order: VendorOrderDto) {
    this.selectedOrderForShipment = order;
  }

  confirmShipment() {
    if (!this.selectedOrderForShipment) return;

    this.api.post('fulfillment/shipments', {
      vendorOrderId: this.selectedOrderForShipment.id,
      carrier: this.shippingCarrier
    }).subscribe({
      next: () => {
        alert('Shipment generated and dispatched!');
        this.selectedOrderForShipment = null;
        this.loadOrders();
      },
      error: (err) => alert(err?.error?.errorMessage || 'Failed to create shipment.')
    });
  }

  getStatusBadgeClass(status: string): string {
    switch (status) {
      case 'Delivered': return 'badge-success';
      case 'Shipped': return 'badge-info';
      case 'Preparing':
      case 'Pending': return 'badge-warning';
      default: return 'badge-neutral';
    }
  }
}
