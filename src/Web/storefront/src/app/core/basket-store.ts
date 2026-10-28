import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CheckoutResult, ShippingAddress, ShoppingCart } from './models';
import { UserSession } from './user-session';

/**
 * Signal-based store for the current user's basket (Basket.API).
 * Reads go through an httpResource that refetches whenever the user changes;
 * writes call the API and push the server's cart back into the resource.
 */
@Injectable({ providedIn: 'root' })
export class BasketStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(UserSession);

  private readonly baseUrl = computed(() => `/api/basket/${encodeURIComponent(this.session.userName())}`);
  private readonly resource = httpResource<ShoppingCart>(() => this.baseUrl());

  readonly cart = computed<ShoppingCart>(
    () => this.resource.value() ?? { userName: this.session.userName(), items: [], total: 0 },
  );
  readonly itemCount = computed(() => this.cart().items.reduce((sum, item) => sum + item.quantity, 0));
  readonly isLoading = this.resource.isLoading;
  readonly error = this.resource.error;

  async add(productId: string, quantity = 1): Promise<void> {
    const cart = await firstValueFrom(
      this.http.post<ShoppingCart>(`${this.baseUrl()}/items`, { productId, quantity }),
    );
    this.resource.set(cart);
  }

  async remove(productId: string): Promise<void> {
    const cart = await firstValueFrom(this.http.delete<ShoppingCart>(`${this.baseUrl()}/items/${productId}`));
    this.resource.set(cart);
  }

  async clear(): Promise<void> {
    await firstValueFrom(this.http.delete<void>(this.baseUrl()));
    this.resetLocal();
  }

  async checkout(shippingAddress: ShippingAddress): Promise<CheckoutResult> {
    const result = await firstValueFrom(
      this.http.post<CheckoutResult>(`${this.baseUrl()}/checkout`, { shippingAddress }),
    );
    this.resetLocal();
    return result;
  }

  private resetLocal(): void {
    this.resource.set({ userName: this.session.userName(), items: [], total: 0 });
  }
}
