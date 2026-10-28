import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BasketStore } from './core/basket-store';
import { ToastService } from './core/toast';
import { UserSession } from './core/user-session';

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

        <label class="user" title="Basket and orders are stored per user name">
          <span class="sr-only">User name</span>
          <input
            #user
            [value]="session.userName()"
            (change)="session.rename(user.value)"
            aria-label="User name"
          />
        </label>

        <a routerLink="/basket" class="cart" aria-label="Basket">
          🛒
          @if (basket.itemCount() > 0) {
            <span class="cart__badge">{{ basket.itemCount() }}</span>
          }
        </a>
      </div>
    </header>

    <main class="container page">
      <router-outlet />
    </main>

    <footer class="container footer">
      .NET 10 microservices · YARP gateway · Angular 21
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
  protected readonly session = inject(UserSession);
  protected readonly toasts = inject(ToastService);
}
