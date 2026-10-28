import { CurrencyPipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../core/models';

@Component({
  selector: 'app-product-card',
  imports: [CurrencyPipe, RouterLink],
  template: `
    <article class="card product-card">
      <a [routerLink]="['/products', product().id]" class="product-card__media">
        <img [src]="product().imageUrl" [alt]="product().name" loading="lazy" width="480" height="320" />
      </a>
      <div class="product-card__body">
        <span class="chip chip--muted">{{ product().category }}</span>
        <h3>
          <a [routerLink]="['/products', product().id]">{{ product().name }}</a>
        </h3>
        <p class="muted">{{ product().description }}</p>
      </div>
      <footer class="product-card__footer">
        <strong class="price">{{ product().price | currency }}</strong>
        <button class="btn btn--primary" [disabled]="busy()" (click)="addToBasket.emit(product())">
          {{ busy() ? 'Adding…' : 'Add to basket' }}
        </button>
      </footer>
    </article>
  `,
})
export class ProductCard {
  readonly product = input.required<Product>();
  readonly busy = input(false);
  readonly addToBasket = output<Product>();
}
