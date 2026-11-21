using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

public class ApiWebApplicationFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
    }

    protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Server=localhost;Database=SheafTests;TrustServerCertificate=True"
        });
    }
}
