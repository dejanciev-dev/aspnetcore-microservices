import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Order } from '../../core/models';
import { OrderList } from './order-list';

describe('OrderList', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrderList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it("lists the signed-in user's orders", async () => {
    const fixture = TestBed.createComponent(OrderList);
    fixture.detectChanges();

    const order: Order = {
      id: 'abcdef12-3456-7890-abcd-ef1234567890',
      userName: 'ada',
      items: [{ productId: 'p1', productName: 'Keyboard', unitPrice: 100, quantity: 2, lineTotal: 200 }],
      shippingAddress: { fullName: 'Ada', street: '1 Main', city: 'London', country: 'UK', zipCode: 'N1' },
      status: 'Submitted',
      createdAt: '2026-10-28T10:00:00Z',
      total: 200,
    };
    TestBed.inject(HttpTestingController)
      .expectOne('/api/orders')
      .flush([order]);
    await TestBed.inject(ApplicationRef).whenStable();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('#abcdef12');
    expect(rows[0].textContent).toContain('Submitted');
    expect(rows[0].textContent).toContain('$200.00');
  });
});
