import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth';
import { BasketStore } from './core/basket-store';
import { ToastService } from './core/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <div class="container topbar__inner">
        <a routerLink="/" class="brand">
          <span class="brand__mark" aria-hidden="true">◆</span> Storefront
        </a>

        <nav class="nav">
          <a routerLink="/products" routerLinkActive="active">Shop</a>
          <a routerLink="/orders" routerLinkActive="active">Orders</a>
        </nav>

        <a routerLink="/basket" class="cart" aria-label="Basket">
          🛒
          @if (basket.itemCount() > 0) {
            <span class="cart__badge">{{ basket.itemCount() }}</span>
          }
        </a>

        @if (auth.isAuthenticated()) {
          <span class="user">
            <span class="user__name">{{ auth.user().displayName }}</span>
            @if (auth.isAdmin()) {
              <span class="chip chip--muted">admin</span>
            }
          </span>
          <button class="btn btn--ghost" (click)="auth.logout()">Sign out</button>
        } @else {
          <button class="btn btn--primary" (click)="auth.login()">Sign in</button>
        }
      </div>
    </header>

    <main class="container page">
      <router-outlet />
    </main>

    <footer class="container footer">
      .NET 10 microservices · YARP BFF · Keycloak · Angular 21
    </footer>

    <div class="toasts" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <button class="toast" [class.toast--error]="toast.kind === 'error'" (click)="toasts.dismiss(toast.id)">
          {{ toast.text }}
        </button>
      }
    </div>
  `,
})
export class App {
  protected readonly basket = inject(BasketStore);
  protected readonly auth = inject(AuthService);
  protected readonly toasts = inject(ToastService);
}
