using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Services;

using Context = Sheaf.Data.Context;

namespace Sheaf.Api.Tests;

public partial class ChurchSessionTests
{
    private static async Task<HttpResponseMessage> TrackerRequestAsync(HttpClient client, string cookie, Guid churchId, HttpMethod method, string path, object? body = null)
    {
        using HttpRequestMessage request = new(method, $"/api/church{path}");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-Sheaf-Church", churchId.ToString("D"));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<T> TrackerReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("Missing tracker response.");
    }

    [Test]
    public async Task CreatePerson_OptionalFieldsRequiredNameAndStaleChurch_EnforcesContract()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var created = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Ada"));
        var person = await TrackerReadAsync<PersonResponse>(created, HttpStatusCode.Created);
        await Assert.That(person.Name).IsEqualTo("Ada");
        await Assert.That(person.Email).IsNull();
        await Assert.That(person.HouseholdId).IsNull();
        await Assert.That(created.Headers.Location?.OriginalString).IsEqualTo($"/api/church/people/{person.Id}");
        using var invalid = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("   "));
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var stale = await TrackerRequestAsync(client, cookie, Guid.NewGuid(), HttpMethod.Post, "/people", new PersonRequest("Blocked"));
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await InChurchAsync(membership.ChurchId, async db => await Assert.That(await db.People.CountAsync()).IsEqualTo(1));
    }

    [Test]
    public async Task GetPerson_LinkedLeaderAndAssociatedHousehold_EnforcesProfileBoundary()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var household = new Household { Label = "Family" };
        var leader = new Person { Name = "Leader Full Name", UserId = user.Id };
        var member = new Person { Name = "Member Full Name", GivenName = "Member", HouseholdId = household.Id };
        var outside = new Person { Name = "Outside Full Name", GivenName = "Outside", Email = "private@example.com", HouseholdId = household.Id };
        var inactive = new Person { Name = "Inactive", IsActive = false };
        var group = new Context { Name = "Group", StartsAt = _time.GetUtcNow().AddDays(-5) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(household, leader, member, outside, inactive, group);
            db.ContextLeaders.Add(new ContextLeader { ContextId = group.Id, PersonId = leader.Id, StartsAt = group.StartsAt });
            db.ContextMembers.Add(new ContextMember { ContextId = group.Id, PersonId = member.Id, JoinedAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var profile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/people/{member.Id}");
        await Assert.That((await TrackerReadAsync<PersonResponse>(profile)).Name).IsEqualTo(member.Name);
        using var denied = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{outside.Id}", new PersonRequest("Changed"));
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var updatedProfile = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/people/{member.Id}", new
        {
            name = "Updated member",
            givenName = "Member",
            userId = user.Id
        });
        await Assert.That((await TrackerReadAsync<PersonResponse>(updatedProfile)).Name).IsEqualTo("Updated member");
        using var unassociated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/households", new HouseholdRequest("Not associated", [outside.Id]));
        await Assert.That(unassociated.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var structure = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/households/{household.Id}", new HouseholdRequest("Renamed", [member.Id, outside.Id]));
        var result = await TrackerReadAsync<HouseholdResponse>(structure);
        await Assert.That(result.Members.Select(p => p.Id)).IsEquivalentTo([member.Id, outside.Id]);
        await Assert.That(await structure.Content.ReadAsStringAsync()).DoesNotContain("private@example.com");
        using var lookup = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, "/people/lookup");
        var found = await TrackerReadAsync<PersonLookupResponse[]>(lookup);
        await Assert.That(found.Any(p => p.Id == inactive.Id)).IsFalse();
        await Assert.That(found.Single(p => p.Id == member.Id).CurrentGroups).IsEquivalentTo([group.Name]);
        await Assert.That(found.Single(p => p.Id == outside.Id).HouseholdGivenNames).IsEquivalentTo(["Member", "Outside"]);
        await Assert.That(found.Single(p => p.Id == leader.Id).HouseholdGivenNames).IsEmpty();
        await Assert.That(await lookup.Content.ReadAsStringAsync()).DoesNotContain("private@example.com");
        using var ending = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/end", new EndRequest(_time.GetUtcNow()));
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
        var group = new Context { Name = "Unlinked", StartsAt = _time.GetUtcNow().AddDays(-1) };
        var person = new Person { Name = "Leader" };
        await InChurchAsync(first.ChurchId, async db =>
        {
            db.AddRange(group, person);
            db.ContextLeaders.Add(new ContextLeader { ContextId = group.Id, PersonId = person.Id, StartsAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        var other = new Context { Name = "Other church", StartsAt = group.StartsAt };
        var linked = new Person { Name = "Same account other church", UserId = user.Id };
        await InChurchAsync(second.ChurchId, async db =>
        {
            db.AddRange(other, linked);
            db.ContextLeaders.Add(new ContextLeader { ContextId = other.Id, PersonId = linked.Id, StartsAt = other.StartsAt });
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        foreach (var id in new[] { group.Id, other.Id })
        {
            using var denied = await TrackerRequestAsync(client, cookie, first.ChurchId, HttpMethod.Get, $"/groups/{id}");
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using var lookup = await TrackerRequestAsync(client, cookie, first.ChurchId, HttpMethod.Get, "/people/lookup");
        await Assert.That((await TrackerReadAsync<PersonLookupResponse[]>(lookup)).Select(p => p.Id)).IsEquivalentTo([person.Id]);
    }

    [Test]
    public async Task EndGroup_ConcurrentAssignmentsAndOptionalType_RetainsHistory()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        using var personResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/people", new PersonRequest("Person"));
        var person = await TrackerReadAsync<PersonResponse>(personResult, HttpStatusCode.Created);
        var start = _time.GetUtcNow().AddDays(-3);
        using var firstResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("First", start));
        using var secondResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, "/groups", new GroupRequest("Second", start));
        var first = await TrackerReadAsync<GroupResponse>(firstResult, HttpStatusCode.Created);
        var second = await TrackerReadAsync<GroupResponse>(secondResult, HttpStatusCode.Created);
        await Assert.That(first.GroupTypeId).IsNull();
        foreach (var group in new[] { first, second })
        {
            foreach (var segment in new[] { "members", "leaders" })
            {
                using var added = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, start));
                await TrackerReadAsync<AssignmentResponse>(added, HttpStatusCode.Created);
            }
        }
        using var ended = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{first.Id}/end", new EndRequest(_time.GetUtcNow()));
        await Assert.That((await TrackerReadAsync<GroupResponse>(ended)).EndsAt).IsEqualTo(_time.GetUtcNow());
        foreach (var segment in new[] { "members", "leaders" })
        {
            using var history = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{first.Id}/{segment}");
            var assignments = await TrackerReadAsync<AssignmentResponse[]>(history);
            await Assert.That(assignments.Select(a => a.PersonId)).IsEquivalentTo([person.Id]);
            using var blocked = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{first.Id}/{segment}", new AssignmentRequest(person.Id, start));
            await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
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
        var start = _time.GetUtcNow().AddDays(-2);
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
        using var updated = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Put, $"/groups/{group.Id}", new GroupRequest("Renamed", start));
        await Assert.That((await TrackerReadAsync<GroupResponse>(updated)).GroupTypeId).IsNull();
    }

    [Test]
    public async Task EndAssignment_OverlappingMembershipsAndRejoining_PreservesEachInterval()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        var person = new Person { Name = "Person" };
        var group = new Context { Name = "Group", StartsAt = _time.GetUtcNow().AddDays(-4) };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, group);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        foreach (var segment in new[] { "members", "leaders" })
        {
            using var firstResult = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt));
            var first = await TrackerReadAsync<AssignmentResponse>(firstResult, HttpStatusCode.Created);
            using var overlap = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt.AddDays(1)));
            var concurrent = await TrackerReadAsync<AssignmentResponse>(overlap, HttpStatusCode.Created);
            using var get = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/{segment}/{concurrent.Id}");
            await Assert.That(await TrackerReadAsync<AssignmentResponse>(get)).IsEqualTo(concurrent);
            using var leave = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}/{first.Id}/end", new EndRequest(group.StartsAt.AddDays(2)));
            await Assert.That((await TrackerReadAsync<AssignmentResponse>(leave)).EndsAt).IsEqualTo(group.StartsAt.AddDays(2));
            using var rejoin = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Post, $"/groups/{group.Id}/{segment}", new AssignmentRequest(person.Id, group.StartsAt.AddDays(3)));
            await TrackerReadAsync<AssignmentResponse>(rejoin, HttpStatusCode.Created);
            using var history = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}/{segment}");
            var intervals = await TrackerReadAsync<AssignmentResponse[]>(history);
            await Assert.That(intervals.Length).IsEqualTo(3);
            await Assert.That(intervals.Single(a => a.Id == first.Id).EndsAt).IsEqualTo(group.StartsAt.AddDays(2));
        }
    }

    [Test]
    public async Task GetGroup_DatedLeadershipAndMembershipRevocation_FailsClosed()
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user);
        var person = new Person { Name = "Linked", UserId = user.Id };
        var group = new Context { Name = "Group", StartsAt = _time.GetUtcNow().AddDays(-3) };
        var leadership = new ContextLeader { ContextId = group.Id, PersonId = person.Id, StartsAt = _time.GetUtcNow().AddDays(1) };
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
            var assignment = await db.ContextLeaders.SingleAsync();
            assignment.StartsAt = group.StartsAt;
            await db.SaveChangesAsync();
        });
        using var active = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await TrackerReadAsync<GroupResponse>(active);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            (await db.ContextLeaders.SingleAsync()).EndsAt = _time.GetUtcNow();
            await db.SaveChangesAsync();
        });
        using var ended = await TrackerRequestAsync(client, cookie, membership.ChurchId, HttpMethod.Get, $"/groups/{group.Id}");
        await Assert.That(ended.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            (await db.ContextLeaders.SingleAsync()).EndsAt = null;
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
        var group = new Context { Name = "Group", StartsAt = _time.GetUtcNow().AddDays(-3) };
        var person = new Person { Name = "Person" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(person, group);
            await db.SaveChangesAsync();
        });
        await InChurchAsync(membership.ChurchId, async staleDb =>
        {
            var staleGroup = await staleDb.Contexts.SingleAsync();
            staleGroup.Version = Guid.NewGuid();
            staleDb.ContextMembers.Add(new ContextMember { ContextId = group.Id, PersonId = person.Id, JoinedAt = group.StartsAt });
            await InChurchAsync(membership.ChurchId, async endingDb =>
            {
                var ending = await endingDb.Contexts.SingleAsync();
                ending.EndsAt = _time.GetUtcNow();
                ending.Version = Guid.NewGuid();
                await endingDb.SaveChangesAsync();
            });
            await Assert.That(async () => await staleDb.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        });
        await InChurchAsync(membership.ChurchId, async db =>
        {
            await Assert.That(await db.ContextMembers.CountAsync()).IsEqualTo(0);
            await Assert.That((await db.Contexts.SingleAsync()).EndsAt).IsEqualTo(_time.GetUtcNow());
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
            db.People.AddRange(new Person { Name = "One" }, new Person { Name = "Two" }, new Person { Name = "Linked", UserId = user.Id });
            await db.SaveChangesAsync();
        });
        await InChurchAsync(second.ChurchId, async db =>
        {
            db.People.Add(new Person { Name = "Other church", UserId = user.Id });
            await db.SaveChangesAsync();
        });
        await InChurchAsync(first.ChurchId, async db =>
        {
            db.People.Add(new Person { Name = "Duplicate link", UserId = user.Id });
            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }
}
