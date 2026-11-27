using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Sheaf.Integrations.PlanningCenter;

public static class PlanningCenterServiceCollectionExtensions
{
    public static IServiceCollection AddPlanningCenter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlanningCenterOptions>()
            .Bind(configuration.GetSection(PlanningCenterOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PlanningCenterOptions>, PlanningCenterOptionsValidator>());

        // EXTEXP0001: RemoveAllResilienceHandlers is the only way to opt one client out of the service defaults' shared
        // pipeline. PlanningCenterCallback_TokenEndpointUnavailable_DoesNotRetryCodeExchange guards the behavior.
#pragma warning disable EXTEXP0001
        // The OpenID Connect handler keeps this client as its backchannel for the app's lifetime, so the factory never
        // rotates its handler. Recycling pooled connections lets it follow DNS changes, as Microsoft's HttpClient
        // guidelines recommend for long-lived clients.
        services.AddHttpClient(PlanningCenterOAuthClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .ConfigureHttpClient((provider, client) =>
            {
                PlanningCenterOptions options = provider.GetRequiredService<IOptions<PlanningCenterOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                // The limit the handler applies to the backchannel it would otherwise create.
                client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
            })
            // The service defaults give every client one shared standard pipeline that retries all methods. OAuth posts
            // must not be retried: a code is single-use and a refresh rotates the refresh token. So this client
            // replaces that pipeline with its own, which retries only safe methods: the discovery document and keys.
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
#pragma warning restore EXTEXP0001

        // The connect step is Microsoft's OpenID Connect handler; Sheaf supplies its options and events.
        services.AddAuthentication().AddOpenIdConnect(PlanningCenterAuthenticationDefaults.Scheme, _ => { });
        services.ConfigureOptions<ConfigurePlanningCenterOpenIdConnect>();
        services.AddScoped<PlanningCenterOpenIdConnectEvents>();

        services.AddSingleton<PlanningCenterOAuthClient>();
        services.TryAddSingleton<IntegrationCredentialProtector>();
        services.TryAddSingleton<IntegrationRefreshLocks>();
        services.AddScoped<PlanningCenterConnectionService>();
        services.AddScoped<PlanningCenterTokenProvider>();
        return services;
    }
}
