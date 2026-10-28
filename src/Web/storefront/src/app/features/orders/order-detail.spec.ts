import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Order } from '../../core/models';
import { OrderDetail } from './order-detail';

const ORDER_ID = 'abcdef12-3456-7890-abcd-ef1234567890';
const order: Order = {
  id: ORDER_ID,
  userName: 'demo-user',
  items: [{ productId: 'p1', productName: 'Keyboard', unitPrice: 100, quantity: 1, lineTotal: 100 }],
  shippingAddress: { fullName: 'Ada', street: '1 Main', city: 'London', country: 'UK', zipCode: 'N1' },
  status: 'Submitted',
  createdAt: '2026-10-28T10:00:00Z',
  total: 100,
};

describe('OrderDetail', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrderDetail],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function render(placed: boolean) {
    const fixture = TestBed.createComponent(OrderDetail);
    fixture.componentRef.setInput('id', ORDER_ID);
    fixture.componentRef.setInput('placed', placed);
    fixture.detectChanges();
    return fixture;
  }

  // Real timers: the zoneless scheduler itself runs on setTimeout, so fake timers would stall whenStable().
  const pollInterval = () => new Promise((resolve) => setTimeout(resolve, 1100));

  const notFound = () =>
    http.expectOne(`/api/orders/${ORDER_ID}`).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });

  it('right after an async checkout, shows "processing" and polls until the order exists', async () => {
    const fixture = render(true);
    const el = fixture.nativeElement as HTMLElement;

    notFound();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(el.textContent).toContain('being processed');
    expect(el.querySelector('[role=alert]')).toBeNull();

    await pollInterval();
    http.expectOne(`/api/orders/${ORDER_ID}`).flush(order);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(el.textContent).not.toContain('being processed');
    expect(el.querySelector('h1')?.textContent).toContain('#abcdef12');
  });

  it('reports a missing order immediately when not coming from checkout', async () => {
    const fixture = render(false);

    notFound();
    await TestBed.inject(ApplicationRef).whenStable();
    await pollInterval();

    http.expectNone(`/api/orders/${ORDER_ID}`);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('Order not found');
  });
});
