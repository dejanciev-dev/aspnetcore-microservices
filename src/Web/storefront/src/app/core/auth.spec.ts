import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService, authGuard, bffInterceptor } from './auth';

describe('auth', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([bffInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    vi.spyOn(auth, 'login').mockImplementation(() => {});   // jsdom can't navigate
  });

  afterEach(() => http.verify());

  it('loads the session from the BFF', async () => {
    const loading = auth.load();
    http.expectOne('/bff/user').flush({ isAuthenticated: true, name: 'grace', displayName: 'Grace Hopper', roles: ['customer'] });
    await loading;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.isAdmin()).toBe(false);
  });

  it('treats an unreachable gateway as anonymous', async () => {
    const loading = auth.load();
    http.expectOne('/bff/user').error(new ProgressEvent('error'), { status: 0 });
    await loading;

    expect(auth.isAuthenticated()).toBe(false);
  });

  it('adds the anti-CSRF header to API calls only', () => {
    const client = TestBed.inject(HttpClient);
    client.get('/api/basket').subscribe();
    client.get('/bff/user').subscribe();

    expect(http.expectOne('/api/basket').request.headers.get('X-CSRF')).toBe('1');
    expect(http.expectOne('/bff/user').request.headers.has('X-CSRF')).toBe(false);
  });

  it('starts a new sign-in when the session has expired', async () => {
    auth.user.set({ isAuthenticated: true, name: 'ada', displayName: 'Ada', roles: [] });

    const request = firstValueFrom(TestBed.inject(HttpClient).get('/api/basket'));
    http.expectOne('/api/basket').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeTruthy();
    expect(auth.login).toHaveBeenCalled();
  });

  it('guards private pages by sending anonymous visitors to sign in', () => {
    const allowed = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/orders' } as RouterStateSnapshot),
    );

    expect(allowed).toBe(false);
    expect(auth.login).toHaveBeenCalledWith('/orders');
  });
});
