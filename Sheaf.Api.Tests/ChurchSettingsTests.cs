using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;
using Sheaf.Endpoints;

namespace Sheaf.Api.Tests;

public class ChurchSettingsTests : ChurchApiTestBase
{
    [Test]
    [Arguments(ChurchMembershipRole.Admin, HttpStatusCode.OK, "America/Denver")]
    [Arguments(ChurchMembershipRole.Staff, HttpStatusCode.Forbidden, "America/Chicago")]
    [Arguments(ChurchMembershipRole.Leader, HttpStatusCode.Forbidden, "America/Chicago")]
    public async Task UpdateChurchSettings_ByRole_OnlyAdminChangesTimeZone(ChurchMembershipRole role, HttpStatusCode expected, string storedZone)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, role);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings", new ChurchSettingsRequest("America/Denver"));

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            await Assert.That(await db.Churches.Where(c => c.Id == membership.ChurchId).Select(c => c.TimeZoneId).SingleAsync()).IsEqualTo(storedZone);
        });
    }

    [Test]
    [Arguments("Central Standard Time")]
    [Arguments("Not/AZone")]
    [Arguments(" ")]
    public async Task UpdateChurchSettings_NotAnIanaZone_ReturnsValidationProblem(string timeZoneId)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings", new ChurchSettingsRequest(timeZoneId));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problem?.Errors.ContainsKey(nameof(ChurchSettingsRequest.TimeZoneId))).IsTrue();
    }

    [Test]
    public async Task GetChurchSettings_Leader_ReturnsNameAndTimeZone()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/settings");

        var settings = await TrackerReadAsync<ChurchSettingsResponse>(response);
        await Assert.That(settings.TimeZoneId).IsEqualTo("America/Chicago");
        await Assert.That(settings.Name).IsEqualTo($"Church {membership.ChurchId}");
    }

    [Test]
    public async Task GetGroups_StoredTimeZoneUnresolvable_ReturnsProblemUntilAdminResetsZone()
    {
        // Stands in for a zone that resolved where it was saved but not on this host.
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        await InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            await db.Churches.Where(c => c.Id == membership.ChurchId).ExecuteUpdateAsync(c => c.SetProperty(x => x.TimeZoneId, "Gone/Zone"));
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var broken = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/groups");
        await Assert.That(broken.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        var problem = await broken.Content.ReadFromJsonAsync<ProblemDetails>();
        await Assert.That(problem?.Title).Contains("/api/church/settings");

        using var reset = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings", new ChurchSettingsRequest("America/Chicago"));
        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var fixedResponse = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/groups");
        await Assert.That(fixedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
