using Azure.Core.Cryptography;
using Azure.Identity;

using Microsoft.Extensions.DependencyInjection;

namespace Sheaf.Api.Tests.Authentication;

public class DataProtectionStartupTests
{
    [Test]
    public async Task Startup_ProductionWithoutKeyProtector_Fails()
    {
        await using ProductionApiFactory factory = new();

        await Assert.That(() => factory.CreateClient())
            .Throws<InvalidOperationException>()
            .WithMessage("DataProtection:KeyVaultKeyIdentifier must be configured as an absolute HTTPS URI outside Development and Testing.");
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    public async Task Startup_DeployedEnvironmentWithKeyProtector_UsesManagedIdentityCredential(string environment)
    {
        await using ProductionApiFactory factory = new(
            environment, "https://sheaf-test.vault.azure.net/keys/data-protection");

        factory.Services.GetRequiredService<IKeyEncryptionKeyResolver>();

        await Assert.That(factory.CredentialUsedByKeyResolver).IsTypeOf<ManagedIdentityCredential>();
    }
}
