# Storefront (Angular 21)

The customer-facing single-page app of the [ASP.NET Core Microservices](../../../README.md) solution. It talks to the backend only through the YARP API gateway.

![Angular](https://img.shields.io/badge/Angular-21-DD0031?logo=angular&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-5.9-3178C6?logo=typescript&logoColor=white)
![Vitest](https://img.shields.io/badge/tested%20with-Vitest-6E9F18?logo=vitest&logoColor=white)

## Highlights

- **Zoneless**, standalone components, **signals** everywhere
- **`httpResource`** for reactive, declarative data loading
- **Signal Forms** for the checkout form
- Router **component input binding**, lazy routes, view transitions
- Signal-based `BasketStore`, with no external state library
- ProblemDetails-aware error messages, toasts, skeleton loaders
- Light and dark themes, responsive layout

## Running

The backend must be running (see the [root README](../../../README.md#-quick-start)).

```bash
npm install
npm start          # http://localhost:4200
```

`/api` requests are proxied to the gateway at `http://localhost:5100`. Override this with `GATEWAY_URL`:

```bash
GATEWAY_URL=http://localhost:6000 npm start
```

When started from the .NET Aspire AppHost, `GATEWAY_URL` is set automatically.

## Scripts

| Command | Description |
|---|---|
| `npm start` | Dev server with the API proxy |
| `npm run build` | Production build to `dist/storefront/browser` |
| `npx ng test --watch=false` | Unit and component tests (Vitest) |
| `npm run watch` | Development build in watch mode |

## Container

```bash
docker build -t storefront .
```

Builds the app on `node:22-slim` and serves it from `nginx:alpine`. [`nginx.conf`](nginx.conf) proxies `/api` to `api-gateway:8080`, caches hashed assets and falls back to `index.html` for client-side routes. The image is normally built through the root `docker-compose.yml`.

## Learn more

[docs/frontend.md](../../../docs/frontend.md) covers the structure, patterns and testing approach in depth.
