using System.Net.Http.Json;

using Microsoft.Net.Http.Headers;

using Sheaf.Data;
using Sheaf.Integrations.PlanningCenter;

namespace Sheaf.Api.Tests.Integrations;

/// <summary>A Sheaf installation with no Planning Center application configured.</summary>
public class PlanningCenterUnconfiguredTests : ChurchApiTestBase
{
    private const string BasePath = "/api/church/integrations/planning-center";

    [Test]
    public async Task GetPlanningCenterStatus_Unconfigured_ReportsNotConfigured()
    {
        var (client, cookie, churchId) = await SignInAdminAsync();
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, cookie, churchId, BasePath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            PlanningCenterStatusResponse? status = await response.Content.ReadFromJsonAsync<PlanningCenterStatusResponse>(JsonOptions);
            await Assert.That(status?.Configured).IsFalse();
            await Assert.That(status?.Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_Unconfigured_ReturnsServiceUnavailable()
    {
        var (client, cookie, churchId) = await SignInAdminAsync();
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, cookie, churchId, $"{BasePath}/connect?churchId={churchId:D}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
    }

    private async Task<(HttpClient Client, string Cookie, Guid ChurchId)> SignInAdminAsync()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        HttpClient client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return (client, cookie, membership.ChurchId);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string cookie, Guid churchId, string path)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add(HeaderNames.Cookie, cookie);
        request.Headers.Add("X-Sheaf-Church", churchId.ToString("D"));
        return await client.SendAsync(request);
    }
}
