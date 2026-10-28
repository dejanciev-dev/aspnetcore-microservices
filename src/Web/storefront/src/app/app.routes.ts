import { Routes } from '@angular/router';
import { authGuard } from './core/auth';

// Every feature is lazy-loaded so the initial bundle only contains the shell.
// Browsing is public; basket, checkout and orders need a signed-in user.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'products' },
  {
    path: 'products',
    title: 'Shop · Storefront',
    loadComponent: () => import('./features/catalog/product-list').then((m) => m.ProductList),
  },
  {
    path: 'products/:id',
    title: 'Product · Storefront',
    loadComponent: () => import('./features/catalog/product-detail').then((m) => m.ProductDetail),
  },
  {
    path: 'basket',
    title: 'Basket · Storefront',
    canActivate: [authGuard],
    loadComponent: () => import('./features/basket/basket-page').then((m) => m.BasketPage),
  },
  {
    path: 'checkout',
    title: 'Checkout · Storefront',
    canActivate: [authGuard],
    loadComponent: () => import('./features/checkout/checkout-page').then((m) => m.CheckoutPage),
  },
  {
    path: 'orders',
    title: 'Orders · Storefront',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-list').then((m) => m.OrderList),
  },
  {
    path: 'orders/:id',
    title: 'Order · Storefront',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-detail').then((m) => m.OrderDetail),
  },
  { path: '**', redirectTo: 'products' },
];
