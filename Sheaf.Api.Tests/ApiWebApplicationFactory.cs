using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

public sealed class ApiWebApplicationFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Server=localhost;Database=SheafTests;TrustServerCertificate=True",
            ["Auth:SigningKey"] = "ThisIsATestSigningKeyAtLeast32CharactersLong"
        });
    }
}
