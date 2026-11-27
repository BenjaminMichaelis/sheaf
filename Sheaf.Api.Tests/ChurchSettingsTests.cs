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
    public async Task UpdateChurchSettings_PaddedGroupLabelsGivenThenOmitted_UsesTrimmedChurchWordsThenDefaults()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var labelled = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings",
            new ChurchSettingsRequest("America/Chicago", "  Village  ", " Villages "));
        var settings = await TrackerReadAsync<ChurchSettingsResponse>(labelled);
        await Assert.That(settings.GroupLabel).IsEqualTo("Village");
        await Assert.That(settings.GroupLabelPlural).IsEqualTo("Villages");
        using var read = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/settings");
        await Assert.That((await TrackerReadAsync<ChurchSettingsResponse>(read)).GroupLabel).IsEqualTo("Village");

        using var reset = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings", new ChurchSettingsRequest("America/Chicago"));
        var defaults = await TrackerReadAsync<ChurchSettingsResponse>(reset);
        await Assert.That(defaults.GroupLabel).IsEqualTo("Group");
        await Assert.That(defaults.GroupLabelPlural).IsEqualTo("Groups");
    }

    [Test]
    [Arguments(50, "", HttpStatusCode.OK)]
    [Arguments(50, "   ", HttpStatusCode.OK)]
    [Arguments(51, "", HttpStatusCode.BadRequest)]
    public async Task UpdateChurchSettings_GroupLabelLength_AcceptsUpTo50CharactersAfterTrimming(int length, string padding, HttpStatusCode expected)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        var label = padding + new string('v', length) + padding;

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings",
            new ChurchSettingsRequest("America/Chicago", label, label));

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Cell", null, nameof(ChurchSettingsRequest.GroupLabelPlural))]
    [Arguments("Cell", "   ", nameof(ChurchSettingsRequest.GroupLabelPlural))]
    [Arguments(null, "Cells", nameof(ChurchSettingsRequest.GroupLabel))]
    [Arguments("Cell", "Cells whose plural label is far too long to fit in fifty characters", nameof(ChurchSettingsRequest.GroupLabelPlural))]
    public async Task UpdateChurchSettings_OneLabelMissingOrTooLong_ReturnsProblemForThatLabelAndKeepsSettings(string? label, string? plural, string invalidField)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var saved = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings",
            new ChurchSettingsRequest("America/Chicago", "Village", "Villages"));
        var before = await TrackerReadAsync<ChurchSettingsResponse>(saved);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, "/settings",
            new ChurchSettingsRequest("America/Denver", label, plural));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problem?.Errors.Keys).IsEquivalentTo([invalidField]);
        using var read = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/settings");
        await Assert.That(await TrackerReadAsync<ChurchSettingsResponse>(read)).IsEqualTo(before);
    }

    [Test]
    public async Task GetChurchSettings_Leader_ReturnsNameTimeZoneAndDefaultLabels()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/settings");

        var settings = await TrackerReadAsync<ChurchSettingsResponse>(response);
        await Assert.That(settings.TimeZoneId).IsEqualTo("America/Chicago");
        await Assert.That(settings.Name).IsEqualTo($"Church {membership.ChurchId}");
        await Assert.That(settings.GroupLabel).IsEqualTo("Group");
        await Assert.That(settings.GroupLabelPlural).IsEqualTo("Groups");
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
