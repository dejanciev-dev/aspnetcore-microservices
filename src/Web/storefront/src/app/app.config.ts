import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';

import { routes } from './app.routes';
import { AuthService, bffInterceptor } from './core/auth';

// Zoneless by default in Angular 21: change detection is driven entirely by signals.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([bffInterceptor])),
    // Know whether someone is signed in before the first route renders.
    provideAppInitializer(() => inject(AuthService).load()),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
  ],
};
