using Azure.Core;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Sheaf.Api.Tests.Authentication;

/// <summary>
/// Starts the API as a deployed environment would. Settings left null are missing, so startup checks can be tested.
/// </summary>
internal sealed class ProductionApiFactory(
    string environment = "Production",
    string? keyIdentifier = null,
    string? mailHost = "smtp.example",
    bool mailTls = true) : ApiWebApplicationFactory
{
    public TokenCredential? CredentialUsedByKeyResolver { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        ConfigurationBuilder configuration = new();
        ConfigureStartupConfiguration(configuration);
        foreach (KeyValuePair<string, string?> setting in configuration.Build().AsEnumerable())
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.UseEnvironment(environment);
        if (keyIdentifier is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                ServiceDescriptor registration = services.Single(service => service.ServiceType == typeof(TokenCredential));
                Func<IServiceProvider, object> credentialFactory = registration.ImplementationFactory
                    ?? throw new InvalidOperationException("The registered credential factory is missing.");
                services.Replace(ServiceDescriptor.Singleton<TokenCredential>(provider =>
                {
                    TokenCredential credential = (TokenCredential)credentialFactory(provider);
                    CredentialUsedByKeyResolver = credential;
                    return credential;
                }));
            });
        }
    }

    protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
    {
        base.ConfigureStartupConfiguration(configurationBuilder);
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedOrigins:0"] = "https://client.example",
            ["DataProtection:KeyVaultKeyIdentifier"] = keyIdentifier,
            ["Email:Smtp:Host"] = mailHost,
            ["Email:Smtp:EnableSsl"] = mailTls ? "true" : "false",
            ["Email:From"] = "Sheaf <no-reply@example.com>"
        });
    }
}
