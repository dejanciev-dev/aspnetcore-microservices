# Getting started

This guide covers everything you need to run, configure and test the solution locally.

- [Prerequisites](#prerequisites)
- [First-time setup](#first-time-setup)
- [Run modes](#run-modes)
- [Ports](#ports)
- [Configuration](#configuration)
- [Databases and migrations](#databases-and-migrations)
- [Running the tests](#running-the-tests)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Version | Check with |
|---|---|---|
| .NET SDK | 10.0.100 or later (see [`global.json`](../global.json)) | `dotnet --version` |
| Node.js | 22 LTS | `node -v` |
| npm | 10+ | `npm -v` |
| Docker Desktop / Engine (or Podman) | for PostgreSQL/Redis and the integration tests | `docker --version` |

Any IDE works: Visual Studio 2026, JetBrains Rider or VS Code with the C# Dev Kit and Angular Language Service extensions.

## First-time setup

```bash
git clone https://github.com/dejanciev-dev/aspnetcore-microservices.git
cd aspnetcore-microservices

dotnet restore aspnetcore-microservices.slnx
dotnet tool restore                      # installs dotnet-ef from dotnet-tools.json
npm install --prefix src/Web/storefront
```

## Run modes

### 1. .NET Aspire AppHost

```bash
dotnet run --project src/Aspire/AppHost
```

The AppHost ([`src/Aspire/AppHost/AppHost.cs`](../src/Aspire/AppHost/AppHost.cs)):

1. starts **Keycloak** on port 8080 and imports the realm from `deploy/keycloak`, then a **PostgreSQL** container with two databases (`catalogdb`, `orderingdb`), a **Redis** container and a **RabbitMQ** container (with its management UI linked from the dashboard), all with persistent data volumes (requires Docker or Podman);
2. starts `catalog-api` and `ordering-api` once their database is ready, injecting `ConnectionStrings__catalogdb` / `__orderingdb`; they apply migrations on startup;
3. starts `basket-api` once Redis, Catalog and Ordering are healthy;
4. starts `api-gateway` once the services are healthy, and injects the real service URLs into the YARP cluster configuration;
5. runs `npm start` for the storefront with `GATEWAY_URL` pointing at the gateway;
6. opens the **Aspire dashboard**, where every service exports logs, metrics and traces (including SQL and Redis spans) over OTLP.

Health checks (`/health`) gate startup through `WaitFor(...)`, so nothing receives traffic before its dependencies are ready.

> **Tip:** in Visual Studio or Rider, set **AppHost** as the startup project and press F5.

### 2. Plain `dotnet run`

Use this mode when you want to debug a single service or avoid orchestration:

```bash
dotnet run --project src/Services/Catalog/Catalog.API
dotnet run --project src/Services/Basket/Basket.API
dotnet run --project src/Services/Ordering/Ordering.API
dotnet run --project src/ApiGateways/ApiGateway
npm start --prefix src/Web/storefront
```

Start Keycloak first; it's the one container this mode can't do without, because signing in needs it:

```bash
docker compose up -d keycloak
```

Every project uses the `http` launch profile with a fixed port (see [Ports](#ports)), so the defaults in `appsettings.json` (including `Identity:Authority=http://localhost:8080/realms/storefront`) line up with no extra configuration. With no connection strings set, **each service uses its in-memory store and checkout calls Ordering over HTTP**, so no Docker is needed. Telemetry is not exported in this mode unless you set `OTEL_EXPORTER_OTLP_ENDPOINT`.

To use real databases in this mode, start only the infrastructure from Compose and point the services at it:

```bash
docker compose up -d postgres redis rabbitmq
export ConnectionStrings__catalogdb="Host=localhost;Database=catalogdb;Username=postgres;Password=postgres"
export ConnectionStrings__orderingdb="Host=localhost;Database=orderingdb;Username=postgres;Password=postgres"
export ConnectionStrings__redis="localhost:6379"
export ConnectionStrings__rabbitmq="amqp://storefront:storefront@localhost:5672"
# then run the services as above
```

### 3. Docker Compose

```bash
docker compose up --build
```

The compose file builds:

- one image per .NET project from the shared [`src/Dockerfile`](../src/Dockerfile): a multi-stage build that ends on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (distroless, non-root, port 8080);
- the storefront from [`src/Web/storefront/Dockerfile`](../src/Web/storefront/Dockerfile): an Angular production build served by nginx, which also proxies `/api` to the gateway;
- **PostgreSQL 18**, **Redis 8** (AOF persistence) and **RabbitMQ 4** (with the management plugin) with health checks. The services wait until their dependencies report healthy before starting;
- a standalone **Aspire dashboard** container that receives OTLP telemetry from every service.

Stop everything with `docker compose down`. Data is kept in the `postgres-data`, `redis-data` and `rabbitmq-data` volumes; `docker compose down -v` resets it. Set `POSTGRES_PASSWORD` / `RABBITMQ_PASSWORD` in a `.env` file to change the default passwords.

## Ports

| Component | Local (`dotnet run` / Aspire) | Docker Compose (host) |
|---|---|---|
| Storefront | 4200 | 4200 |
| API gateway | 5100 | 5100 |
| Catalog.API | 5101 | internal only |
| Basket.API | 5102 | internal only |
| Ordering.API | 5103 | internal only |
| Keycloak | 8080 | 8080 |
| Aspire dashboard | 15100 (AppHost) | 18888 |
| PostgreSQL | dynamic (AppHost) | 5432 |
| Redis | dynamic (AppHost) | 6379 |
| RabbitMQ (AMQP / management UI) | dynamic (AppHost) | 5672 / 15672 |

In Docker Compose only the gateway and the storefront are published. The services are reachable solely on the internal compose network, which is how they would run in production.

## Configuration

All settings use standard ASP.NET Core configuration, so any value can be overridden with an environment variable (use `__` for nesting).

| Setting | Used by | Default | Purpose |
|---|---|---|---|
| `Identity:Authority` | all services, gateway | `http://localhost:8080/realms/storefront` | Keycloak realm; the token issuer |
| `Identity:MetadataAddress` | all services, gateway | *(derived from Authority)* | Where to fetch discovery and keys when the internal host differs (Compose) |
| `RateLimiting:Anonymous:*` / `RateLimiting:Authenticated:*` | gateway | burst 60 / 200, then 30 / 100 per 10 s | Global token buckets per IP / per user (`TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriodSeconds`) |
| `RateLimiting:Checkout:*` / `RateLimiting:Login:*` | gateway | 5 / 10 per 60 s | Fixed windows for checkout and sign-in starts (`PermitLimit`, `WindowSeconds`) |
| `OutputCaching:CatalogSeconds` | gateway | `60` | How long catalog reads are cached at the gateway |
| `Identity:ClientId` / `Identity:ClientSecret` | gateway | `storefront-bff` / dev secret | The BFF's confidential client; override the secret outside development |
| `ConnectionStrings:catalogdb` | Catalog.API | *(unset → in-memory)* | Npgsql connection string for the catalog database |
| `ConnectionStrings:orderingdb` | Ordering.API | *(unset → in-memory)* | Npgsql connection string for the ordering database |
| `ConnectionStrings:redis` | Basket.API | *(unset → in-memory)* | StackExchange.Redis configuration string, e.g. `localhost:6379` |
| `ConnectionStrings:rabbitmq` | Catalog, Basket, Ordering | *(unset → no messaging, HTTP checkout)* | AMQP URI, e.g. `amqp://user:pass@localhost:5672` |
| `Services:Catalog` | Basket.API | `http://localhost:5101` | Base URL for product and price lookups |
| `Services:Ordering` | Basket.API | `http://localhost:5103` | Base URL used to create orders on checkout |
| `ReverseProxy:Clusters:{name}:Destinations:d1:Address` | ApiGateway | `http://localhost:510x` | Where each YARP cluster forwards to |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | all .NET services | *(unset)* | Enables OTLP export of logs, metrics and traces |
| `OTEL_SERVICE_NAME` | all .NET services | assembly name | Service name shown in traces |
| `GATEWAY_URL` | Angular dev server | `http://localhost:5100` | Target of the `/api` dev proxy ([`proxy.conf.mjs`](../src/Web/storefront/proxy.conf.mjs)) |

Example: point Basket at a Catalog running elsewhere:

```bash
Services__Catalog=http://catalog.internal:8080 dotnet run --project src/Services/Basket/Basket.API
```

## Signing in

| User | Password | Roles |
|---|---|---|
| `ada` | `ada` | `admin`, `customer` |
| `grace` | `grace` | `customer` |

The Keycloak admin console is at http://localhost:8080 (`admin` / `admin` in Compose; Aspire shows its generated password in the dashboard). Changes made there are kept in the Keycloak volume. To go back to the realm file, delete the volume (`docker compose down -v`), because the realm is only imported when it doesn't exist yet.

For API clients and scripts, get a token from the test client and call the gateway or a service directly:

```bash
curl -s http://localhost:8080/realms/storefront/protocol/openid-connect/token \
  -d grant_type=password -d client_id=storefront-tests -d username=ada -d password=ada | jq -r .access_token
```

## Databases and migrations

Migrations live in `Data/Migrations/` of Catalog.API and Ordering.API and are applied automatically on startup. To change a schema:

```bash
# 1. edit the entity / OnModelCreating, then add a migration
dotnet ef migrations add AddProductSku --project src/Services/Catalog/Catalog.API --output-dir Data/Migrations

# 2. optional: inspect the SQL it will run
dotnet ef migrations script --project src/Services/Catalog/Catalog.API --idempotent

# 3. run the service; MigrateAsync() applies it
```

`dotnet ef` needs no running database to add a migration: each context has an `IDesignTimeDbContextFactory`.

To look at the data with Docker Compose:

```bash
docker compose exec postgres psql -U postgres -d catalogdb -c "select name, price from products"
docker compose exec redis redis-cli --scan --pattern 'basket:*'
docker compose exec postgres psql -U postgres -d catalogdb -c "select type, occurred_at, processed_at from outbox_messages"
docker compose exec rabbitmq rabbitmqctl list_queues name messages consumers
```

To watch messaging in action: change a product's price (`PUT /api/products/{id}` in Catalog's Scalar UI) while that product sits in a basket, then reload the basket. Queues, rates and dead-lettered messages are visible in the RabbitMQ management UI at http://localhost:15672.

## Running the tests

The test projects use **xUnit v3** on **Microsoft.Testing.Platform** (opted in via `global.json`).

```bash
# everything: 25 API tests + 18 PostgreSQL/Redis/RabbitMQ/Keycloak tests
dotnet test

# with TRX reports and Cobertura coverage
dotnet test --solution aspnetcore-microservices.slnx --report-trx --coverage --coverage-output-format cobertura --results-directory TestResults

# front end: Vitest, single run
cd src/Web/storefront
npx ng test --watch=false
```

`tests/Services.IntegrationTests` starts PostgreSQL, Redis, RabbitMQ and Keycloak with **Testcontainers**, so it needs Docker. Without Docker the 18 tests are reported as **skipped** locally. When the `CI` environment variable is set (GitHub Actions sets it), a missing Docker is a failure instead, so the pipeline can never pass without actually testing the databases.

See the "Testing" sections in [architecture.md](architecture.md#testing-strategy) and [frontend.md](frontend.md#testing) for what each suite covers.

## Troubleshooting

<details>
<summary><b>The storefront shows "The API gateway is unreachable"</b></summary>

The Angular dev server forwards `/api` to the gateway. Make sure the gateway is running (`curl http://localhost:5100/health` should return `Healthy`). If it runs on another port, start the front end with `GATEWAY_URL=http://localhost:<port> npm start`.
</details>

<details>
<summary><b><code>Failed to bind to address ... address already in use</code></b></summary>

Another process is using one of the fixed ports. Find it (`netstat -ano | findstr :5100` on Windows, `lsof -i :5100` on macOS/Linux) and stop it, or change `applicationUrl` in the project's `Properties/launchSettings.json` and update the matching `Services:*` / `ReverseProxy` settings. The gateway deliberately avoids port 5000, which is often taken (AirPlay on macOS, other ASP.NET apps).
</details>

<details>
<summary><b>The Aspire AppHost stops at "Distributed application starting"</b></summary>

The AppHost launches resources through the Aspire orchestrator (DCP). If nothing starts:

- stop any previous AppHost instance (ports 15100, 19100 and 20100 must be free);
- install the Aspire CLI (`dotnet tool install -g Aspire.Cli`) and start with `aspire run` from `src/Aspire/AppHost`;
- make sure security software isn't blocking `dcp.exe` from the NuGet cache.

You can always fall back to [run mode 2](#2-plain-dotnet-run).
</details>

<details>
<summary><b>Basket returns 400 "Product ... does not exist"</b></summary>

Basket validates every product against Catalog. Use one of the seeded IDs (`4f1c9a0e-0001-4a6e-9b1a-00000000000{1..8}`) or create a product first. If you run without connection strings, the in-memory stores reset whenever a service restarts.
</details>

<details>
<summary><b>Catalog/Ordering exits on startup with <code>NpgsqlException: Failed to connect</code></b></summary>

A connection string is set but PostgreSQL isn't reachable. Start it (`docker compose up -d postgres`), check host, port and password, or unset `ConnectionStrings__catalogdb` / `__orderingdb` to fall back to in-memory.
</details>

<details>
<summary><b>Keycloak says "Invalid parameter: redirect_uri"</b></summary>

The gateway builds the callback URL from the host the browser used. Only `http://localhost:4200/signin-oidc` and `http://localhost:5100/signin-oidc` are registered on the `storefront-bff` client. Open the storefront on one of those (not `127.0.0.1`), or add your URL to the client in Keycloak.
</details>

<details>
<summary><b>Signing in ends with "Correlation failed" or loops back to the login page</b></summary>

The browser didn't send back the OIDC correlation cookie. Make sure the dev proxy or nginx forwards `/signin-oidc` to the gateway **with the original Host header** (`changeOrigin: false`, `proxy_set_header Host $http_host`), and clear cookies for `localhost`.
</details>

<details>
<summary><b>Requests fail with 429 "Too many requests"</b></summary>

The gateway's rate limiter kicked in; the response's `Retry-After` header says when to retry. Load tests and scripts hit the anonymous per-IP budget quickly. Raise the limits for local work, e.g. `RateLimiting__Anonymous__TokenLimit=1000`, or call a service directly on its own port, which bypasses the gateway.
</details>

<details>
<summary><b>The catalog shows old data after editing the database directly</b></summary>

The gateway caches catalog reads for `OutputCaching:CatalogSeconds` (60 s by default). Edits made through the API clear the cache at once; edits made in PostgreSQL or by calling Catalog.API on its own port don't. Wait for the entries to expire, restart the gateway, or lower the duration, e.g. `OutputCaching__CatalogSeconds=1`.
</details>

<details>
<summary><b>APIs return 401 although I'm signed in</b></summary>

- **Through the gateway:** the session may have expired (sign in again), or the issuer doesn't match. Tokens are issued for the URL the *browser* uses for Keycloak, so `Identity:Authority` must be exactly that (`http://localhost:8080/realms/storefront`).
- **Calling a service directly:** send `Authorization: Bearer <token>`; the token's audience must include `storefront-api`.
</details>

<details>
<summary><b>The order page keeps saying "Your order is being processed…"</b></summary>

With RabbitMQ, Ordering creates the order from the `BasketCheckedOut` message. If it never appears, check that Ordering.API is running and consuming (`rabbitmqctl list_queues name messages consumers` should show a consumer on `ordering-api.basket.checked-out`), and look in `ordering-api.basket.checked-out.dlq` for messages whose handler failed.
</details>

<details>
<summary><b>A service exits with <code>BrokerUnreachableException</code></b></summary>

`ConnectionStrings__rabbitmq` is set but RabbitMQ isn't reachable, or the credentials are wrong. Note that RabbitMQ's default `guest` user can only connect from localhost, which is why Compose creates a `storefront` user.
</details>

<details>
<summary><b>Integration tests are all "skipped"</b></summary>

Docker isn't running or reachable. Start Docker Desktop (or set `DOCKER_HOST`) and run `dotnet test` again.
</details>
