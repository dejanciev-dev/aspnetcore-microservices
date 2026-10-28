import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, numberAttribute } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PagedResult, Product } from '../../core/models';
import { problemMessage } from '../../core/problem-details';
import { AddToBasket } from '../../shared/add-to-basket';
import { ProductCard } from '../../shared/product-card';

const PAGE_SIZE = 8;

@Component({
  selector: 'app-product-list',
  imports: [ProductCard, RouterLink],
  template: `
    <section class="hero">
      <h1>Gear for your desk</h1>
      <p class="muted">Served by <code>Catalog.API</code> through the YARP gateway.</p>

      <form class="search" (submit)="$event.preventDefault(); applySearch(term.value)">
        <input #term type="search" placeholder="Search products…" [value]="search() ?? ''" aria-label="Search" />
        <button class="btn" type="submit">Search</button>
      </form>
    </section>

    <nav class="chips" aria-label="Categories">
      <a class="chip" [class.chip--active]="!category()" routerLink="/products" [queryParams]="{ search: search() }">
        All
      </a>
      @for (c of categories.value(); track c) {
        <a
          class="chip"
          [class.chip--active]="c === category()"
          routerLink="/products"
          [queryParams]="{ category: c, search: search() }"
        >
          {{ c }}
        </a>
      }
    </nav>

    @if (products.error()) {
      <div class="alert" role="alert">
        {{ errorMessage() }}
        <button class="btn" (click)="products.reload()">Retry</button>
      </div>
    } @else if (products.hasValue()) {
      @let result = products.value();
      @if (result.items.length === 0) {
        <p class="empty">No products match your filters.</p>
      } @else {
        <div class="grid" [class.is-loading]="products.isLoading()">
          @for (product of result.items; track product.id) {
            <app-product-card
              [product]="product"
              [busy]="adder.pending().has(product.id)"
              (addToBasket)="adder.run($event)"
            />
          }
        </div>

        @if (result.totalPages > 1) {
          <nav class="pager" aria-label="Pagination">
            <button class="btn" [disabled]="page() <= 1" (click)="goTo(page() - 1)">← Prev</button>
            <span class="muted">Page {{ result.page }} of {{ result.totalPages }}</span>
            <button class="btn" [disabled]="page() >= result.totalPages" (click)="goTo(page() + 1)">Next →</button>
          </nav>
        }
      }
    } @else {
      <div class="grid">
        @for (_ of skeletons; track $index) {
          <div class="card skeleton"></div>
        }
      </div>
    }
  `,
})
export class ProductList {
  private readonly router = inject(Router);
  protected readonly adder = inject(AddToBasket);

  // Bound from query params via withComponentInputBinding().
  readonly category = input<string>();
  readonly search = input<string>();
  readonly page = input(1, { transform: (v: unknown) => Math.max(numberAttribute(v, 1), 1) });

  protected readonly skeletons = Array.from({ length: PAGE_SIZE });

  protected readonly categories = httpResource<string[]>(() => '/api/products/categories', { defaultValue: [] });

  protected readonly products = httpResource<PagedResult<Product>>(() => ({
    url: '/api/products',
    params: {
      page: this.page(),
      pageSize: PAGE_SIZE,
      ...(this.category() ? { category: this.category()! } : {}),
      ...(this.search() ? { search: this.search()! } : {}),
    },
  }));

  protected readonly errorMessage = computed(() => problemMessage(this.products.error()));

  protected applySearch(term: string): void {
    this.navigate({ search: term.trim() || null, page: null });
  }

  protected goTo(page: number): void {
    this.navigate({ page });
  }

  private navigate(queryParams: Record<string, string | number | null>): void {
    this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}
