# Front end: Angular 21 storefront

The storefront in [`src/Web/storefront`](../src/Web/storefront) is a single-page app that consumes the microservices through the API gateway. It shows how a modern Angular app looks without NgModules, zone.js or a state-management library.

- [Features](#features)
- [Structure](#structure)
- [Key patterns](#key-patterns)
- [Talking to the backend](#talking-to-the-backend)
- [Styling](#styling)
- [Testing](#testing)
- [Scripts](#scripts)

---

## Features

| Page | Route | Backed by |
|---|---|---|
| Shop: search, category chips, paging | `/products?category=&search=&page=` | Catalog.API |
| Product details with quantity picker | `/products/:id` | Catalog.API |
| Basket | `/basket` | Basket.API |
| Checkout with a validated address form | `/checkout` | Basket.API → Ordering.API |
| Order history | `/orders` | Ordering.API |
| Order confirmation and details (polls while an async order is processed) | `/orders/:id` | Ordering.API |

The header shows a live basket badge and an editable **user name**. Without authentication, this name keys the basket and the orders, and it is stored in `localStorage`.

## Structure

```text
src/app/
├── app.ts / app.config.ts / app.routes.ts   # shell, providers, lazy routes
├── core/                                    # app-wide singletons and contracts
│   ├── models.ts                            # TypeScript mirrors of the API contracts
│   ├── basket-store.ts                      # signal-based basket state
│   ├── user-session.ts                      # current user name (localStorage)
│   ├── problem-details.ts                   # RFC 9457 → readable message
│   └── toast.ts                             # notifications
├── shared/                                  # reusable UI and actions
│   ├── product-card.ts
│   └── add-to-basket.ts
└── features/                                # one folder per area, lazy-loaded
    ├── catalog/   (product-list, product-detail)
    ├── basket/    (basket-page)
    ├── checkout/  (checkout-page)
    └── orders/    (order-list, order-detail)
```

File names follow the current Angular style guide (`product-list.ts`, not `product-list.component.ts`).

## Key patterns

### Zoneless and signal-first

Angular 21 is zoneless by default and there is no `zone.js` in `package.json`. All component state is held in `signal()`, `computed()` and `input()`, so Angular knows exactly what changed and when to re-render.

### `httpResource` for data loading

Read-only data is declared, not fetched imperatively:

```ts
readonly category = input<string>();   // bound from ?category=
readonly page = input(1, { transform: ... });

protected readonly products = httpResource<PagedResult<Product>>(() => ({
  url: '/api/products',
  params: { page: this.page(), pageSize: 8, ...(this.category() ? { category: this.category()! } : {}) },
}));
```

When a query parameter changes, the input signal changes, the resource re-requests, and the template reacts through `products.value()`, `isLoading()` and `error()`. There are no subscriptions to manage and no `ngOnInit`.

### Router input binding

`provideRouter(routes, withComponentInputBinding())` passes route and query params straight into component `input()`s, so `/products/:id` gives `ProductDetail` an `id` input. Search and paging just navigate with new query params.

### A tiny signal store

[`BasketStore`](../src/Web/storefront/src/app/core/basket-store.ts) shows that a store can be a single injectable:

- **Reads:** an `httpResource` keyed on the current user (switching user refetches automatically)
- **Derived state:** `computed()` for the cart and `itemCount`
- **Writes:** `HttpClient` calls; the server's response is written back with `resource.set(cart)`, so the server stays the source of truth

### Signal Forms

The checkout form uses the new `@angular/forms/signals` API:

```ts
private readonly model = signal<ShippingAddress>({ fullName: '', street: '', ... });

protected readonly address = form(this.model, (path) => {
  required(path.fullName, { message: 'Full name is required.' });
  maxLength(path.fullName, 100);
  // ...
});
```

```html
<input [formField]="address.fullName" />
```

`submit(form, action)` marks every field as touched, runs the action only when the form is valid, and exposes `submitting()` for the button state.

> Signal Forms are marked **experimental** in Angular 21. They are used here on purpose, to show where Angular forms are heading.

### Handling eventual consistency

With RabbitMQ, checkout returns an order ID **before** Ordering has created the order (see [architecture.md](architecture.md#asynchronous-checkout)). [`OrderDetail`](../src/Web/storefront/src/app/features/orders/order-detail.ts) handles this:

- when it arrives with `?placed=true` and the API answers `404`, a `processing` computed signal shows "Your order is being processed…" instead of an error;
- an `effect()` schedules `order.reload()` once a second (cleaned up via `onCleanup`), for up to 20 attempts;
- because `processing` also stays true while a reload is in flight, the page doesn't flicker back to a skeleton between attempts.

Without `placed`, a 404 is shown as "Order not found." straight away.

### Modern template syntax

The templates use built-in control flow (`@if`, `@else if`, `@for … track`), `@let` for local aliases, and lazy-loaded routes with `loadComponent`. View transitions are enabled with `withViewTransitions()`.

## Talking to the backend

The app only ever calls **relative** URLs (`/api/...`):

| Environment | Who forwards `/api` | Config |
|---|---|---|
| `npm start` | Angular dev server | [`proxy.conf.mjs`](../src/Web/storefront/proxy.conf.mjs), target `GATEWAY_URL` or `http://localhost:5100` |
| Aspire | Angular dev server | AppHost sets `GATEWAY_URL` to the gateway endpoint |
| Docker | nginx | [`nginx.conf`](../src/Web/storefront/nginx.conf), `proxy_pass http://api-gateway:8080` |

Because it is always same-origin, there is no CORS setup and no environment-specific build.

Errors are converted by [`problemMessage()`](../src/Web/storefront/src/app/core/problem-details.ts). It shows the first validation message, falls back to `detail` or `title`, and gives a clear hint when the gateway is unreachable (status `0`).

## Styling

- One global stylesheet ([`styles.scss`](../src/Web/storefront/src/styles.scss)) holds a small design system: CSS custom properties, `.btn`, `.card`, `.chip` and layout helpers
- **Light and dark themes** follow `prefers-color-scheme`
- Responsive down to phone width; two-column layouts collapse below 760px
- Skeleton loaders while resources load, toasts for feedback, an accessible `aria-live` region

## Testing

Tests use **Vitest** through the Angular CLI (`@angular/build:unit-test`) with `jsdom`.

| Spec | Covers |
|---|---|
| `app.spec.ts` | Shell renders; basket badge reflects the server cart |
| `core/basket-store.spec.ts` | Load per user, add-item request shape, checkout clears local state |
| `features/catalog/product-list.spec.ts` | Query params become request params; cards, active chip and pager render; unreachable-gateway message |
| `features/checkout/checkout-page.spec.ts` | Signal Form validation blocks submit; a valid form posts the exact address |
| `features/orders/order-list.spec.ts` | Orders are requested for the current user and rendered |
| `features/orders/order-detail.spec.ts` | After checkout a 404 shows "processing" and polls until the order exists; without `placed` a 404 is an immediate "not found" |

HTTP is faked with `HttpTestingController`. Because `httpResource` registers pending tasks, the specs flush requests first and then `await ApplicationRef.whenStable()`.

## Scripts

```bash
npm start                    # dev server on http://localhost:4200 with /api proxy
npm run build                # production build → dist/storefront/browser
npx ng test --watch=false    # run the unit tests once
npm run watch                # development build in watch mode
```
