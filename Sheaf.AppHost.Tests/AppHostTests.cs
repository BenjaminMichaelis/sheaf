using Aspire.Hosting.Testing;

using TUnit.Aspire;

namespace Sheaf.AppHost.Tests;

/// <summary>
/// Integration tests for the Sheaf AppHost.
/// These tests validate that the distributed application components start correctly
/// and respond to HTTP requests through the orchestrated host.
/// </summary>
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public class AppHostTests(AppHostFixture fixture)
{
    /// <summary>
    /// Verifies that the backend is reachable through Aspire orchestration.
    /// The liveness endpoint proves HTTP reachability without coupling the test to database readiness checks.
    /// </summary>
    [Test]
    public async Task BackendAliveEndpoint_Get_ReturnsSuccess()
    {
        using var client = fixture.CreateHttpClient("Sheaf-backend", "http");

        using var response = await client.GetAsync("/alive");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that the AppHost can resolve the database connection string.
    /// This validates that resource orchestration and connection string injection are configured correctly.
    /// </summary>
    [Test]
    public async Task GetConnectionStringAsync_DatabaseResource_ReturnsNonEmptyValue()
    {
        var connectionString = await fixture.App.GetConnectionStringAsync("Sheaf-db");
        await Assert.That(connectionString).IsNotNull().And.IsNotEmpty();
    }
}
