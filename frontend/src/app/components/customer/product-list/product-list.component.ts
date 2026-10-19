import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { ProductDto, CategoryDto, PagedResult } from '../../../core/models/models';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="container">
      <div class="catalog-header">
        <div>
          <h1 class="page-title">Marketplace Catalog</h1>
          <p class="page-subtitle">Shop authentic products fulfilled directly by verified vendors.</p>
        </div>
      </div>

      <!-- Filters & Search Toolbar -->
      <div class="card toolbar-card">
        <div class="toolbar-grid">
          <div class="form-group mb-0">
            <input type="text" [(ngModel)]="searchQuery" (keyup.enter)="loadProducts()" placeholder="Search products, brands, or SKUs..." class="form-control">
          </div>
          <div class="form-group mb-0">
            <select [(ngModel)]="selectedCategory" (change)="loadProducts()" class="form-control">
              <option value="">All Categories</option>
              <option *ngFor="let cat of categories" [value]="cat.id">{{ cat.name }}</option>
            </select>
          </div>
          <div class="form-group mb-0">
            <select [(ngModel)]="sortBy" (change)="loadProducts()" class="form-control">
              <option value="newest">Sort: Newest First</option>
              <option value="price_asc">Price: Low to High</option>
              <option value="price_desc">Price: High to Low</option>
              <option value="name">Name (A-Z)</option>
            </select>
          </div>
          <button (click)="loadProducts()" class="btn btn-primary">Filter</button>
        </div>
      </div>

      <!-- Product Cards Grid -->
      <div *ngIf="loading" class="text-center py-5">
        <p>Loading catalog items...</p>
      </div>

      <div *ngIf="!loading && products.length === 0" class="card text-center py-5">
        <h3>No products found</h3>
        <p class="text-muted">Try adjusting your filters or search keywords.</p>
      </div>

      <div *ngIf="!loading && products.length > 0" class="grid-3">
        <div *ngFor="let product of products" class="card product-card">
          <div class="product-vendor">Sold by: <strong>{{ product.vendorName }}</strong></div>
          <h3 class="product-title">{{ product.name }}</h3>
          <p class="product-description">{{ product.description }}</p>

          <div class="product-meta">
            <span class="product-category badge badge-neutral">{{ product.categoryName }}</span>
            <span *ngIf="product.availableStock > 5" class="badge badge-success">In Stock ({{ product.availableStock }})</span>
            <span *ngIf="product.availableStock > 0 && product.availableStock <= 5" class="badge badge-warning">Only {{ product.availableStock }} Left!</span>
            <span *ngIf="product.availableStock <= 0" class="badge badge-danger">Out of Stock</span>
          </div>

          <div class="product-footer">
            <div class="product-price">\${{ product.basePrice.toFixed(2) }}</div>
            <button
              [disabled]="product.availableStock <= 0 || addingProductId === product.id"
              (click)="addToCart(product)"
              class="btn btn-primary btn-sm">
              {{ addingProductId === product.id ? 'Adding...' : 'Add to Cart' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .catalog-header { margin-bottom: 1.5rem; }
    .page-title { font-size: 1.75rem; font-weight: 700; color: var(--text-main); }
    .page-subtitle { color: var(--text-muted); font-size: 0.95rem; }
    .toolbar-card { padding: 1rem; margin-bottom: 1.5rem; }
    .toolbar-grid { display: grid; grid-template-columns: 2fr 1fr 1fr auto; gap: 0.75rem; align-items: center; }
    .mb-0 { margin-bottom: 0 !important; }
    .product-card {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      height: 100%;
      transition: transform 0.15s, box-shadow 0.15s;
    }
    .product-card:hover {
      transform: translateY(-2px);
      box-shadow: var(--shadow-lg);
    }
    .product-vendor { font-size: 0.75rem; color: var(--text-muted); margin-bottom: 0.25rem; }
    .product-title { font-size: 1.1rem; font-weight: 600; margin-bottom: 0.5rem; }
    .product-description {
      font-size: 0.85rem;
      color: var(--text-muted);
      margin-bottom: 1rem;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .product-meta { display: flex; gap: 0.5rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .product-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding-top: 0.75rem;
      border-top: 1px solid var(--border);
    }
    .product-price { font-size: 1.25rem; font-weight: 700; color: var(--primary); }
    .text-center { text-align: center; }
    .py-5 { padding-top: 2rem; padding-bottom: 2rem; }
  `]
})
export class ProductListComponent implements OnInit {
  private api = inject(ApiService);
  private auth = inject(AuthService);

  products: ProductDto[] = [];
  categories: CategoryDto[] = [];
  searchQuery = '';
  selectedCategory = '';
  sortBy = 'newest';
  loading = false;
  addingProductId: string | null = null;

  ngOnInit() {
    this.loadCategories();
    this.loadProducts();
  }

  loadCategories() {
    this.api.get<CategoryDto[]>('categories').subscribe({
      next: (cats) => this.categories = cats,
      error: () => {}
    });
  }

  loadProducts() {
    this.loading = true;
    const params: any = {
      sortBy: this.sortBy,
      pageNumber: 1,
      pageSize: 30
    };
    if (this.searchQuery) params.search = this.searchQuery;
    if (this.selectedCategory) params.categoryId = this.selectedCategory;

    this.api.get<PagedResult<ProductDto>>('products', params).subscribe({
      next: (res) => {
        this.products = res.items;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  addToCart(product: ProductDto) {
    if (!this.auth.isAuthenticated()) {
      alert('Please sign in to add items to your cart.');
      return;
    }

    this.addingProductId = product.id;
    this.api.post('cart/items', {
      productId: product.id,
      quantity: 1
    }).subscribe({
      next: () => {
        this.addingProductId = null;
        alert(`Added "${product.name}" to cart!`);
      },
      error: (err) => {
        this.addingProductId = null;
        alert(err?.error?.errorMessage || 'Failed to add item to cart.');
      }
    });
  }
}
