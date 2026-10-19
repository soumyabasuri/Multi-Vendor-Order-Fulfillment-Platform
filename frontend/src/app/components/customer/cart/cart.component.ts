import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { CartDto } from '../../../core/models/models';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="container">
      <h1 class="page-title mb-4">Your Shopping Cart</h1>

      <div *ngIf="loading" class="text-center py-5">
        <p>Loading cart items...</p>
      </div>

      <div *ngIf="!loading && (!cart || cart.items.length === 0)" class="card text-center py-5">
        <h2>Your cart is empty</h2>
        <p class="text-muted mb-4">Explore our multi-vendor catalog to add quality products.</p>
        <div>
          <a routerLink="/products" class="btn btn-primary">Start Shopping</a>
        </div>
      </div>

      <div *ngIf="!loading && cart && cart.items.length > 0" class="cart-layout">
        <!-- Cart Items List -->
        <div class="cart-items-column">
          <div *ngIf="cart.hasStockIssues" class="alert alert-warning mb-3">
            Some items in your cart exceed current available inventory. Please adjust quantities before checkout.
          </div>

          <div *ngFor="let item of cart.items" class="card cart-item-card">
            <div class="item-header">
              <div>
                <span class="badge badge-neutral">Vendor: {{ item.vendorName }}</span>
                <h3 class="item-title">{{ item.productName }}</h3>
                <span *ngIf="item.variantName" class="item-variant">Variant: {{ item.variantName }}</span>
              </div>
              <div class="item-price">\${{ item.unitPrice.toFixed(2) }}</div>
            </div>

            <div class="item-actions">
              <div class="quantity-picker">
                <button (click)="updateQuantity(item.id, item.quantity - 1)" class="btn-qty">-</button>
                <span class="qty-display">{{ item.quantity }}</span>
                <button (click)="updateQuantity(item.id, item.quantity + 1)" class="btn-qty">+</button>
              </div>

              <div class="item-total-price">
                Total: <strong>\${{ item.totalPrice.toFixed(2) }}</strong>
              </div>

              <button (click)="removeItem(item.id)" class="btn btn-danger btn-sm">Remove</button>
            </div>
          </div>
        </div>

        <!-- Order Summary Box -->
        <div class="cart-summary-column">
          <div class="card summary-card">
            <h2 class="card-title">Order Summary</h2>

            <div class="summary-row">
              <span>Subtotal ({{ cart.totalItems }} items)</span>
              <span>\${{ cart.subtotal.toFixed(2) }}</span>
            </div>
            <div class="summary-row">
              <span>Shipping Estimate</span>
              <span>{{ cart.subtotal >= 100 ? 'FREE' : '$10.00' }}</span>
            </div>
            <div class="summary-row">
              <span>Estimated Tax (5%)</span>
              <span>\${{ (cart.subtotal * 0.05).toFixed(2) }}</span>
            </div>

            <div class="summary-divider"></div>

            <div class="summary-row total-row">
              <span>Estimated Total</span>
              <span class="total-amount">
                \${{ (cart.subtotal + (cart.subtotal >= 100 ? 0 : 10) + (cart.subtotal * 0.05)).toFixed(2) }}
              </span>
            </div>

            <button
              [disabled]="cart.hasStockIssues"
              routerLink="/checkout"
              class="btn btn-primary btn-checkout">
              Proceed to Checkout
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .mb-4 { margin-bottom: 1.5rem; }
    .mb-3 { margin-bottom: 1rem; }
    .cart-layout { display: grid; grid-template-columns: 2fr 1fr; gap: 1.5rem; }
    .alert-warning {
      background: #fef3c7;
      color: #92400e;
      padding: 0.75rem 1rem;
      border-radius: var(--radius);
      font-size: 0.875rem;
    }
    .cart-item-card { margin-bottom: 1rem; }
    .item-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1rem; }
    .item-title { font-size: 1.1rem; font-weight: 600; margin-top: 0.35rem; }
    .item-variant { font-size: 0.8rem; color: var(--text-muted); }
    .item-price { font-size: 1rem; font-weight: 600; color: var(--text-muted); }
    .item-actions { display: flex; justify-content: space-between; align-items: center; }
    .quantity-picker { display: flex; align-items: center; border: 1px solid var(--border); border-radius: var(--radius); }
    .btn-qty { background: none; border: none; padding: 0.25rem 0.6rem; cursor: pointer; font-size: 1rem; }
    .qty-display { padding: 0.25rem 0.6rem; font-weight: 600; font-size: 0.875rem; }
    .summary-card { position: sticky; top: 5rem; }
    .summary-row { display: flex; justify-content: space-between; margin-bottom: 0.75rem; font-size: 0.9rem; }
    .summary-divider { height: 1px; background: var(--border); margin: 1rem 0; }
    .total-row { font-weight: 700; font-size: 1.1rem; color: var(--text-main); }
    .total-amount { color: var(--primary); font-size: 1.3rem; }
    .btn-checkout { width: 100%; margin-top: 1rem; padding: 0.75rem; font-size: 1rem; }
    @media (max-width: 768px) { .cart-layout { grid-template-columns: 1fr; } }
  `]
})
export class CartComponent implements OnInit {
  private api = inject(ApiService);
  cart: CartDto | null = null;
  loading = true;

  ngOnInit() {
    this.loadCart();
  }

  loadCart() {
    this.loading = true;
    this.api.get<CartDto>('cart').subscribe({
      next: (cart) => {
        this.cart = cart;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  updateQuantity(itemId: string, newQuantity: number) {
    this.api.put<CartDto>(`cart/items/${itemId}`, { quantity: newQuantity }).subscribe({
      next: (updatedCart) => this.cart = updatedCart,
      error: (err) => alert(err?.error?.errorMessage || 'Could not update quantity.')
    });
  }

  removeItem(itemId: string) {
    this.api.delete<CartDto>(`cart/items/${itemId}`).subscribe({
      next: (updatedCart) => this.cart = updatedCart,
      error: () => alert('Failed to remove item.')
    });
  }
}
