import { CurrencyPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BasketStore } from '../../core/basket-store';
import { problemMessage } from '../../core/problem-details';
import { ToastService } from '../../core/toast';

@Component({
  selector: 'app-basket-page',
  imports: [CurrencyPipe, RouterLink],
  template: `
    <h1>Your basket</h1>

    @let cart = basket.cart();
    @if (cart.items.length === 0) {
      <div class="empty card">
        <p>Your basket is empty.</p>
        <a class="btn btn--primary" routerLink="/products">Browse products</a>
      </div>
    } @else {
      <div class="layout-2col">
        <ul class="card lines">
          @for (item of cart.items; track item.productId) {
            <li class="line">
              <img [src]="item.imageUrl" [alt]="item.productName" width="96" height="64" />
              <div class="line__info">
                <a [routerLink]="['/products', item.productId]">{{ item.productName }}</a>
                <span class="muted">{{ item.quantity }} × {{ item.unitPrice | currency }}</span>
              </div>
              <strong>{{ item.lineTotal | currency }}</strong>
              <button class="btn btn--ghost" (click)="remove(item.productId)" aria-label="Remove {{ item.productName }}">
                ✕
              </button>
            </li>
          }
        </ul>

        <aside class="card summary">
          <h2>Summary</h2>
          <div class="summary__row">
            <span>Items</span><span>{{ basket.itemCount() }}</span>
          </div>
          <div class="summary__row summary__total">
            <span>Total</span><span>{{ cart.total | currency }}</span>
          </div>
          <a class="btn btn--primary btn--block" routerLink="/checkout">Checkout</a>
          <button class="btn btn--ghost btn--block" (click)="clear()">Clear basket</button>
        </aside>
      </div>
    }
  `,
})
export class BasketPage {
  protected readonly basket = inject(BasketStore);
  private readonly toasts = inject(ToastService);

  protected async remove(productId: string): Promise<void> {
    await this.basket.remove(productId).catch((e) => this.toasts.error(problemMessage(e)));
  }

  protected async clear(): Promise<void> {
    await this.basket.clear().catch((e) => this.toasts.error(problemMessage(e)));
  }
}
