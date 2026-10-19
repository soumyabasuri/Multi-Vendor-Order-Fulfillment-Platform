import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { OrderDto, ShippingAddressDto } from '../../../core/models/models';

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="container checkout-container">
      <h1 class="page-title mb-4">Complete Your Order</h1>

      <div class="grid-2">
        <!-- Shipping Address Form -->
        <div class="card">
          <h2 class="card-title">1. Shipping Address</h2>

          <div class="form-group">
            <label class="form-label">Full Name</label>
            <input type="text" [(ngModel)]="address.recipientName" class="form-control" placeholder="Jane Doe">
          </div>

          <div class="form-group">
            <label class="form-label">Phone Number</label>
            <input type="text" [(ngModel)]="address.recipientPhone" class="form-control" placeholder="+1 (555) 000-0000">
          </div>

          <div class="form-group">
            <label class="form-label">Street Address</label>
            <input type="text" [(ngModel)]="address.street" class="form-control" placeholder="123 Market St, Apt 4B">
          </div>

          <div class="grid-2">
            <div class="form-group">
              <label class="form-label">City</label>
              <input type="text" [(ngModel)]="address.city" class="form-control" placeholder="San Francisco">
            </div>
            <div class="form-group">
              <label class="form-label">State / Province</label>
              <input type="text" [(ngModel)]="address.state" class="form-control" placeholder="CA">
            </div>
          </div>

          <div class="grid-2">
            <div class="form-group">
              <label class="form-label">Postal Code</label>
              <input type="text" [(ngModel)]="address.postalCode" class="form-control" placeholder="94105">
            </div>
            <div class="form-group">
              <label class="form-label">Country</label>
              <input type="text" [(ngModel)]="address.country" class="form-control" placeholder="United States">
            </div>
          </div>
        </div>

        <!-- Payment Simulation Details -->
        <div>
          <div class="card">
            <h2 class="card-title">2. Payment Simulation</h2>
            <p class="text-muted mb-3" style="font-size: 0.85rem;">
              Distributed Saga Test Mode: Any card number will succeed, <strong>except</strong> card numbers ending with <strong>0000</strong> which will trigger payment decline and demonstrate the inventory release compensating transaction.
            </p>

            <div class="form-group">
              <label class="form-label">Payment Method</label>
              <select [(ngModel)]="paymentMethod" class="form-control">
                <option value="CreditCard">Credit / Debit Card</option>
                <option value="ApplePay">Apple Pay</option>
              </select>
            </div>

            <div class="form-group">
              <label class="form-label">Card Number</label>
              <input type="text" [(ngModel)]="cardNumber" class="form-control" placeholder="4242 4242 4242 4242">
            </div>

            <div class="grid-2">
              <div class="form-group">
                <label class="form-label">Expiry (MM/YY)</label>
                <input type="text" [(ngModel)]="cardExpiry" class="form-control" placeholder="12/28">
              </div>
              <div class="form-group">
                <label class="form-label">CVC</label>
                <input type="text" [(ngModel)]="cardCvc" class="form-control" placeholder="123">
              </div>
            </div>

            <button
              [disabled]="submitting"
              (click)="submitOrder()"
              class="btn btn-primary btn-submit">
              {{ submitting ? 'Processing Order & Reserving Inventory...' : 'Confirm & Place Order' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .checkout-container { max-width: 900px; }
    .btn-submit { width: 100%; padding: 0.85rem; font-size: 1rem; margin-top: 1rem; }
  `]
})
export class CheckoutComponent {
  private api = inject(ApiService);
  private router = inject(Router);

  address: ShippingAddressDto = {
    street: '742 Evergreen Terrace',
    city: 'Springfield',
    state: 'OR',
    postalCode: '97477',
    country: 'United States',
    recipientName: 'Alex Johnson',
    recipientPhone: '+1 (555) 234-5678'
  };

  paymentMethod = 'CreditCard';
  cardNumber = '4242 4242 4242 4242';
  cardExpiry = '12/28';
  cardCvc = '888';
  submitting = false;

  submitOrder() {
    this.submitting = true;

    // Step 1: Create Order with idempotency header
    this.api.post<OrderDto>('orders', { shippingAddress: this.address }, true).subscribe({
      next: (order) => {
        // Step 2: Initiate Payment with idempotency key
        this.api.post('payments', {
          orderId: order.id,
          paymentMethod: this.paymentMethod,
          cardNumber: this.cardNumber.replace(/\s+/g, '')
        }, true).subscribe({
          next: () => {
            this.submitting = false;
            this.router.navigate(['/track-order', order.id]);
          },
          error: (err) => {
            this.submitting = false;
            alert(err?.error?.errorMessage || 'Payment simulation failed.');
            this.router.navigate(['/track-order', order.id]);
          }
        });
      },
      error: (err) => {
        this.submitting = false;
        alert(err?.error?.errorMessage || 'Failed to place order. Out of stock or invalid items.');
      }
    });
  }
}
