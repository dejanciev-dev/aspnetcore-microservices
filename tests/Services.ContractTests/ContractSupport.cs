using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PactNet;
using PactNet.Infrastructure.Outputters;
using PactNet.Verifier;

namespace Services.ContractTests;

/// <summary>
/// Where the pact files live and how they are written. They are committed under <c>pacts/</c>:
/// consumer tests regenerate them, provider tests verify the committed copies, and CI fails when a
/// consumer test changed a contract without the new file being committed.
/// </summary>
internal static class Pacts
{
    /// <summary>The Pact core sends anonymous usage statistics unless told not to.</summary>
    [ModuleInitializer]
    internal static void DisableTracking() => Environment.SetEnvironmentVariable("PACT_DO_NOT_TRACK", "true");

    /// <summary>The JSON shape on the wire: ASP.NET Core's and the RabbitMQ bus's web defaults (camelCase).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Directory { get; } = Path.Combine(ProjectDirectory(), "pacts");

    public static PactConfig Config => new()
    {
        PactDir = Directory,
        DefaultJsonSettings = Json,
        Outputters = [new XunitOutput()],
        LogLevel = PactLogLevel.Warn,
    };

    public static PactVerifierConfig VerifierConfig => new()
    {
        Outputters = [new XunitOutput()],
        LogLevel = PactLogLevel.Warn,
    };

    public static FileInfo File(string consumer, string provider) => new(Path.Combine(Directory, $"{consumer}-{provider}.json"));

    private static string ProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (dir.GetFiles("Services.ContractTests.csproj").Length > 0) return dir.FullName;
        throw new InvalidOperationException("Services.ContractTests.csproj not found above the test output directory.");
    }

    private sealed class XunitOutput : IOutput
    {
        public void WriteLine(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);
    }
}

/// <summary>Provider-state name → seeds the provider's stores, given the state's parameters.</summary>
internal sealed class ProviderStates : Dictionary<string, Func<IServiceProvider, IReadOnlyDictionary<string, string>, Task>>;

/// <summary>Message description → produces the event the provider would publish.</summary>
internal sealed class ProviderMessages : Dictionary<string, Func<IServiceProvider, Task<object>>>;

/// <summary>
/// Hosts a provider on a real Kestrel port (the Pact verifier talks to it over the network) with
/// its in-memory stores, plus two endpoints for the verifier: provider states, which seed the stores
/// before each interaction, and messages, which return the event for a message interaction.
/// </summary>
/// <remarks>
/// PactNet 5.0.1's own <c>WithMessages</c> registers its listener with a <c>message://</c> URL the
/// Rust verifier rejects ("builder error for url"). Serving messages over HTTP from the provider is
/// the same protocol, and builds each event inside the real service.
/// </remarks>
internal sealed class ProviderHost<TMarker> : IAsyncDisposable where TMarker : class
{
    private const string StatePath = "/_pact/provider-states";
    private const string MessagesPath = "/_pact/messages";

    private readonly WebApplicationFactory<TMarker> _factory;

    public ProviderHost(ProviderStates? states = null, ProviderMessages? messages = null)
    {
        _factory = new WebApplicationFactory<TMarker>().WithWebHostBuilder(b => b
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new PactEndpoints(states ?? [], messages ?? []));
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = AnyBearerHandler.SchemeName;
                        options.DefaultAuthenticateScheme = AnyBearerHandler.SchemeName;
                        options.DefaultChallengeScheme = AnyBearerHandler.SchemeName;
                        options.DefaultForbidScheme = AnyBearerHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, AnyBearerHandler>(AnyBearerHandler.SchemeName, _ => { });
            }));
        // A free port on loopback; UseKestrel(0) binds "localhost", where dynamic ports aren't supported.
        _factory.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _factory.StartServer();

        BaseUri = new Uri(_factory.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First());
    }

    public Uri BaseUri { get; }

    public Uri StateUri => new(BaseUri, StatePath);

    public Uri MessagesUri => new(BaseUri, MessagesPath);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private sealed class PactEndpoints(ProviderStates states, ProviderMessages messages) : IStartupFilter
    {
        private sealed record StateRequest(string? State, Dictionary<string, JsonElement>? Params);
        private sealed record MessageRequest(string Description);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(StatePath, endpoint => endpoint.Run(async context =>
            {
                var request = await context.Request.ReadFromJsonAsync<StateRequest>(Pacts.Json);
                if (request?.State is not { Length: > 0 } state) return;   // interactions without a "given"

                if (!states.TryGetValue(state, out var setUp))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync($"Unknown provider state '{state}'.");
                    return;
                }

                await setUp(context.RequestServices, (request.Params ?? []).ToDictionary(p => p.Key, p => p.Value.ToString()));
            }));

            app.Map(MessagesPath, endpoint => endpoint.Run(async context =>
            {
                var request = await context.Request.ReadFromJsonAsync<MessageRequest>(Pacts.Json);
                if (request is null || !messages.TryGetValue(request.Description, out var produce))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"No message '{request?.Description}'.");
                    return;
                }

                var message = await produce(context.RequestServices);
                await context.Response.WriteAsJsonAsync(message, message.GetType(), Pacts.Json);
            }));

            next(app);
        };
    }
}

/// <summary>
/// Token validation is not part of the contract (the Keycloak integration tests cover it): any
/// bearer token authenticates as <see cref="UserName"/>.
/// </summary>
internal sealed class AnyBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AnyBearer";
    public const string UserName = "contract-user";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim("preferred_username", UserName)], SchemeName, "preferred_username", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

/// <summary>Consumer and provider tests share the pact files, so they never run concurrently.</summary>
[CollectionDefinition(Name)]
public sealed class PactCollection
{
    public const string Name = "Pacts";
}
