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
        services.AddHttpClient(PlanningCenterOAuthClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                PlanningCenterOptions options = provider.GetRequiredService<IOptions<PlanningCenterOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            // The service defaults give every client one shared standard pipeline that retries all methods. OAuth posts
            // must not be retried: a code is single-use and a refresh rotates the refresh token. So this client
            // replaces that pipeline with its own, which retries only safe methods such as the userinfo GET.
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
