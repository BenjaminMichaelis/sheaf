using System.Text.Json;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

public class ApiTests : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    [Test]
    public async Task GetCurrentUser_AnonymousRequest_ReturnsUnauthenticated()
    {
        using var client = Factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/user");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(content.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
    }
}
