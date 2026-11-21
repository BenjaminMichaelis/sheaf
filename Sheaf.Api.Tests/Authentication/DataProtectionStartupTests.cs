using Microsoft.AspNetCore.Hosting;

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

    private sealed class ProductionDataProtectionFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            ConfigurationBuilder configuration = new();
            ConfigureStartupConfiguration(configuration);
            foreach (KeyValuePair<string, string?> setting in configuration.Build().AsEnumerable())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseEnvironment("Production");
        }

        protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
        {
            base.ConfigureStartupConfiguration(configurationBuilder);
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedOrigins:0"] = "https://client.example"
            });
        }
    }
}
