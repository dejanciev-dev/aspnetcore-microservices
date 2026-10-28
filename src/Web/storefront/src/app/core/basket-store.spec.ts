import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BasketStore } from './basket-store';
import { ShoppingCart } from './models';
import { AuthService } from './auth';

const cartWith = (quantity: number): ShoppingCart => ({
  userName: 'ada',
  items: [{ productId: 'p1', productName: 'Keyboard', imageUrl: '', unitPrice: 10, quantity, lineTotal: 10 * quantity }],
  total: 10 * quantity,
});

describe('BasketStore', () => {
  let store: BasketStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(AuthService).user.set({ isAuthenticated: true, name: 'ada', displayName: 'Ada', roles: [] });
    store = TestBed.inject(BasketStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the basket once a user is signed in', async () => {
    TestBed.tick();
    http.expectOne('/api/basket').flush(cartWith(2));
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.itemCount()).toBe(2);
    expect(store.cart().total).toBe(20);
  });

  it('posts new items and replaces the cart with the server response', async () => {
    TestBed.tick();
    http.expectOne('/api/basket').flush(cartWith(0));

    const done = store.add('p1', 3);
    const req = http.expectOne('/api/basket/items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ productId: 'p1', quantity: 3 });
    req.flush(cartWith(3));
    await done;

    expect(store.itemCount()).toBe(3);
  });

  it('empties the local cart after checkout', async () => {
    TestBed.tick();
    http.expectOne('/api/basket').flush(cartWith(1));

    const address = { fullName: 'Ada', street: '1 Main St', city: 'London', country: 'UK', zipCode: 'N1' };
    const done = store.checkout(address);
    const req = http.expectOne('/api/basket/checkout');
    expect(req.request.body).toEqual({ shippingAddress: address });
    req.flush({ orderId: 'o1', total: 10 });

    await expect(done).resolves.toEqual({ orderId: 'o1', total: 10 });
    expect(store.itemCount()).toBe(0);
  });
});
