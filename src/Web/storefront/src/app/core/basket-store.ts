import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth';
import { CheckoutResult, ShippingAddress, ShoppingCart } from './models';

const BASKET_URL = '/api/basket';

/**
 * Signal-based store for the signed-in user's basket (Basket.API).
 * Reads go through an httpResource that only runs once someone is signed in; writes call the API
 * and push the server's cart back into the resource. The user comes from the session, not the URL.
 */
@Injectable({ providedIn: 'root' })
export class BasketStore {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly resource = httpResource<ShoppingCart>(() => (this.auth.isAuthenticated() ? BASKET_URL : undefined));

  readonly cart = computed<ShoppingCart>(() => this.resource.value() ?? this.empty());
  readonly itemCount = computed(() => this.cart().items.reduce((sum, item) => sum + item.quantity, 0));
  readonly isLoading = this.resource.isLoading;
  readonly error = this.resource.error;

  async add(productId: string, quantity = 1): Promise<void> {
    const cart = await firstValueFrom(this.http.post<ShoppingCart>(`${BASKET_URL}/items`, { productId, quantity }));
    this.resource.set(cart);
  }

  async remove(productId: string): Promise<void> {
    const cart = await firstValueFrom(this.http.delete<ShoppingCart>(`${BASKET_URL}/items/${productId}`));
    this.resource.set(cart);
  }

  async clear(): Promise<void> {
    await firstValueFrom(this.http.delete<void>(BASKET_URL));
    this.resource.set(this.empty());
  }

  async checkout(shippingAddress: ShippingAddress): Promise<CheckoutResult> {
    const result = await firstValueFrom(this.http.post<CheckoutResult>(`${BASKET_URL}/checkout`, { shippingAddress }));
    this.resource.set(this.empty());
    return result;
  }

  private empty(): ShoppingCart {
    return { userName: this.auth.user().name ?? '', items: [], total: 0 };
  }
}
