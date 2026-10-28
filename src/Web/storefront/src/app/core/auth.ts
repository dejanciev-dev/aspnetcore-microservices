import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';

/** What the gateway's BFF reports about the session at /bff/user. */
export interface BffUser {
  isAuthenticated: boolean;
  name: string | null;
  displayName: string | null;
  roles: string[];
}

const ANONYMOUS: BffUser = { isAuthenticated: false, name: null, displayName: null, roles: [] };

/**
 * Session state for the BFF pattern: the gateway holds the tokens, the browser only has an
 * HttpOnly cookie, so the app never handles a token. Sign-in and sign-out are full-page
 * navigations to the gateway, which talks OpenID Connect to Keycloak.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly location = inject(DOCUMENT).location;

  readonly user = signal<BffUser>(ANONYMOUS);
  readonly isAuthenticated = computed(() => this.user().isAuthenticated);
  readonly isAdmin = computed(() => this.user().roles.includes('admin'));

  /** Runs once at startup (provideAppInitializer) so guards and the header know the session up front. */
  async load(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.http.get<BffUser>('/bff/user')));
    } catch {
      this.user.set(ANONYMOUS);   // gateway unreachable: the public catalog still renders
    }
  }

  login(returnUrl = this.location.pathname + this.location.search): void {
    this.location.assign(`/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  }

  logout(): void {
    this.location.assign('/bff/logout');
  }
}

/** Pages that need a signed-in user send anonymous visitors to Keycloak and back. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) return true;
  auth.login(state.url);
  return false;
};

/**
 * Adds the anti-CSRF header the gateway requires on cookie-authenticated /api calls, and starts a
 * new sign-in when a request comes back 401 for a user we believed signed in (session expired).
 */
export const bffInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api')) return next(request);

  const auth = inject(AuthService);
  return next(request.clone({ setHeaders: { 'X-CSRF': '1' } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && auth.isAuthenticated()) auth.login();
      return throwError(() => error);
    }),
  );
};
