using System.Net.Http.Json;
using System.Text.Json;

using Sheaf.Endpoints;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

public class ApiTests : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    [Test]
    public async Task AuthUserEndpoint_AnonymousRequest_ReturnsUnauthenticated()
    {
        using var client = Factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/user");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(content.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
    }

    [Test]
    [Arguments("/health")]
    [Arguments("/alive")]
    [Arguments("/")]
    [Arguments("/openapi/v1.json")]
    public async Task PublicEndpoint_AnonymousRequest_ReturnsOk(string path)
    {
        using var client = Factory.CreateClient();
        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task UnmappedChurchPath_AnonymousRequest_FallbackReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        using var response = await client.GetAsync("/api/church/anything");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Register_PasswordsDoNotMatch_ReturnsValidationProblemDetails()
    {
        using var client = Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "leader@example.com",
            password = "password",
            confirmPassword = "different"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");

        using var content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(content.RootElement.GetProperty("errors")
            .GetProperty(nameof(RegisterRequest.ConfirmPassword))[0].GetString())
            .IsEqualTo("Passwords do not match.");
    }
}
