# Deployment

The same container images run in four places. Docker Compose and Aspire are covered in [Getting started](getting-started.md#run-modes). This page covers **Kubernetes** and **Azure Container Apps**.

| | Docker Compose | Kubernetes ([`deploy/k8s`](../deploy/k8s)) | Azure Container Apps ([`infra/`](../infra), `azd`) |
|---|---|---|---|
| Definition | `docker-compose.yml` | Kustomize base + `kind` overlay | Bicep + `azure.yaml` |
| Public entry | storefront :4200, Keycloak :8080 | NodePorts (kind) / your ingress | HTTPS ingress: storefront, Keycloak |
| PostgreSQL | container | StatefulSet + volume | Azure Database for PostgreSQL Flexible Server |
| Redis, RabbitMQ | containers | StatefulSets + volumes | container apps (TCP ingress, no volumes) |
| Keycloak | dev mode, realm mounted | dev mode, realm from a ConfigMap | production mode on PostgreSQL, realm baked into the image |
| Secrets | `.env` defaults | `Secret` (overlay `secretGenerator`) | Container Apps secrets from `azd` parameters |
| Verified in CI | Playwright suite | kind cluster + Playwright suite | Bicep build and lint, `azure.yaml` schema; not deployed |

## Kubernetes

### Layout

```
deploy/
├── keycloak/                   # realm JSON (shared by every run mode) + kustomization → ConfigMap
└── k8s/
    ├── base/                   # namespace, workloads, services, shared config
    └── overlays/kind/          # localhost URLs, dev secrets, NodePorts, kind cluster config
```

The **base** is shaped for a real cluster: `ASPNETCORE_ENVIRONMENT=Production`, HTTPS-only identity metadata, and placeholder public URLs under `example.com`. Overlays supply what differs per environment: the public Keycloak URL, secrets, the image registry and tags, and how traffic gets in.

### Workloads

| Workload | Kind | Replicas | Probes |
|---|---|---|---|
| catalog-migrate, ordering-migrate | Job | once per apply | – |
| catalog-api, ordering-api | Deployment | 2 (the Jobs own the schema) | startup and liveness `/alive`, readiness `/health` (includes "schema up to date") |
| basket-api | Deployment | 2 (stateless) | same |
| api-gateway | Deployment | 2 (Data Protection keys and output cache shared through Redis) | same |
| storefront | Deployment | 2 | `GET /` |
| postgres, redis, rabbitmq | StatefulSet with a volume | 1 | `pg_isready`, `redis-cli ping`, `rabbitmq-diagnostics ping` |
| keycloak | Deployment | 1 | `/health/started`, `/health/live`, `/health/ready` on port 9000 |
| aspire-dashboard | Deployment | 1 | – |

**Liveness vs readiness.** `/health` runs every registered check (PostgreSQL, Redis, RabbitMQ). `/alive` runs none. If liveness used `/health`, a database outage would make Kubernetes restart every healthy pod in a loop. With `/alive` for liveness, the pods only leave the Service's endpoints until the dependency is back.

**Hardening.** The .NET pods run as the chiseled image's non-root user (uid 1654) with a read-only root filesystem (an `emptyDir` on `/tmp`; the gateway keeps its Data Protection keys in Redis), no privilege escalation, all capabilities dropped, the `RuntimeDefault` seccomp profile, and no service-account token. They meet the *restricted* Pod Security Standard. The namespace enforces *baseline*, because the stock postgres, redis, rabbitmq and nginx images start as root.

**Scaling out.** Every application workload runs two replicas, and the E2E suite runs against them through the Services' load balancing:

- **Gateway:** the Data Protection key ring (which encrypts the session cookie and the OIDC correlation and nonce cookies) and the output cache live in Redis. A sign-in can start on one pod and finish on another, and a catalog write evicts every pod's cached reads. Rate-limit counters stay per pod, so the effective limit is the configured one times the replica count.
- **Catalog and Ordering:** the `catalog-migrate` and `ordering-migrate` Jobs run the service images with the `migrate` argument (migrations, plus Catalog's seed). The Deployments set `Database__MigrateOnStartup=false`, and their readiness waits for the schema. Jobs are immutable, so finished ones delete themselves after ten minutes (`ttlSecondsAfterFinished`). The next `kubectl apply` recreates them, and migrating an up-to-date database is a no-op.

**Configuration.** A generated `service-config` ConfigMap holds what every .NET service shares. Connection strings are assembled from `Secret` keys with Kubernetes' `$(VAR)` expansion, so no password is written into a manifest.

**Keycloak.** The realm is the same `deploy/keycloak/storefront-realm.json` that Compose, Aspire and the tests import. A `kustomization.yaml` next to it turns it into a ConfigMap; Compose and Aspire ignore that file because Keycloak only imports `*.json`.

### Running it on kind

[kind](https://kind.sigs.k8s.io/) runs a cluster in Docker. `kind-config.yaml` maps the NodePorts to the same localhost ports as Docker Compose, so the browser, Keycloak's issuer and the BFF redirect URIs need no changes.

```bash
kind create cluster --config deploy/k8s/overlays/kind/kind-config.yaml
docker compose build                       # tags storefront/<service>:local
for s in catalog-api basket-api ordering-api api-gateway storefront; do
  kind load docker-image storefront/$s:local --name storefront
done
kubectl apply -k deploy/k8s/overlays/kind
kubectl -n storefront wait deployment --all --for=condition=Available --timeout=10m
```

Then open http://localhost:4200 and sign in as `ada` / `ada` or `grace` / `grace`. The Aspire dashboard is on http://localhost:18888. `kind delete cluster --name storefront` removes everything.

CI does exactly this on every push and then runs the [Playwright suite](getting-started.md#end-to-end-tests-playwright) against the cluster.

### A real cluster

Add an overlay that:

- points `images` at your registry and a version tag;
- sets `Identity__Authority` and `KC_HOSTNAME` to Keycloak's public HTTPS URL (and leaves `Identity__RequireHttpsMetadata` at its default);
- provides `storefront-secrets` from your secret store (for example External Secrets) instead of literals;
- exposes `storefront` and `keycloak` through your Ingress or Gateway API controller with TLS;
- ideally replaces the in-cluster PostgreSQL, Redis and RabbitMQ with managed services, and runs Keycloak in production mode (see the Container Apps image).

## Azure Container Apps

### What `azd up` creates

```
Internet ──HTTPS──▶ storefront (nginx) ──▶ api-gateway ──▶ catalog-api · basket-api · ordering-api
         ──HTTPS──▶ keycloak                                   │            │            │
                                                     PostgreSQL Flexible Server · redis · rabbitmq
```

| Resource | Notes |
|---|---|
| Resource group `rg-<env>` | everything below |
| Container Apps environment | VNet-integrated (TCP ingress needs it), Consumption workload profile, logs to Log Analytics |
| Container registry (Basic) | images built in the cloud (`remoteBuild`), pulled by a user-assigned managed identity with `AcrPull` |
| PostgreSQL Flexible Server (B1ms, v16) | databases `catalogdb`, `orderingdb`, `keycloak`; TLS required; reachable from Azure only |
| Container apps | `storefront` and `keycloak` with public HTTPS ingress; `api-gateway`, the three services, `redis` and `rabbitmq` internal only |

Keycloak runs in production mode (`start --optimized`) from [`deploy/keycloak/Dockerfile`](../deploy/keycloak/Dockerfile), with its data in PostgreSQL and the realm baked into the image. Two import placeholders in the realm are filled from the environment: `STOREFRONT_URL` (an extra redirect URI and post-logout URI) and `STOREFRONT_BFF_CLIENT_SECRET`. Their defaults are the local values, so every other run mode imports the realm unchanged.

### Deploy

Requires the [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) and an Azure subscription. Docker is not needed: images are built in the registry.

```bash
azd auth login
azd up          # prompts for an environment name, subscription, region and the four secrets
azd up          # a second run attaches the health probes to the deployed images (see below)
```

`azd up` prints `STOREFRONT_URL` and `KEYCLOAK_URL`. Later code changes need only `azd deploy` (or `azd deploy <service>`). `azd down --purge` deletes everything.

The four secure parameters are the PostgreSQL admin password (it must meet Azure's complexity rules), the RabbitMQ password, the Keycloak admin password and the BFF client secret. `azd` stores them in the environment's local configuration under `.azure/`, which is git-ignored.

### Behind the Container Apps ingress

Running behind a TLS-terminating proxy that routes internal traffic by `Host` needed three things that are also used, unchanged, by the other run modes:

- **nginx upstream from the environment.** [`nginx.conf`](../src/Web/storefront/nginx.conf) is a template: `GATEWAY_URL` is `http://api-gateway:8080` by default and `http://api-gateway` here. nginx sends the gateway's own `Host`, puts the browser's in `X-Forwarded-Host`, and passes on the outer proxy's `X-Forwarded-Proto`.
- **A fixed public origin for the BFF.** `Bff__PublicOrigin` makes the gateway build OIDC redirect URIs and `Secure` cookies from the storefront's URL, instead of trusting whichever forwarded headers survive two proxies.
- **The real client IP.** `ForwardedHeaders__KnownNetworks__0` is the VNet range, so the gateway walks `X-Forwarded-For` back through the environment's own proxies. Anonymous rate limits stay per visitor instead of collapsing into one shared budget.

### Caveats

- **Not deployed by CI.** CI compiles and lints the Bicep and validates `azure.yaml` against the azd schema on every push, but nothing deploys the templates automatically. Treat the first `azd up` as the real test.
- **Probes start on the second provision.** Before the first `azd deploy`, each app runs a placeholder image on the same port (the ASP.NET sample, or hello-world for nginx) without probes, so the first revision is healthy. Once real images exist, the next `azd provision` / `azd up` keeps them and adds the probes.
- **Redis and RabbitMQ are not durable.** They have no volumes here: a restart loses open baskets and unconsumed messages (orders already written to PostgreSQL are safe), and the gateway's Data Protection keys, which signs everyone out. Azure Managed Redis and Azure Service Bus are the production choices; the latter would need a different messaging transport.
- **No telemetry export.** `OTEL_EXPORTER_OTLP_ENDPOINT` isn't set, so only console logs reach Log Analytics. Pointing it at an OpenTelemetry collector or Application Insights is a follow-up.
- **Demo identities are public.** The realm's demo users (`ada` / `ada` with the admin role, `grace` / `grace`), the `storefront-tests` password-grant client and the localhost redirect URIs come along. Change or remove them in the Keycloak admin console, or keep the deployment private.
- **Catalog and Ordering stay at one replica.** They still migrate at startup here; a Container Apps job running `migrate` (the Kubernetes setup's approach) would lift that. The gateway already scales to three replicas.
- **Costs.** PostgreSQL B1ms, the registry and the always-on replicas (min 1 per app) incur charges while the environment exists.
