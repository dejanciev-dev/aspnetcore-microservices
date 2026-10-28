# Getting started

This guide covers everything you need to run, configure and test the solution locally.

- [Prerequisites](#prerequisites)
- [First-time setup](#first-time-setup)
- [Run modes](#run-modes)
- [Ports](#ports)
- [Configuration](#configuration)
- [Running the tests](#running-the-tests)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Version | Check with |
|---|---|---|
| .NET SDK | 10.0.100 or later (see [`global.json`](../global.json)) | `dotnet --version` |
| Node.js | 22 LTS | `node -v` |
| npm | 10+ | `npm -v` |
| Docker Desktop / Engine | optional, for containers | `docker --version` |

Any IDE works: Visual Studio 2026, JetBrains Rider or VS Code with the C# Dev Kit and Angular Language Service extensions.

## First-time setup

```bash
git clone https://github.com/dejanciev-dev/aspnetcore-microservices.git
cd aspnetcore-microservices

dotnet restore aspnetcore-microservices.slnx
npm install --prefix src/Web/storefront
```

## Run modes

### 1. .NET Aspire AppHost

```bash
dotnet run --project src/Aspire/AppHost
```

The AppHost ([`src/Aspire/AppHost/AppHost.cs`](../src/Aspire/AppHost/AppHost.cs)):

1. starts `catalog-api` and `ordering-api`, then `basket-api` once both are healthy;
2. starts `api-gateway` once the services are healthy, and injects the real service URLs into the YARP cluster configuration;
3. runs `npm start` for the storefront with `GATEWAY_URL` pointing at the gateway;
4. opens the **Aspire dashboard**, where every service exports logs, metrics and traces over OTLP.

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

Every project uses the `http` launch profile with a fixed port (see [Ports](#ports)), so the defaults in `appsettings.json` line up with no extra configuration. Telemetry is not exported in this mode unless you set `OTEL_EXPORTER_OTLP_ENDPOINT`.

### 3. Docker Compose

```bash
docker compose up --build
```

The compose file builds:

- one image per .NET project from the shared [`src/Dockerfile`](../src/Dockerfile): a multi-stage build that ends on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (distroless, non-root, port 8080);
- the storefront from [`src/Web/storefront/Dockerfile`](../src/Web/storefront/Dockerfile): an Angular production build served by nginx, which also proxies `/api` to the gateway;
- a standalone **Aspire dashboard** container that receives OTLP telemetry from every service.

Stop everything with `docker compose down`.

## Ports

| Component | Local (`dotnet run` / Aspire) | Docker Compose (host) |
|---|---|---|
| Storefront | 4200 | 4200 |
| API gateway | 5100 | 5100 |
| Catalog.API | 5101 | internal only |
| Basket.API | 5102 | internal only |
| Ordering.API | 5103 | internal only |
| Aspire dashboard | 15100 (AppHost) | 18888 |

In Docker Compose only the gateway and the storefront are published. The services are reachable solely on the internal compose network, which is how they would run in production.

## Configuration

All settings use standard ASP.NET Core configuration, so any value can be overridden with an environment variable (use `__` for nesting).

| Setting | Used by | Default | Purpose |
|---|---|---|---|
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

## Running the tests

```bash
# backend: integration tests
dotnet test aspnetcore-microservices.slnx

# with coverage (Cobertura XML under tests/**/TestResults)
dotnet test aspnetcore-microservices.slnx --collect:"XPlat Code Coverage"

# front end: Vitest, single run
cd src/Web/storefront
npx ng test --watch=false
```

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

Basket validates every product against Catalog. Use one of the seeded IDs (`4f1c9a0e-0001-4a6e-9b1a-00000000000{1..8}`) or create a product first. The in-memory stores reset whenever a service restarts.
</details>
