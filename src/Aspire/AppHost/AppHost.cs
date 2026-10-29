// .NET Aspire orchestration: one F5 starts Keycloak, PostgreSQL, Redis, RabbitMQ, every service, the gateway and
// the Angular app, wires their connection strings and URLs together and streams logs, metrics
// and traces to the Aspire dashboard.

var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure runs in containers (Docker or Podman); data volumes survive restarts.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Message broker for integration events; the management UI is linked from the dashboard.
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Identity provider. Port 8080 is fixed so the issuer matches the realm import and the
// docker-compose setup; the realm (clients, roles, demo users) is imported on first start.
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithDataVolume()
    .WithRealmImport("../../../deploy/keycloak")
    .WithLifetime(ContainerLifetime.Persistent);
var authority = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/storefront");

// WithReference injects ConnectionStrings__<name>, which switches each service from its
// in-memory store to the real database, and from HTTP-only to RabbitMQ messaging.
var catalog = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("Identity__Authority", authority)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health");

var ordering = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(orderingDb)
    .WaitFor(orderingDb)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("Identity__Authority", authority)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health");

var basket = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("Identity__Authority", authority)
    .WaitFor(keycloak)
    .WithEnvironment("Services__Catalog", catalog.GetEndpoint("http"))
    .WithEnvironment("Services__Ordering", ordering.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WaitFor(catalog)
    .WaitFor(ordering);

// The gateway is the BFF: it signs users in with Keycloak and relays their access tokens.
var gateway = builder.AddProject<Projects.ApiGateway>("api-gateway")
    .WithReference(redis)            // shared Data Protection keys and output cache
    .WaitFor(redis)
    .WithEnvironment("Identity__Authority", authority)
    .WaitFor(keycloak)
    .WithEnvironment("ReverseProxy__Clusters__catalog__Destinations__d1__Address", catalog.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__basket__Destinations__d1__Address", basket.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__ordering__Destinations__d1__Address", ordering.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WaitFor(catalog)
    .WaitFor(basket)
    .WaitFor(ordering);

builder.AddJavaScriptApp("storefront", "../../Web/storefront", "start")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(gateway);

builder.Build().Run();
