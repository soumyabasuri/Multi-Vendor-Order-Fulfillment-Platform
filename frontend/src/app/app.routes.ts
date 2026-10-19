import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { ProductListComponent } from './components/customer/product-list/product-list.component';
import { CartComponent } from './components/customer/cart/cart.component';
import { CheckoutComponent } from './components/customer/checkout/checkout.component';
import { OrderTrackingComponent } from './components/customer/order-tracking/order-tracking.component';
import { OrderHistoryComponent } from './components/customer/order-history/order-history.component';
import { VendorDashboardComponent } from './components/vendor/vendor-dashboard/vendor-dashboard.component';
import { VendorOrdersComponent } from './components/vendor/vendor-orders/vendor-orders.component';
import { AdminDashboardComponent } from './components/admin/admin-dashboard/admin-dashboard.component';
import { LoginComponent } from './components/auth/login/login.component';
import { RegisterComponent } from './components/auth/register/register.component';

export const routes: Routes = [
  { path: '', redirectTo: 'products', pathMatch: 'full' },
  { path: 'products', component: ProductListComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },

  // Customer Routes
  { path: 'cart', component: CartComponent, canActivate: [authGuard] },
  { path: 'checkout', component: CheckoutComponent, canActivate: [authGuard] },
  { path: 'my-orders', component: OrderHistoryComponent, canActivate: [authGuard], data: { role: 'Customer' } },
  { path: 'track-order/:id', component: OrderTrackingComponent, canActivate: [authGuard] },

  // Vendor Routes
  { path: 'vendor/dashboard', component: VendorDashboardComponent, canActivate: [authGuard], data: { role: 'Vendor' } },
  { path: 'vendor/orders', component: VendorOrdersComponent, canActivate: [authGuard], data: { role: 'Vendor' } },

  // Admin Routes
  { path: 'admin/dashboard', component: AdminDashboardComponent, canActivate: [authGuard], data: { role: 'Admin' } },

  { path: '**', redirectTo: 'products' }
];
