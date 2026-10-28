import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from '../core/auth';
import { BasketStore } from '../core/basket-store';
import { Product } from '../core/models';
import { problemMessage } from '../core/problem-details';
import { ToastService } from '../core/toast';

/** Shared "add to basket" action with per-product busy state and user feedback. */
@Injectable({ providedIn: 'root' })
export class AddToBasket {
  private readonly auth = inject(AuthService);
  private readonly basket = inject(BasketStore);
  private readonly toasts = inject(ToastService);

  readonly pending = signal<ReadonlySet<string>>(new Set());

  async run(product: Product, quantity = 1): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.auth.login();   // come back to this page after signing in
      return;
    }

    this.pending.update((ids) => new Set(ids).add(product.id));
    try {
      await this.basket.add(product.id, quantity);
      this.toasts.success(`Added ${quantity} × ${product.name}`);
    } catch (error) {
      this.toasts.error(problemMessage(error));
    } finally {
      this.pending.update((ids) => {
        const next = new Set(ids);
        next.delete(product.id);
        return next;
      });
    }
  }
}
