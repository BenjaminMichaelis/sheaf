using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Identity;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests.Authentication;

public class DataProtectionStartupTests
{
    [Test]
    public async Task Startup_ProductionWithoutKeyProtector_Fails()
    {
        await using ProductionDataProtectionFactory factory = new();

        await Assert.That(() => factory.CreateClient())
            .Throws<InvalidOperationException>()
            .WithMessage("DataProtection:KeyVaultKeyIdentifier must be configured as an absolute HTTPS URI outside Development and Testing.");
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    public async Task Startup_DeployedEnvironmentWithKeyProtector_UsesManagedIdentityCredential(string environment)
    {
        await using ProductionDataProtectionFactory factory = new(
            environment, "https://sheaf-test.vault.azure.net/keys/data-protection");

        factory.Services.GetRequiredService<IKeyEncryptionKeyResolver>();

        await Assert.That(factory.CredentialUsedByKeyResolver).IsTypeOf<ManagedIdentityCredential>();
    }

    private sealed class ProductionDataProtectionFactory(
        string environment = "Production",
        string? keyIdentifier = null) : ApiWebApplicationFactory
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
                ["DataProtection:KeyVaultKeyIdentifier"] = keyIdentifier
            });
        }
    }
}
