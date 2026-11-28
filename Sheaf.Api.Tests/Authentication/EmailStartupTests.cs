using Microsoft.Extensions.Options;

namespace Sheaf.Api.Tests.Authentication;

public class EmailStartupTests
{
    private const string KeyIdentifier = "https://sheaf-test.vault.azure.net/keys/data-protection";

    [Test]
    public async Task Startup_ProductionWithoutMailServer_Fails()
    {
        await using ProductionApiFactory factory = new(keyIdentifier: KeyIdentifier, mailHost: null);

        await Assert.That(() => factory.CreateClient()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Startup_ProductionMailServerWithoutTls_Fails()
    {
        await using ProductionApiFactory factory = new(keyIdentifier: KeyIdentifier, mailTls: false);

        await Assert.That(() => factory.CreateClient()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Startup_ProductionWithMailServer_Starts()
    {
        await using ProductionApiFactory factory = new(keyIdentifier: KeyIdentifier);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }
}
