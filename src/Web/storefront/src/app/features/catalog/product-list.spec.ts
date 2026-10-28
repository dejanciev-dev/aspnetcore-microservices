import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PagedResult, Product } from '../../core/models';
import { ProductList } from './product-list';

const product = (n: number): Product => ({
  id: `p${n}`,
  name: `Product ${n}`,
  description: 'desc',
  category: 'Audio',
  price: 10 * n,
  imageUrl: '',
});

describe('ProductList', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProductList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('requests the filtered page and renders product cards, categories and the pager', async () => {
    const fixture = TestBed.createComponent(ProductList);
    fixture.componentRef.setInput('category', 'Audio');
    fixture.componentRef.setInput('page', '2');
    fixture.detectChanges();

    http.expectOne('/api/products/categories').flush(['Audio', 'Displays']);
    const req = http.expectOne((r) => r.url === '/api/products');
    expect(req.request.params.get('category')).toBe('Audio');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.has('search')).toBe(false);

    const page: PagedResult<Product> = { items: [product(1), product(2)], page: 2, pageSize: 8, totalCount: 18, totalPages: 3 };
    req.flush(page);
    await TestBed.inject(ApplicationRef).whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('app-product-card').length).toBe(2);
    expect(el.querySelector('.chip--active')?.textContent?.trim()).toBe('Audio');
    expect(el.querySelector('.pager')?.textContent).toContain('Page 2 of 3');
  });

  it('shows a friendly message when the gateway is unreachable', async () => {
    const fixture = TestBed.createComponent(ProductList);
    fixture.detectChanges();

    http.expectOne('/api/products/categories').flush([]);
    http.expectOne((r) => r.url === '/api/products').error(new ProgressEvent('error'), { status: 0 });
    await TestBed.inject(ApplicationRef).whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.alert')?.textContent).toContain('unreachable');
  });
});
