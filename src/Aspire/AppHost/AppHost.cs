// .NET Aspire orchestration: one F5 starts every service, the gateway and the Angular app,
// wires their URLs together and streams logs, metrics and traces to the Aspire dashboard.

var builder = DistributedApplication.CreateBuilder(args);

var catalog = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithHttpHealthCheck("/health");

var ordering = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithHttpHealthCheck("/health");

var basket = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithEnvironment("Services__Catalog", catalog.GetEndpoint("http"))
    .WithEnvironment("Services__Ordering", ordering.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WaitFor(catalog)
    .WaitFor(ordering);

var gateway = builder.AddProject<Projects.ApiGateway>("api-gateway")
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
