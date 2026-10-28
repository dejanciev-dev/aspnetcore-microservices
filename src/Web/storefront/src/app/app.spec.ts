import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('renders the navigation and shows the basket item count', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/basket/demo-user')
      .flush({ userName: 'demo-user', items: [{ productId: 'p1', quantity: 3 }], total: 30 });
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand')?.textContent).toContain('Storefront');
    expect(el.querySelector('.cart__badge')?.textContent?.trim()).toBe('3');
  });
});
