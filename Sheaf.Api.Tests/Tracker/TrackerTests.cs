using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Authorization;
using Sheaf.Data;
using Sheaf.Services;


namespace Sheaf.Api.Tests;

public class TrackerTests : ChurchApiTestBase
{
    [Test]
    public async Task CreatePerson_PersistedRoleDemotedThenRevoked_DeniesBeforeCookieRevalidation()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var before = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Before demotion"));
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.Created);

        // The cookie still carries the Staff role claim; authorization must follow the stored role instead.
        await InChurchAsync(membership.ChurchId, async db =>
        {
            (await db.ChurchMemberships.SingleAsync(m => m.Id == membership.Id)).Role = ChurchMembershipRole.Leader;
            await db.SaveChangesAsync();
        });
        using var demoted = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Denied"));
        await Assert.That(demoted.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var leaderRead = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/group-types");
        await Assert.That(leaderRead.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync(m => m.Id == membership.Id));
            await db.SaveChangesAsync();
        });
        using var revoked = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/group-types");
        await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That(await db.People.Select(p => p.FirstName).ToArrayAsync()).IsEquivalentTo(["Before demotion"]));
    }

    [Test]
    public async Task GetAsync_MissingHttpOrMismatchedUserOrChurch_ReturnsNoActor()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var otherMembership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        await InScopeAsync(async services =>
        {
            var initializer = services.GetRequiredService<ITenantContextInitializer>();
            initializer.Initialize(membership.ChurchId, membership.Id);
            var accessor = services.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            try
            {
                var currentActor = services.GetRequiredService<ICurrentChurchActor>();
                accessor.HttpContext = null;
                await Assert.That(await currentActor.GetAsync(default)).IsNull();
                var http = CreateHttpContext(services);
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "fixture"));
                accessor.HttpContext = http;
                await Assert.That(await currentActor.GetAsync(default)).IsNull();
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)]));
                await Assert.That(await currentActor.GetAsync(default)).IsNull();
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "fixture"));
                initializer.Initialize(otherMembership.ChurchId, membership.Id);
                await Assert.That(await currentActor.GetAsync(default)).IsNull();
            }
            finally
            {
                accessor.HttpContext = previous;
            }
        });
    }

    [Test]
    [Arguments(ChurchMembershipRole.Staff, HttpStatusCode.Created)]
    [Arguments(ChurchMembershipRole.Admin, HttpStatusCode.Created)]
    [Arguments(ChurchMembershipRole.Leader, HttpStatusCode.Forbidden)]
    [Arguments(ChurchMembershipRole.Coordinator, HttpStatusCode.Forbidden)]
    public async Task CreatePerson_ByRole_AllowsOnlyPeopleManagers(ChurchMembershipRole role, HttpStatusCode expected)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, role);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Typed actor"));

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That(await db.People.CountAsync()).IsEqualTo(expected == HttpStatusCode.Created ? 1 : 0));
    }

    public static IEnumerable<(string Method, string Path)> ManageRoutes()
    {
        var id = Guid.NewGuid();
        yield return ("POST", "/people");
        yield return ("POST", "/group-types");
        yield return ("PUT", $"/group-types/{id}");
        yield return ("POST", "/groups");
        yield return ("PUT", $"/groups/{id}");
        yield return ("POST", $"/groups/{id}/end");
        yield return ("POST", $"/groups/{id}/leaders");
        yield return ("POST", $"/groups/{id}/leaders/{id}/end");
        yield return ("POST", $"/groups/{id}/assignments/{id}/role");
        yield return ("GET", "/attendance-records");
    }

    [Test]
    [MethodDataSource(nameof(ManageRoutes))]
    public async Task ManageRoute_LeaderCaller_ForbiddenAtEndpoint(string method, string path)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Leader);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        // The route ids do not exist, so a 403 rather than 404 shows the endpoint policy denied before any lookup.
        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, new HttpMethod(method), path, new { }, Guid.NewGuid());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreatePerson_OptionalFieldsRequiredFirstNameAndStaleChurch_EnforcesContract()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Ada"));
        var person = await TrackerReadAsync<PersonResponse>(created, HttpStatusCode.Created);
        await Assert.That(person.Name).IsEqualTo("Ada");
        await Assert.That(person.FirstName).IsEqualTo("Ada");
        await Assert.That(person.LastName).IsNull();
        await Assert.That(person.Email).IsNull();
        await Assert.That(person.HouseholdId).IsNull();
        await Assert.That(created.Headers.Location?.OriginalString).IsEqualTo($"/api/church/people/{person.Id}");
        using var invalid = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("   "));
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await invalid.Content.ReadFromJsonAsync<ProblemDetails>())?.Title).IsEqualTo("Invalid tracker input.");
        using var stale = await TrackerRequestAsync(client, cookie, Guid.NewGuid(), HttpMethod.Post, "/people", new PersonRequest("Blocked"));
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That(await db.People.Select(p => p.Id).ToArrayAsync()).IsEquivalentTo([person.Id]));
    }

    [Test]
    public async Task UpdatePerson_SeparateNames_NormalizesProfilesAndLookup()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("  Mary Jane  ", "  van Buren  "));
        var person = await TrackerReadAsync<PersonResponse>(created, HttpStatusCode.Created);
        await Assert.That(person.FirstName).IsEqualTo("Mary Jane");
        await Assert.That(person.LastName).IsEqualTo("van Buren");
        await Assert.That(person.Name).IsEqualTo("Mary Jane van Buren");
        using var lookup = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Jane%20van");
        await Assert.That((await TrackerReadAsync<PersonLookupResponse[]>(lookup)).Single().Name).IsEqualTo(person.Name);
        foreach (var input in new[] { new PersonRequest(" "), new PersonRequest(new string('a', 101)), new PersonRequest("Mary", new string('b', 101)) })
        {
            using var invalid = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", input, person.Version);
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using var unchanged = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/{person.Id}");
        await Assert.That(await TrackerReadAsync<PersonResponse>(unchanged)).IsEqualTo(person);
        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", new PersonRequest("  Mary Jane  ", " "), person.Version);
        var result = await TrackerReadAsync<PersonResponse>(updated);
        await Assert.That(result.FirstName).IsEqualTo("Mary Jane");
        await Assert.That(result.LastName).IsNull();
        await Assert.That(result.Name).IsEqualTo("Mary Jane");
    }

    [Test]
    [Arguments("Beta", "The Beta Family")]
    [Arguments(null, null)]
    public async Task CreateHousehold_OmittedJsonLabel_DefaultsFromFirstMemberAndPreservesOnUpdate(string? firstSurname, string? expectedLabel)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var earlier = new Person { FirstName = "Earlier", LastName = "Alpha" };
        var submittedFirst = new Person { FirstName = "Submitted first", LastName = firstSurname };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.People.AddRange(earlier, submittedFirst);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        var body = new { memberIds = new[] { submittedFirst.Id, earlier.Id } };
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", body);
        var household = await TrackerReadAsync<HouseholdResponse>(created, HttpStatusCode.Created);
        await Assert.That(household.Label).IsEqualTo(expectedLabel);
        await Assert.That(household.Members.Select(p => p.Id)).IsEquivalentTo([submittedFirst.Id, earlier.Id]);
        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}",
            new { memberIds = new[] { earlier.Id, submittedFirst.Id } }, household.Version);
        await Assert.That((await TrackerReadAsync<HouseholdResponse>(updated)).Label).IsEqualTo(expectedLabel ?? "The Alpha Family");
        using var fetched = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/households/{household.Id}");
        await Assert.That((await TrackerReadAsync<HouseholdResponse>(fetched)).Label).IsEqualTo(expectedLabel ?? "The Alpha Family");
    }

    [Test]
    [Arguments(null, "Beta", "The Beta Family")]
    [Arguments("   ", "Beta", "The Beta Family")]
    [Arguments("  Custom label  ", "Beta", "Custom label")]
    [Arguments(null, null, null)]
    public async Task CreateHousehold_FirstSubmittedMemberAndExplicitLabel_UsesAgreedDefault(string? label, string? firstSurname, string? expectedLabel)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var earlier = new Person { FirstName = "Earlier", LastName = "Alpha" };
        var submittedFirst = new Person { FirstName = "Submitted first", LastName = firstSurname };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.People.AddRange(earlier, submittedFirst);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", new HouseholdRequest([submittedFirst.Id, earlier.Id], label));
        var household = await TrackerReadAsync<HouseholdResponse>(created, HttpStatusCode.Created);
        await Assert.That(household.Label).IsEqualTo(expectedLabel);
        await Assert.That(household.Members.Select(p => p.FirstName)).IsEquivalentTo([earlier.FirstName, submittedFirst.FirstName]);
        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}", new HouseholdRequest([earlier.Id, submittedFirst.Id]), household.Version);
        var afterUpdate = await TrackerReadAsync<HouseholdResponse>(updated);
        await Assert.That(afterUpdate.Label).IsEqualTo(expectedLabel ?? "The Alpha Family");
        using var renamed = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}", new HouseholdRequest([earlier.Id, submittedFirst.Id], "Edited label"), afterUpdate.Version);
        var afterRename = await TrackerReadAsync<HouseholdResponse>(renamed);
        await Assert.That(afterRename.Label).IsEqualTo("Edited label");
        using var preserved = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}", new HouseholdRequest([submittedFirst.Id], " "), afterRename.Version);
        await Assert.That((await TrackerReadAsync<HouseholdResponse>(preserved)).Label).IsEqualTo("Edited label");
        using var lookup = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Submitted");
        var found = (await TrackerReadAsync<PersonLookupResponse[]>(lookup)).Single(p => p.Id == submittedFirst.Id);
        await Assert.That(found.HouseholdLabel).IsEqualTo("Edited label");
    }

    [Test]
    public async Task LookupPeople_PersonInHouseholdWithoutLabel_SaysTheyAreInAHousehold()
    {
        // A household whose first member has no last name gets no label, so the label alone can't say who is in one.
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Leader);
        var household = new Household();
        var housed = new Person { FirstName = "Quinn", HouseholdId = household.Id };
        var unhoused = new Person { FirstName = "Quincy" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(household, housed, unhoused);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var lookup = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Qu");

        var found = await TrackerReadAsync<PersonLookupResponse[]>(lookup);
        await Assert.That(found.Select(p => (p.Id, p.HouseholdLabel, p.InHousehold)))
            .IsEquivalentTo([(housed.Id, (string?)null, true), (unhoused.Id, (string?)null, false)]);
    }

    [Test]
    public async Task CreateHousehold_EmptyMemberList_LeavesDefaultUnset()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", new HouseholdRequest([]));
        var result = await TrackerReadAsync<HouseholdResponse>(created, HttpStatusCode.Created);
        await Assert.That(result.Label).IsNull();
        await Assert.That(result.Members).IsEmpty();
    }

    [Test]
    public async Task GetPerson_LinkedLeaderAndAssociatedHousehold_EnforcesProfileBoundary()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var household = new Household { Label = "Family" };
        var leader = new Person { FirstName = "Leader Full Name", UserId = user.Id };
        var member = new Person { FirstName = "Member", LastName = "Full Name", HouseholdId = household.Id };
        var outside = new Person { FirstName = "Outside", LastName = "Full Name", Email = "private@example.com", HouseholdId = household.Id };
        var inactive = new Person { FirstName = "Inactive", IsActive = false };
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-5) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(household, leader, member, outside, inactive, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leader.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = member.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var profile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/{member.Id}");
        await Assert.That((await TrackerReadAsync<PersonResponse>(profile)).Name).IsEqualTo(member.Name);
        using var denied = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{outside.Id}", new PersonRequest("Changed"), outside.Version);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await denied.Content.ReadFromJsonAsync<ProblemDetails>())?.Title).IsEqualTo("Resource not found.");
        using var updatedProfile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{member.Id}", new
        {
            firstName = "Member",
            lastName = "Updated",
            userId = user.Id
        }, member.Version);
        await Assert.That((await TrackerReadAsync<PersonResponse>(updatedProfile)).Name).IsEqualTo("Member Updated");
        using var unassociated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", new HouseholdRequest([outside.Id], "Not associated"));
        await Assert.That(unassociated.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await unassociated.Content.ReadFromJsonAsync<ProblemDetails>())?.Title).IsEqualTo("Access denied.");
        using var structure = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}", new HouseholdRequest([member.Id, outside.Id], "Renamed"), household.Version);
        var result = await TrackerReadAsync<HouseholdResponse>(structure);
        await Assert.That(result.Members.Select(p => p.Id)).IsEquivalentTo([member.Id, outside.Id]);
        await Assert.That(await structure.Content.ReadAsStringAsync()).DoesNotContain("private@example.com");
        using var lookup = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Full%20Name");
        var found = await TrackerReadAsync<PersonLookupResponse[]>(lookup);
        // A Leader finds any active person by name, including people outside their groups, but sees only name,
        // household label, never group names. The member was renamed above and the inactive person is excluded.
        await Assert.That(found.Select(p => p.Id)).IsEquivalentTo([leader.Id, outside.Id]);
        await Assert.That(found.Single(p => p.Id == outside.Id).HouseholdLabel).IsEqualTo("Renamed");
        await Assert.That(found.Where(p => p.Groups is not null)).IsEmpty();
        await Assert.That(await lookup.Content.ReadAsStringAsync()).DoesNotContain("private@example.com");
        using var ending = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/end", new EndRequest(Time.GetUtcNow()), group.Version);
        await Assert.That(ending.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            await Assert.That((await db.People.SingleAsync(p => p.Id == member.Id)).UserId).IsNull();
            await Assert.That((await db.People.SingleAsync(p => p.Id == outside.Id)).Email).IsEqualTo("private@example.com");
        });
    }

    [Test]
    public async Task GetGroup_UnlinkedOrCrossChurchLeader_FailsClosed()
    {
        var user = await CreateUserAsync();
        var first = await AddMembershipAsync(user);
        var second = await AddMembershipAsync(await CreateUserAsync(), ChurchMembershipRole.Staff);
        var group = new Group { Name = "Unlinked", StartsAt = Time.GetUtcNow().AddDays(-1) };
        var person = new Person { FirstName = "Leader" };
        await InChurchAsync(first.ChurchId, async db =>
        {
            db.AddRange(group, person);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        var other = new Group { Name = "Other church", StartsAt = group.StartsAt };
        var linked = new Person { FirstName = "Same account other church", UserId = user.Id };
        await InChurchAsync(second.ChurchId, async db =>
        {
            db.AddRange(other, linked);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = other.Id, PersonId = linked.Id, Role = GroupRole.Leader, StartsAt = other.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        foreach (var id in new[] { group.Id, other.Id })
        {
            using var denied = await TrackerRequestAsync(client, cookie, first.ChurchId, HttpMethod.Get, $"/groups/{id}");
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using var lookup = await TrackerRequestAsync(client, cookie, first.ChurchId, HttpMethod.Get, "/people/lookup?name=Same%20account");
        await Assert.That(await TrackerReadAsync<PersonLookupResponse[]>(lookup)).IsEmpty();
    }

    [Test]
    public async Task EndGroup_ConcurrentAssignmentsAndOptionalType_RetainsHistory()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var memberResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Member"));
        using var leaderResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Leader"));
        var people = new Dictionary<string, PersonResponse>
        {
            ["members"] = await TrackerReadAsync<PersonResponse>(memberResult, HttpStatusCode.Created),
            ["leaders"] = await TrackerReadAsync<PersonResponse>(leaderResult, HttpStatusCode.Created)
        };
        var start = Time.GetUtcNow().AddDays(-3);
        using var firstResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("First", start));
        using var secondResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("Second", start));
        var first = await TrackerReadAsync<GroupResponse>(firstResult, HttpStatusCode.Created);
        var second = await TrackerReadAsync<GroupResponse>(secondResult, HttpStatusCode.Created);
        await Assert.That(first.GroupTypeId).IsNull();
        foreach (var group in new[] { first, second })
        {
            foreach (var (segment, person) in people)
            {
                using var added = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, start));
                await TrackerReadAsync<AssignmentResponse>(added, HttpStatusCode.Created);
            }
        }
        // Adding assignments moved the group's version, so read the current one before ending it.
        using var current = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{first.Id}");
        var currentVersion = (await TrackerReadAsync<GroupResponse>(current)).Version;
        using var ended = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{first.Id}/end", new EndRequest(Time.GetUtcNow()), currentVersion);
        await Assert.That((await TrackerReadAsync<GroupResponse>(ended)).EndsAt).IsEqualTo(Time.GetUtcNow());
        foreach (var (segment, person) in people)
        {
            using var history = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{first.Id}/{segment}");
            var assignments = (await TrackerReadAsync<PagedResponse<AssignmentResponse>>(history)).Items;
            await Assert.That(assignments.Select(a => a.PersonId)).IsEquivalentTo([person.Id]);
            using var blocked = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{first.Id}/{segment}", new AssignmentRequest(person.Id, start));
            await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That((await blocked.Content.ReadFromJsonAsync<ProblemDetails>())?.Title).IsEqualTo("The resource changed or the group has ended.");
        }
        using var other = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{second.Id}");
        await Assert.That((await TrackerReadAsync<GroupResponse>(other)).EndsAt).IsNull();
    }

    [Test]
    public async Task CreateGroup_RequiredFieldsAndChurchLocalType_EnforcesContract()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var other = await AddMembershipAsync(await CreateUserAsync(), ChurchMembershipRole.Staff);
        var foreignType = new GroupType { Name = "Other church type" };
        await InChurchAsync(other.ChurchId, async db =>
        {
            db.GroupTypes.Add(foreignType);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        var start = Time.GetUtcNow().AddDays(-2);
        foreach (var input in new[] { new GroupRequest(" ", start), new GroupRequest("Group", default) })
        {
            using var invalid = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", input);
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using var foreign = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("Group", start, foreignType.Id));
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/group-types", new GroupTypeRequest("Small group"));
        var type = await TrackerReadAsync<GroupTypeResponse>(created, HttpStatusCode.Created);
        using var get = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/group-types/{type.Id}");
        await Assert.That(await TrackerReadAsync<GroupTypeResponse>(get)).IsEqualTo(type);
        using var renamed = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/group-types/{type.Id}", new GroupTypeRequest("Community"));
        await Assert.That((await TrackerReadAsync<GroupTypeResponse>(renamed)).Name).IsEqualTo("Community");
        using var groupResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("Group", start, type.Id));
        var group = await TrackerReadAsync<GroupResponse>(groupResult, HttpStatusCode.Created);
        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/groups/{group.Id}", new GroupRequest("Renamed", start), group.Version);
        await Assert.That((await TrackerReadAsync<GroupResponse>(updated)).GroupTypeId).IsNull();
    }

    [Test]
    public async Task UpdateGroupType_Retired_GroupsKeepTypeButCannotBeGivenIt()
    {
        using var staff = await SignInStaffAsync();
        var type = await CreateGroupTypeAsync(staff, "Freshmen");
        using var createdGroup = await staff.SendAsync(HttpMethod.Post, "/groups", new GroupRequest("Kept", staff.Start, type.Id));
        var kept = await TrackerReadAsync<GroupResponse>(createdGroup, HttpStatusCode.Created);
        using var createdOther = await staff.SendAsync(HttpMethod.Post, "/groups", new GroupRequest("Untyped", staff.Start));
        var untyped = await TrackerReadAsync<GroupResponse>(createdOther, HttpStatusCode.Created);

        using var retire = await staff.SendAsync(HttpMethod.Put, $"/group-types/{type.Id}", new GroupTypeRequest("Freshmen", IsRetired: true));
        await Assert.That((await TrackerReadAsync<GroupTypeResponse>(retire)).IsRetired).IsTrue();
        using var newGroup = await staff.SendAsync(HttpMethod.Post, "/groups", new GroupRequest("New", staff.Start, type.Id));
        using var giveType = await staff.SendAsync(HttpMethod.Put, $"/groups/{untyped.Id}", new GroupRequest("Untyped", staff.Start, type.Id), untyped.Version);
        using var keepType = await staff.SendAsync(HttpMethod.Put, $"/groups/{kept.Id}", new GroupRequest("Kept renamed", staff.Start, type.Id), kept.Version);

        await Assert.That(newGroup.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(giveType.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await TrackerReadAsync<GroupResponse>(keepType)).GroupTypeId).IsEqualTo(type.Id);
        using var groups = await staff.SendAsync(HttpMethod.Get, "/groups");
        var stored = await TrackerReadAsync<PagedResponse<GroupResponse>>(groups);
        await Assert.That(stored.Items.Select(g => g.Name)).IsEquivalentTo(["Kept renamed", "Untyped"]);
        await Assert.That(stored.Items.Single(g => g.Id == untyped.Id).GroupTypeId).IsNull();
    }

    [Test]
    public async Task UpdateGroupType_RenamedWithoutRetiredFlagThenUnretired_KeepsThenClearsRetirement()
    {
        using var staff = await SignInStaffAsync();
        var type = await CreateGroupTypeAsync(staff, "Freshmen");
        using var retire = await staff.SendAsync(HttpMethod.Put, $"/group-types/{type.Id}", new GroupTypeRequest("Freshmen", IsRetired: true));
        await Assert.That((await TrackerReadAsync<GroupTypeResponse>(retire)).IsRetired).IsTrue();

        using var rename = await staff.SendAsync(HttpMethod.Put, $"/group-types/{type.Id}", new GroupTypeRequest("Class of 2026"));
        await Assert.That((await TrackerReadAsync<GroupTypeResponse>(rename)).IsRetired).IsTrue();
        using var unretire = await staff.SendAsync(HttpMethod.Put, $"/group-types/{type.Id}", new GroupTypeRequest("Class of 2026", IsRetired: false));
        await Assert.That((await TrackerReadAsync<GroupTypeResponse>(unretire)).IsRetired).IsFalse();

        using var assigned = await staff.SendAsync(HttpMethod.Post, "/groups", new GroupRequest("New", staff.Start, type.Id));
        await Assert.That((await TrackerReadAsync<GroupResponse>(assigned, HttpStatusCode.Created)).GroupTypeId).IsEqualTo(type.Id);
    }

    /// <summary>A signed-in Staff member of a new church, with a past date groups can start on.</summary>
    private sealed record StaffSession(HttpClient Client, string Cookie, Guid ChurchId, DateTimeOffset Start) : IDisposable
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, Guid? ifMatch = null) =>
            TrackerRequestAsync(Client, Cookie, ChurchId, method, path, body, ifMatch);

        public void Dispose() => Client.Dispose();
    }

    private async Task<StaffSession> SignInStaffAsync()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return new StaffSession(client, cookie, membership.ChurchId, Time.GetUtcNow().AddDays(-2));
    }

    private static async Task<GroupTypeResponse> CreateGroupTypeAsync(StaffSession staff, string name)
    {
        using var created = await staff.SendAsync(HttpMethod.Post, "/group-types", new GroupTypeRequest(name));
        return await TrackerReadAsync<GroupTypeResponse>(created, HttpStatusCode.Created);
    }

    [Test]
    public async Task LookupAsync_StaffFindPersonInCurrentEndedAndLeftGroups_ReturnsOnlyCurrentGroupNames()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var now = Time.GetUtcNow();
        var person = new Person { FirstName = "Grace", LastName = "Hopper" };
        var current = new Group { Name = "Current group", StartsAt = now.AddDays(-30) };
        var leading = new Group { Name = "Another group", StartsAt = now.AddDays(-30) };
        // Ending a group leaves open assignments in place, so only the group's own end excludes it.
        var ended = new Group { Name = "Ended group", StartsAt = now.AddDays(-30), EndsAt = now.AddDays(-1) };
        var left = new Group { Name = "Left group", StartsAt = now.AddDays(-30) };
        var steppedDown = new Group { Name = "Stepped-down group", StartsAt = now.AddDays(-30) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, current, leading, ended, left, steppedDown);
            db.GroupAssignments.AddRange(
                new GroupAssignment { GroupId = current.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = current.StartsAt },
                new GroupAssignment { GroupId = left.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = left.StartsAt, EndsAt = now.AddDays(-2) });
            db.GroupAssignments.AddRange(
                new GroupAssignment { GroupId = leading.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = leading.StartsAt },
                new GroupAssignment { GroupId = ended.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = ended.StartsAt },
                new GroupAssignment { GroupId = steppedDown.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = steppedDown.StartsAt, EndsAt = now.AddDays(-2) });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Grace");

        var found = (await TrackerReadAsync<PersonLookupResponse[]>(response)).Single();
        var groups = await Assert.That(found.Groups).IsNotNull();
        await Assert.That(groups).IsEquivalentTo(["Another group", "Current group"]);
    }

    [Test]
    public async Task CreatePerson_SimilarNameUnconfirmedThenConfirmed_WarnsWithMatchGroupsThenCreates()
    {
        using var staff = await SignInStaffAsync();
        var existing = new Person { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" };
        var group = new Group { Name = "Engine group", StartsAt = staff.Start };
        await InChurchAsync(staff.ChurchId, async db =>
        {
            db.AddRange(existing, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = existing.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });

        using var warned = await staff.SendAsync(HttpMethod.Post, "/people", new PersonRequest("Ada", "Lovelace"));
        var match = (await ReadSimilarPeopleAsync(warned))[0].Matches[0];
        await Assert.That(match.Id).IsEqualTo(existing.Id);
        await Assert.That(await Assert.That(match.Groups).IsNotNull()).IsEquivalentTo([group.Name]);
        await Assert.That(await warned.Content.ReadAsStringAsync()).DoesNotContain("ada@example.com");
        await InChurchAsync(staff.ChurchId, async db => await Assert.That(await db.People.CountAsync()).IsEqualTo(1));

        using var confirmed = await staff.SendAsync(HttpMethod.Post, "/people?confirm=true", new PersonRequest("Ada", "Lovelace"));
        var created = await TrackerReadAsync<PersonResponse>(confirmed, HttpStatusCode.Created);
        await Assert.That(created.Id).IsNotEqualTo(existing.Id);
        await Assert.That(created.CanEdit).IsTrue();
    }

    [Test]
    public async Task AddAssignment_OverlappingIntervalsThenRejoining_RejectsOverlapAndKeepsEachInterval()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-4) };
        var people = new Dictionary<string, Person>
        {
            ["members"] = new() { FirstName = "Member" },
            ["leaders"] = new() { FirstName = "Leader" }
        };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange([group, .. people.Values]);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        foreach (var (segment, person) in people)
        {
            using var firstResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt));
            var first = await TrackerReadAsync<AssignmentResponse>(firstResult, HttpStatusCode.Created);
            using var get = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/{segment}/{first.Id}");
            await Assert.That(await TrackerReadAsync<AssignmentResponse>(get)).IsEqualTo(first);
            // Both segments refuse a second assignment while the person is already in the group in any role.
            foreach (var other in new[] { "members", "leaders" })
            {
                using var open = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{other}", new AssignmentRequest(person.Id, group.StartsAt.AddDays(1)));
                await Assert.That(open.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            }
            using var leave = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}/{first.Id}/end", new EndRequest(group.StartsAt.AddDays(2)));
            await Assert.That((await TrackerReadAsync<AssignmentResponse>(leave)).EndsAt).IsEqualTo(group.StartsAt.AddDays(2));
            using var backdated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt.AddDays(1)));
            await Assert.That(backdated.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            using var rejoin = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt.AddDays(3)));
            await TrackerReadAsync<AssignmentResponse>(rejoin, HttpStatusCode.Created);
            using var history = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/{segment}");
            var intervals = (await TrackerReadAsync<PagedResponse<AssignmentResponse>>(history)).Items;
            await Assert.That(intervals.Length).IsEqualTo(2);
            await Assert.That(intervals.Single(a => a.Id == first.Id).EndsAt).IsEqualTo(group.StartsAt.AddDays(2));
        }
    }

    [Test]
    public async Task ChangeRole_MemberPromotedThenSteppedDown_KeepsEachRoleInterval()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-10) };
        var person = new Person { FirstName = "Person" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, person);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        var promotedAt = group.StartsAt.AddDays(3);
        var steppedDownAt = group.StartsAt.AddDays(6);

        using var joined = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/members", new AssignmentRequest(person.Id, group.StartsAt));
        var member = await TrackerReadAsync<AssignmentResponse>(joined, HttpStatusCode.Created);
        using var promoted = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/assignments/{member.Id}/role", new RoleChangeRequest(GroupRole.Leader, promotedAt));
        var leader = await TrackerReadAsync<AssignmentResponse>(promoted);
        using var sameRole = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/assignments/{leader.Id}/role", new RoleChangeRequest(GroupRole.Leader, steppedDownAt));
        await Assert.That(sameRole.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var steppedDown = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/assignments/{leader.Id}/role", new RoleChangeRequest(GroupRole.Member, steppedDownAt));
        var memberAgain = await TrackerReadAsync<AssignmentResponse>(steppedDown);

        await Assert.That(leader).IsEqualTo(new AssignmentResponse(leader.Id, person.Id, GroupRole.Leader, promotedAt, null));
        await Assert.That(memberAgain).IsEqualTo(new AssignmentResponse(memberAgain.Id, person.Id, GroupRole.Member, steppedDownAt, null));
        using var members = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/members");
        await Assert.That((await TrackerReadAsync<PagedResponse<AssignmentResponse>>(members)).Items.Select(a => (a.StartsAt, a.EndsAt)))
            .IsEquivalentTo([(group.StartsAt, (DateTimeOffset?)promotedAt), (steppedDownAt, null)]);
        using var leaders = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/leaders");
        await Assert.That((await TrackerReadAsync<PagedResponse<AssignmentResponse>>(leaders)).Items.Select(a => (a.StartsAt, a.EndsAt)))
            .IsEquivalentTo([(promotedAt, (DateTimeOffset?)steppedDownAt)]);
    }

    [Test]
    public async Task ChangeRole_FromAssignmentStart_ChangesRoleWithoutNewInterval()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-2) };
        var person = new Person { FirstName = "Person" };
        var assignment = new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = group.StartsAt };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, person, assignment);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var changed = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/assignments/{assignment.Id}/role", new RoleChangeRequest(GroupRole.Leader, group.StartsAt));

        await Assert.That(await TrackerReadAsync<AssignmentResponse>(changed))
            .IsEqualTo(new AssignmentResponse(assignment.Id, person.Id, GroupRole.Leader, group.StartsAt, null));
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That(await db.GroupAssignments.CountAsync()).IsEqualTo(1));
    }

    [Test]
    public async Task GetGroup_CallerIsOnlyAMember_GrantsNoGroupAccess()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var person = new Person { FirstName = "Linked", UserId = user.Id };
        var other = new Person { FirstName = "Other" };
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-3) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, other, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = other.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var list = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/groups");
        await Assert.That((await TrackerReadAsync<PagedResponse<GroupResponse>>(list)).Items).IsEmpty();
        using var get = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var roster = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/members");
        await Assert.That(roster.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var record = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/sessions", new SessionRequest(DateOnly.FromDateTime(Time.GetUtcNow().UtcDateTime.AddDays(-1)), [other.Id]));
        await Assert.That(record.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var profile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/{other.Id}");
        await Assert.That(profile.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SaveChangesAsync_SecondOpenAssignmentForPersonInGroup_Throws()
    {
        var membership = await AddMembershipAsync(await CreateUserAsync());
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-3) };
        var person = new Person { FirstName = "Person" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, person);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    [Test]
    public async Task HasPermission_MemberRole_GrantsNothing()
    {
        foreach (var permission in Enum.GetValues<GroupPermission>())
        {
            await Assert.That(GroupRolePermissions.HasPermission(GroupRole.Member, permission)).IsFalse();
            await Assert.That(GroupRolePermissions.HasPermission(GroupRole.Leader, permission)).IsTrue();
        }
    }

    [Test]
    public async Task GetGroup_DatedLeadershipAndMembershipRevocation_FailsClosed()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var person = new Person { FirstName = "Linked", UserId = user.Id };
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-3) };
        var leadership = new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Leader, StartsAt = Time.GetUtcNow().AddDays(1) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, group, leadership);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var future = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await Assert.That(future.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            var assignment = await db.GroupAssignments.SingleAsync();
            assignment.StartsAt = group.StartsAt;
            await db.SaveChangesAsync();
        });
        using var active = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await TrackerReadAsync<GroupResponse>(active);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            (await db.GroupAssignments.SingleAsync()).EndsAt = Time.GetUtcNow();
            await db.SaveChangesAsync();
        });
        using var ended = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await Assert.That(ended.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            (await db.GroupAssignments.SingleAsync()).EndsAt = null;
            db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync());
            await db.SaveChangesAsync();
        });
        using var revoked = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SaveChangesAsync_AssignmentRacesGroupEnding_RollsBackStaleAssignment()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var group = new Group { Name = "Group", StartsAt = Time.GetUtcNow().AddDays(-3) };
        var person = new Person { FirstName = "Person" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, group);
            await db.SaveChangesAsync();
        });
        await InChurchAsync(membership.ChurchId, async staleDb =>
        {
            var staleGroup = await staleDb.Groups.SingleAsync();
            staleGroup.Version = Guid.NewGuid();
            staleDb.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = person.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await InChurchAsync(membership.ChurchId, async endingDb =>
            {
                var ending = await endingDb.Groups.SingleAsync();
                ending.EndsAt = Time.GetUtcNow();
                ending.Version = Guid.NewGuid();
                await endingDb.SaveChangesAsync();
            });
            await Assert.That(async () => await staleDb.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        });
        await InChurchAsync(membership.ChurchId, async db =>
        {
            await Assert.That(await db.GroupAssignments.ToArrayAsync()).IsEmpty();
            await Assert.That((await db.Groups.SingleAsync()).EndsAt).IsEqualTo(Time.GetUtcNow());
        });
    }

    [Test]
    public async Task SaveChangesAsync_NullablePersonAccountLink_EnforcesChurchLocalUniqueness()
    {
        var user = await CreateUserAsync();
        var first = await AddMembershipAsync(user);
        var second = await AddMembershipAsync(await CreateUserAsync());
        await InChurchAsync(first.ChurchId, async db =>
        {
            db.People.AddRange(new Person { FirstName = "One" }, new Person { FirstName = "Two" }, new Person { FirstName = "Linked", UserId = user.Id });
            await db.SaveChangesAsync();
        });
        await InChurchAsync(second.ChurchId, async db =>
        {
            db.People.Add(new Person { FirstName = "Other church", UserId = user.Id });
            await db.SaveChangesAsync();
        });
        await InChurchAsync(first.ChurchId, async db =>
        {
            db.People.Add(new Person { FirstName = "Duplicate link", UserId = user.Id });
            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    [Test]
    public async Task AddAssignmentAsync_LeaderAddsPersonOutsideTheirGroups_ReturnsNotFoundAndDoesNotAssign()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var leader = new Person { FirstName = "Leader", UserId = user.Id };
        var outside = new Person { FirstName = "Outside", Email = "private@example.com" };
        var group = new Group { Name = "Led", StartsAt = Time.GetUtcNow().AddDays(-5) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(leader, outside, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leader.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var add = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/members", new AssignmentRequest(outside.Id, group.StartsAt));
        await Assert.That(add.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var profile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/{outside.Id}");
        await Assert.That(profile.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateHousehold_LeaderIncludesAccessibleAndInaccessiblePeople_ReturnsForbidden()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var leader = new Person { FirstName = "Leader", UserId = user.Id };
        var member = new Person { FirstName = "Member" };
        var outside = new Person { FirstName = "Outside" };
        var group = new Group { Name = "Led", StartsAt = Time.GetUtcNow().AddDays(-5) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(leader, member, outside, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leader.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = member.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var create = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", new HouseholdRequest([member.Id, outside.Id]));
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That((await db.People.SingleAsync(p => p.Id == outside.Id)).HouseholdId).IsNull());
    }

    [Test]
    public async Task UpdatePerson_MissingOrStaleIfMatch_RejectsWithoutChangingProfile()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Original"));
        var person = await TrackerReadAsync<PersonResponse>(created, HttpStatusCode.Created);
        await Assert.That(created.Headers.ETag?.Tag).IsEqualTo(EntityTag.Format(person.Version));

        using var missing = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", new PersonRequest("No header"));
        await Assert.That(missing.StatusCode).IsEqualTo((HttpStatusCode)StatusCodes.Status428PreconditionRequired);
        using var stale = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", new PersonRequest("Stale"), Guid.NewGuid());
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);

        using var current = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", new PersonRequest("Current"), person.Version);
        var updated = await TrackerReadAsync<PersonResponse>(current);
        await Assert.That(updated.Name).IsEqualTo("Current");
        await Assert.That(updated.Version).IsNotEqualTo(person.Version);
        await Assert.That(current.Headers.ETag?.Tag).IsEqualTo(EntityTag.Format(updated.Version));
        using var replayed = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{person.Id}", new PersonRequest("Lost update"), person.Version);
        await Assert.That(replayed.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await InChurchAsync(membership.ChurchId, async db =>
            await Assert.That((await db.People.SingleAsync()).FirstName).IsEqualTo("Current"));
    }

    [Test]
    public async Task UpdateHousehold_MovesPersonFromAnotherHousehold_ChangesAffectedVersions()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var source = new Household { Label = "Source" };
        var target = new Household { Label = "Target" };
        var moving = new Person { FirstName = "Moving", HouseholdId = source.Id };
        var staying = new Person { FirstName = "Staying", HouseholdId = target.Id };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(source, target, moving, staying);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var moved = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{target.Id}", new HouseholdRequest([staying.Id, moving.Id]), target.Version);
        await TrackerReadAsync<HouseholdResponse>(moved);

        // The source household lost a member and the moved person's HouseholdId changed, so both earlier versions are stale.
        using var staleSource = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{source.Id}", new HouseholdRequest([]), source.Version);
        await Assert.That(staleSource.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        using var stalePerson = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{moving.Id}", new PersonRequest("Renamed"), moving.Version);
        await Assert.That(stalePerson.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        using var unaffected = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{staying.Id}", new PersonRequest("Still current"), staying.Version);
        await Assert.That(unaffected.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LookupAsync_TwoOrThreeCharacterTerms_MatchNameStartsOrAnywhere()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var bo = new Person { FirstName = "Bo" };
        var bob = new Person { FirstName = "Bob", LastName = "Smith" };
        var bolton = new Person { FirstName = "Jim", LastName = "Bolton" };
        var abbott = new Person { FirstName = "Abbott", LastName = "Ray" };
        var inactive = new Person { FirstName = "Bonnie", IsActive = false };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.People.AddRange(bo, bob, bolton, abbott, inactive);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var prefix = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Bo");
        using var anywhere = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=bbo");

        // Two characters match only the start of a first or last name, so "Bo" finds Bo, Bob, and Bolton but not Abbott.
        await Assert.That((await TrackerReadAsync<PersonLookupResponse[]>(prefix)).Select(p => p.Id)).IsEquivalentTo([bo.Id, bob.Id, bolton.Id]);
        await Assert.That((await TrackerReadAsync<PersonLookupResponse[]>(anywhere)).Select(p => p.Id)).IsEquivalentTo([abbott.Id]);
    }

    [Test]
    [Arguments("")]
    [Arguments("?name=")]
    [Arguments("?name=B")]
    [Arguments("?name=%20B%20")]
    public async Task LookupAsync_MissingOrOneCharacterTerm_ReturnsBadRequest(string query)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/lookup{query}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LookupAsync_MoreMatchesThanCap_ReturnsFirstResultsInNameOrder()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.People.AddRange(Enumerable.Range(0, PeopleService.LookupMaxResults + 5)
                .Select(i => new Person { FirstName = "Person", LastName = $"{i:D2}" }));
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Person");
        var found = await TrackerReadAsync<PersonLookupResponse[]>(response);

        await Assert.That(string.Join(",", found.Select(p => p.Name))).IsEqualTo(
            string.Join(",", Enumerable.Range(0, PeopleService.LookupMaxResults).Select(i => $"Person {i:D2}")));
    }

    [Test]
    public async Task LookupAsync_OverPerAccountLimit_ReturnsTooManyRequestsForThatAccountOnly()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var other = await CreateUserAsync();
        var otherMembership = await AddMembershipAsync(other);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        var (_, otherCookie) = await LoginAsync(client, other);

        for (var i = 0; i < 60; i++)
        {
            using var allowed = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Ada");
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using var limited = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup?name=Ada");
        using var otherAccount = await TrackerRequestAsync(client, otherCookie, otherMembership.ChurchId, HttpMethod.Get, "/people/lookup?name=Ada");

        await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(otherAccount.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetGroup_AuthorizedTrackerRequest_ReadsMembershipOnce()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var group = new Group { Name = "Counted", StartsAt = Time.GetUtcNow().AddDays(-1) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.Add(group);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        _membershipReads.Reset();

        using var response = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        // The endpoint permission check and the tracker's current actor share one read of the stored role.
        await Assert.That(_membershipReads.Count).IsEqualTo(1);
    }

    private readonly MembershipReadCounter _membershipReads = new();

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) => options.AddInterceptors(_membershipReads);

    private sealed class MembershipReadCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [ChurchMemberships]", StringComparison.Ordinal))
                Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task UpdatePerson_LeaderChangesActiveState_ReturnsForbiddenAndKeepsPerson()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var leader = new Person { FirstName = "Leader", UserId = user.Id };
        var member = new Person { FirstName = "Member" };
        var group = new Group { Name = "Led", StartsAt = Time.GetUtcNow().AddDays(-5) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(leader, member, group);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leader.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = member.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var deactivate = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{member.Id}",
            new PersonRequest("Member", IsActive: false), member.Version);
        await Assert.That(deactivate.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var unchanged = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{member.Id}",
            new PersonRequest("Member", Phone: "555-0100", IsActive: true), member.Version);
        var updated = await TrackerReadAsync<PersonResponse>(unchanged);

        await Assert.That(updated.Phone).IsEqualTo("555-0100");
        await Assert.That(updated.IsActive).IsTrue();
    }

    [Test]
    public async Task UpdatePerson_OmittedIsActive_KeepsInactivePersonInactive()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var inactive = new Person { FirstName = "Inactive", IsActive = false };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.People.Add(inactive);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);

        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{inactive.Id}",
            new { firstName = "Renamed" }, inactive.Version);

        await Assert.That((await TrackerReadAsync<PersonResponse>(updated)).IsActive).IsFalse();
    }

    [Test]
    public async Task SaveChangesAsync_BlankFirstNameWrittenDirectly_RejectedByCheckConstraint()
    {
        var membership = await AddMembershipAsync(await CreateUserAsync(), ChurchMembershipRole.Staff);

        await InChurchAsync(membership.ChurchId, async db =>
        {
            var constraint = await Assert.That(async () => await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO [People] ([ChurchId], [Id], [FirstName], [IsActive], [Version]) VALUES ({membership.ChurchId}, {Guid.NewGuid()}, N'   ', 1, {Guid.NewGuid()})"))
                .Throws<SqlException>();
            await Assert.That(constraint?.Number).IsEqualTo(547);
        });
    }
}
