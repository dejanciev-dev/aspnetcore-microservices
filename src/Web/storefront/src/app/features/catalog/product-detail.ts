import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../../core/models';
import { problemMessage } from '../../core/problem-details';
import { AddToBasket } from '../../shared/add-to-basket';

@Component({
  selector: 'app-product-detail',
  imports: [CurrencyPipe, RouterLink],
  template: `
    <a routerLink="/products" class="back">← Back to shop</a>

    @if (product.error()) {
      <div class="alert" role="alert">{{ errorMessage() }}</div>
    } @else if (product.hasValue()) {
      @let p = product.value();
      <article class="detail card">
        <img [src]="p.imageUrl" [alt]="p.name" width="480" height="320" />
        <div class="detail__body">
          <span class="chip chip--muted">{{ p.category }}</span>
          <h1>{{ p.name }}</h1>
          <p>{{ p.description }}</p>
          <p class="price price--lg">{{ p.price | currency }}</p>

          <div class="row">
            <div class="stepper" role="group" aria-label="Quantity">
              <button class="btn" (click)="quantity.set(quantity() - 1)" [disabled]="quantity() <= 1">−</button>
              <output>{{ quantity() }}</output>
              <button class="btn" (click)="quantity.set(quantity() + 1)" [disabled]="quantity() >= 99">+</button>
            </div>
            <button class="btn btn--primary" [disabled]="adder.pending().has(p.id)" (click)="adder.run(p, quantity())">
              Add to basket · {{ p.price * quantity() | currency }}
            </button>
          </div>
        </div>
      </article>
    } @else {
      <div class="card skeleton skeleton--tall"></div>
    }
  `,
})
export class ProductDetail {
  protected readonly adder = inject(AddToBasket);

  readonly id = input.required<string>();
  protected readonly quantity = signal(1);

  protected readonly product = httpResource<Product>(() => `/api/products/${this.id()}`);
  protected readonly errorMessage = computed(() =>
    this.product.error() && (this.product.statusCode() === 404 ? 'Product not found.' : problemMessage(this.product.error())),
  );
}
