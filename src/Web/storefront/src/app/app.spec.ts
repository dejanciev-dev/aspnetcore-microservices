import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth';

describe('App', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('offers sign-in to anonymous visitors and loads no basket', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand')?.textContent).toContain('Storefront');
    expect(el.querySelector('.topbar .btn--primary')?.textContent).toContain('Sign in');
    http.expectNone('/api/basket');
  });

  it('shows the signed-in user, their role and their basket count', async () => {
    TestBed.inject(AuthService).user.set({ isAuthenticated: true, name: 'ada', displayName: 'Ada Lovelace', roles: ['admin', 'customer'] });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    http.expectOne('/api/basket').flush({ userName: 'ada', items: [{ productId: 'p1', quantity: 3 }], total: 30 });
    await TestBed.inject(ApplicationRef).whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.user__name')?.textContent).toContain('Ada Lovelace');
    expect(el.querySelector('.user .chip')?.textContent).toContain('admin');
    expect(el.querySelector('.cart__badge')?.textContent?.trim()).toBe('3');
    expect(el.textContent).toContain('Sign out');
  });
});
