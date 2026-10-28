import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth';
import { CheckoutPage } from './checkout-page';

describe('CheckoutPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CheckoutPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).user.set({ isAuthenticated: true, name: 'ada', displayName: 'Ada', roles: [] });
  });

  async function render() {
    const fixture = TestBed.createComponent(CheckoutPage);
    fixture.detectChanges();
    http.expectOne('/api/basket').flush({
      userName: 'ada',
      items: [{ productId: 'p1', productName: 'Keyboard', imageUrl: '', unitPrice: 10, quantity: 1, lineTotal: 10 }],
      total: 10,
    });
    await fixture.whenStable();
    return fixture;
  }

  it('shows validation errors and does not call the API when the form is empty', async () => {
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(el.querySelectorAll('.field__error').length).toBe(5);
    http.expectNone('/api/basket/checkout');
  });

  it('submits the shipping address when the form is valid', async () => {
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;

    const values = ['Ada Lovelace', '1 Main St', 'London', 'N1 9GU', 'UK'];
    el.querySelectorAll('input').forEach((input, i) => {
      input.value = values[i];
      input.dispatchEvent(new Event('input'));
    });
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = http.expectOne('/api/basket/checkout');
    expect(req.request.body.shippingAddress).toEqual({
      fullName: 'Ada Lovelace',
      street: '1 Main St',
      city: 'London',
      zipCode: 'N1 9GU',
      country: 'UK',
    });
  });
});
