using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Services;

using Context = Sheaf.Data.Context;

namespace Sheaf.Api.Tests;

public class AttendanceTests : ChurchApiTestBase
{
    private const string TimeZone = "America/Chicago";

    /// <summary>A signed-in caller who leads one group with one other member.</summary>
    private sealed record GroupFixture(ChurchMembership Membership, HttpClient Client, string Cookie, Context Group, Person Leader, Person Member) : IDisposable
    {
        public Guid ChurchId => Membership.ChurchId;
        public void Dispose() => Client.Dispose();
    }

    private DateOnly Today => new ChurchCalendar(TimeZone).DateOf(Time.GetUtcNow());

    /// <summary>Seeds a group led by Lena. Unless <paramref name="callerLeads"/> is false, Lena is the signed-in caller.</summary>
    private async Task<GroupFixture> SeedGroupAsync(ChurchMembershipRole role = ChurchMembershipRole.Leader, bool callerLeads = true)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, role);
        var group = new Context { Name = "Sunday group", StartsAt = Time.GetUtcNow().AddDays(-200) };
        var leader = new Person { FirstName = "Lena", LastName = "Leader", UserId = callerLeads ? user.Id : null };
        var member = new Person { FirstName = "Max", LastName = "Member" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, leader, member);
            db.ContextLeaders.Add(new ContextLeader { ContextId = group.Id, PersonId = leader.Id, StartsAt = group.StartsAt });
            db.ContextMembers.Add(new ContextMember { ContextId = group.Id, PersonId = member.Id, JoinedAt = group.StartsAt });
            await db.SaveChangesAsync();
        });
        var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return new GroupFixture(membership, client, cookie, group, leader, member);
    }

    private static Task<HttpResponseMessage> SendAsync(GroupFixture f, HttpMethod method, string path, object? body = null, Guid? ifMatch = null) =>
        TrackerRequestAsync(f.Client, f.Cookie, f.ChurchId, method, path, body, ifMatch);

    private async Task<Person> AddPersonAsync(GroupFixture f, string firstName, string? lastName = null, string? email = null)
    {
        var person = new Person { FirstName = firstName, LastName = lastName, Email = email };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.People.Add(person);
            await db.SaveChangesAsync();
        });
        return person;
    }

    private Task<ContextSession> AddSessionAsync(GroupFixture f, DateOnly date, params Guid[] personIds) =>
        AddSessionAsync(f, date, Time.GetUtcNow(), personIds);

    private async Task<ContextSession> AddSessionAsync(GroupFixture f, DateOnly date, DateTimeOffset recordedAt, params Guid[] personIds)
    {
        var session = new ContextSession { ContextId = f.Group.Id, OccurredOn = date, RecordedAt = recordedAt };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.ContextSessions.Add(session);
            foreach (var personId in personIds)
                db.Attendances.Add(new Attendance { SessionId = session.Id, PersonId = personId, RecordedAt = session.RecordedAt });
            await db.SaveChangesAsync();
        });
        return session;
    }

    private static async Task<ProblemDetails> ReadConfirmationAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict).Because(await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>() ?? throw new InvalidOperationException("Missing problem.");
        await Assert.That(problem.Type).IsEqualTo(TrackerException.ConfirmationRequiredType);
        return problem;
    }

    private static async Task<JsonElement> ExtensionAsync(ProblemDetails problem, string name)
    {
        await Assert.That(problem.Extensions.TryGetValue(name, out var value)).IsTrue();
        return await Assert.That(value).IsTypeOf<JsonElement>();
    }

    [Test]
    public async Task RecordGroupSession_RosterAndNewPerson_RecordsAttendeesAbsentAndCreatesPerson()
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions",
            new SessionRequest(Today, [f.Leader.Id], [new PersonRequest("Zed", "Newcomer", Phone: "555-0100")]));

        var session = await TrackerReadAsync<SessionResponse>(response, HttpStatusCode.Created);
        await Assert.That(response.Headers.ETag?.Tag).IsEqualTo(EntityTag.Format(session.Version));
        await Assert.That(session.Attendees.Select(p => p.Name)).IsEquivalentTo(["Lena Leader", "Zed Newcomer"]);
        await Assert.That(session.Absent.Select(p => p.Id)).IsEquivalentTo([f.Member.Id]);
        await InChurchAsync(f.ChurchId, async db =>
        {
            var created = await db.People.SingleAsync(p => p.FirstName == "Zed");
            await Assert.That(created.CreatedByUserId).IsEqualTo(f.Membership.UserId);
            await Assert.That(created.Phone).IsEqualTo("555-0100");
            await Assert.That(await db.ContextMembers.AnyAsync(m => m.PersonId == created.Id)).IsFalse();
            await Assert.That(await db.Attendances.AllAsync(a => a.RecordedByUserId == f.Membership.UserId)).IsTrue();
        });
    }

    [Test]
    public async Task RecordGroupSession_SimilarNameOrSameDay_RequiresConfirmationAndSavesNothing()
    {
        using var f = await SeedGroupAsync();
        var existing = await AddPersonAsync(f, "Ada", "Lovelace", "ada@example.com");
        var path = $"/groups/{f.Group.Id}/sessions";

        using var similar = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, NewPeople: [new PersonRequest("Ada", "Lovelace")]));

        var problem = await ReadConfirmationAsync(similar);
        var matches = (await ExtensionAsync(problem, "similarPeople"))[0];
        await Assert.That(matches.GetProperty("index").GetInt32()).IsEqualTo(0);
        await Assert.That(matches.GetProperty("matches")[0].GetProperty("id").GetGuid()).IsEqualTo(existing.Id);
        await Assert.That(await similar.Content.ReadAsStringAsync()).DoesNotContain("ada@example.com");
        await InChurchAsync(f.ChurchId, async db =>
        {
            await Assert.That(await db.ContextSessions.CountAsync()).IsEqualTo(0);
            await Assert.That(await db.People.CountAsync(p => p.FirstName == "Ada")).IsEqualTo(1);
        });

        using var confirmed = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, NewPeople: [new PersonRequest("Ada", "Lovelace")], Confirm: true));
        var first = await TrackerReadAsync<SessionResponse>(confirmed, HttpStatusCode.Created);

        using var sameDay = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, [f.Member.Id]));
        var sameDayProblem = await ReadConfirmationAsync(sameDay);
        await Assert.That((await ExtensionAsync(sameDayProblem, "existingSessionIds"))[0].GetGuid()).IsEqualTo(first.Id);

        using var second = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, [f.Member.Id], Confirm: true));
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    [Test]
    [Arguments(1)]
    [Arguments(-250)]
    public async Task RecordGroupSession_FutureDateOrBeforeGroupStarted_ReturnsBadRequest(int daysFromToday)
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today.AddDays(daysFromToday), [f.Member.Id]));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments(ChurchMembershipRole.Leader, 60)]
    [Arguments(ChurchMembershipRole.Staff, 150)]
    public async Task RecordGroupSession_PastDateWhileGroupExisted_RecordsOutsideEditWindow(ChurchMembershipRole role, int daysAgo)
    {
        // Staff record for a group they do not lead. Both dates are past the Leader edit window but after the group started.
        using var f = await SeedGroupAsync(role, callerLeads: role == ChurchMembershipRole.Leader);

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today.AddDays(-daysAgo), [f.Member.Id]));

        var session = await TrackerReadAsync<SessionResponse>(response, HttpStatusCode.Created);
        await Assert.That(session.OccurredOn).IsEqualTo(Today.AddDays(-daysAgo));
    }

    [Test]
    public async Task RecordGroupSession_GroupLedBySomeoneElse_ReturnsNotFound()
    {
        using var f = await SeedGroupAsync();
        var otherGroup = new Context { Name = "Other group", StartsAt = f.Group.StartsAt };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.Add(otherGroup);
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{otherGroup.Id}/sessions", new SessionRequest(Today, [f.Member.Id]));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_RemoveVisitor_KeepsHistoryAndEndsProfileAccess()
    {
        using var f = await SeedGroupAsync();
        var visitor = await AddPersonAsync(f, "Vera", "Visitor", "vera@example.com");
        using var before = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");
        await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var recorded = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today, [visitor.Id, f.Member.Id]));
        var session = await TrackerReadAsync<SessionResponse>(recorded, HttpStatusCode.Created);
        using var attended = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");
        await Assert.That((await TrackerReadAsync<PersonResponse>(attended)).Email).IsEqualTo("vera@example.com");

        using var edited = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}", new AttendeesRequest([f.Member.Id]), session.Version);

        var updated = await TrackerReadAsync<SessionResponse>(edited);
        await Assert.That(updated.Attendees.Select(p => p.Id)).IsEquivalentTo([f.Member.Id]);
        await Assert.That(updated.Version).IsNotEqualTo(session.Version);
        using var after = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await InChurchAsync(f.ChurchId, async db =>
        {
            var history = await db.Attendances.SingleAsync(a => a.PersonId == visitor.Id);
            await Assert.That(history.RemovedAt).IsNotNull();
            await Assert.That(history.RemovedByUserId).IsEqualTo(f.Membership.UserId);
            await Assert.That(history.RecordedByUserId).IsEqualTo(f.Membership.UserId);
        });
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_StaleIfMatch_ReturnsPreconditionFailedWithoutChanges()
    {
        using var f = await SeedGroupAsync();
        var session = await AddSessionAsync(f, Today, f.Member.Id);

        using var response = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}", new AttendeesRequest([]), Guid.NewGuid());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await InChurchAsync(f.ChurchId, async db =>
            await Assert.That(await db.Attendances.CountAsync(a => a.RemovedAt == null)).IsEqualTo(1));
    }

    [Test]
    [Arguments(ChurchMembershipRole.Leader, 7, HttpStatusCode.OK)]
    [Arguments(ChurchMembershipRole.Leader, 8, HttpStatusCode.Forbidden)]
    [Arguments(ChurchMembershipRole.Staff, 30, HttpStatusCode.OK)]
    public async Task UpdateGroupSessionAttendees_DaysAfterRecording_AllowsLeadersOnlyWithinEditWindow(ChurchMembershipRole role, int daysAgo, HttpStatusCode expected)
    {
        // The meeting date is older than every window, so only when it was recorded decides. Staff edit a group they
        // do not lead, so the result shows their role, not leadership, allows it.
        using var f = await SeedGroupAsync(role, callerLeads: role == ChurchMembershipRole.Leader);
        var session = await AddSessionAsync(f, Today.AddDays(-60), Time.GetUtcNow().AddDays(-daysAgo), f.Member.Id);

        using var response = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}", new AttendeesRequest([f.Leader.Id]), session.Version);

        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
    }

    [Test]
    [Arguments(90, HttpStatusCode.OK)]
    [Arguments(91, HttpStatusCode.NotFound)]
    public async Task GetPerson_AttendedLeadersGroupDaysAgo_VisibleOnlyWithinNinetyDays(int daysAgo, HttpStatusCode expected)
    {
        using var f = await SeedGroupAsync();
        var visitor = await AddPersonAsync(f, "Vera", "Visitor");
        await AddSessionAsync(f, Today.AddDays(-daysAgo), visitor.Id);

        using var response = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    public async Task TrackerWrites_LeaderTargetsAttendedVisitor_RejectedWithoutChanges()
    {
        // Attending grants a Leader read access only: the visitor stays out of reach for writes.
        using var f = await SeedGroupAsync();
        var visitor = await AddPersonAsync(f, "Vera", "Visitor");
        await AddSessionAsync(f, Today, visitor.Id);
        using var read = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");
        var profile = await TrackerReadAsync<PersonResponse>(read);

        using var edit = await SendAsync(f, HttpMethod.Put, $"/people/{visitor.Id}", new PersonRequest("Vera", "Visitor", Email: "new@example.com"), profile.Version);
        using var assign = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/members", new AssignmentRequest(visitor.Id, Time.GetUtcNow()));
        using var household = await SendAsync(f, HttpMethod.Post, "/households", new HouseholdRequest([f.Member.Id, visitor.Id]));

        await Assert.That(edit.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(assign.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(household.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await InChurchAsync(f.ChurchId, async db =>
        {
            var stored = await db.People.SingleAsync(p => p.Id == visitor.Id);
            await Assert.That(stored.Email).IsNull();
            await Assert.That(stored.HouseholdId).IsNull();
            await Assert.That(await db.ContextMembers.AnyAsync(m => m.PersonId == visitor.Id)).IsFalse();
        });
    }

    [Test]
    public async Task GetGroupSession_MemberJoinedAfterMeeting_NotListedAbsent()
    {
        using var f = await SeedGroupAsync();
        var session = await AddSessionAsync(f, Today.AddDays(-14), f.Leader.Id);
        var late = await AddPersonAsync(f, "Lars", "Late");
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.ContextMembers.Add(new ContextMember { ContextId = f.Group.Id, PersonId = late.Id, JoinedAt = Time.GetUtcNow().AddDays(-3) });
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions/{session.Id}");

        var result = await TrackerReadAsync<SessionResponse>(response);
        await Assert.That(result.Attendees.Select(p => p.Id)).IsEquivalentTo([f.Leader.Id]);
        await Assert.That(result.Absent.Select(p => p.Id)).IsEquivalentTo([f.Member.Id]);
    }

    [Test]
    public async Task GetGroupSessions_SeveralMeetings_ListsNewestFirstWithCounts()
    {
        using var f = await SeedGroupAsync();
        await AddSessionAsync(f, Today.AddDays(-14), f.Member.Id);
        var newest = await AddSessionAsync(f, Today, f.Member.Id, f.Leader.Id);

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions");

        var page = await TrackerReadAsync<PagedResponse<SessionSummaryResponse>>(response);
        await Assert.That(page.Items.Select(s => (s.OccurredOn, s.AttendeeCount)))
            .IsEquivalentTo([(Today, 2), (Today.AddDays(-14), 1)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(page.Items[0].Id).IsEqualTo(newest.Id);
    }

    [Test]
    public async Task GetAttendanceRecords_StaffReview_IncludesRemovedRowsWithWhoRemovedThem()
    {
        using var f = await SeedGroupAsync(ChurchMembershipRole.Staff);
        using var recorded = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today, [f.Member.Id]));
        var session = await TrackerReadAsync<SessionResponse>(recorded, HttpStatusCode.Created);
        using var removed = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}", new AttendeesRequest([]), session.Version);
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var response = await SendAsync(f, HttpMethod.Get, $"/attendance-records?personId={f.Member.Id}");

        var record = (await TrackerReadAsync<PagedResponse<AttendanceRecordResponse>>(response)).Items.Single();
        await Assert.That(record.PersonName).IsEqualTo("Max Member");
        await Assert.That(record.RecordedByUserId).IsEqualTo(f.Membership.UserId);
        await Assert.That(record.RemovedByUserId).IsEqualTo(f.Membership.UserId);
        await Assert.That(record.RemovedAt).IsNotNull();
    }

    [Test]
    public async Task GetAttendanceRecords_OldRowRemovedToday_ListedBeforeRowsRecordedSince()
    {
        using var f = await SeedGroupAsync(ChurchMembershipRole.Staff);
        var old = await AddSessionAsync(f, Today.AddDays(-30), f.Member.Id);
        await InChurchAsync(f.ChurchId, async db =>
        {
            var row = await db.Attendances.SingleAsync(a => a.SessionId == old.Id);
            row.RecordedAt = Time.GetUtcNow().AddDays(-30);
            await db.SaveChangesAsync();
        });
        await AddSessionAsync(f, Today.AddDays(-1), f.Leader.Id);
        Time.Advance(TimeSpan.FromMinutes(1));
        using var removed = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{old.Id}", new AttendeesRequest([]), old.Version);
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var response = await SendAsync(f, HttpMethod.Get, "/attendance-records");

        var records = (await TrackerReadAsync<PagedResponse<AttendanceRecordResponse>>(response)).Items;
        await Assert.That(records.Select(r => r.PersonId)).IsEquivalentTo([f.Member.Id, f.Leader.Id], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
