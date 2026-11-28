using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Services;


namespace Sheaf.Api.Tests;

public class AttendanceTests : ChurchApiTestBase
{
    private const string TimeZone = "America/Chicago";

    /// <summary>A signed-in caller who leads one group with one other member.</summary>
    private sealed record GroupFixture(ChurchMembership Membership, HttpClient Client, string Cookie, Group Group, Person Leader, Person Member) : IDisposable
    {
        public Guid ChurchId => Membership.ChurchId;
        public void Dispose() => Client.Dispose();
    }

    private static ChurchCalendar Calendar => new(TimeZone);

    private DateOnly Today => Calendar.DateOf(Time.GetUtcNow());

    /// <summary>Seeds a group led by Lena. Unless <paramref name="callerLeads"/> is false, Lena is the signed-in caller.</summary>
    private async Task<GroupFixture> SeedGroupAsync(ChurchMembershipRole role = ChurchMembershipRole.Leader, bool callerLeads = true)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, role);
        var group = new Group { Name = "Sunday group", StartsAt = Time.GetUtcNow().AddDays(-200) };
        var leader = new Person { FirstName = "Lena", LastName = "Leader", UserId = callerLeads ? user.Id : null };
        var member = new Person { FirstName = "Max", LastName = "Member" };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, leader, member);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = leader.Id, Role = GroupRole.Leader, StartsAt = group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = group.Id, PersonId = member.Id, Role = GroupRole.Member, StartsAt = group.StartsAt });
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

    private Task<GroupMeeting> AddSessionAsync(GroupFixture f, DateOnly date, params Guid[] personIds) =>
        AddSessionAsync(f, date, Time.GetUtcNow(), personIds);

    private async Task<GroupMeeting> AddSessionAsync(GroupFixture f, DateOnly date, DateTimeOffset recordedAt, params Guid[] personIds)
    {
        var session = new GroupMeeting { GroupId = f.Group.Id, OccurredOn = date, RecordedAt = recordedAt };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.GroupMeetings.Add(session);
            foreach (var personId in personIds)
                db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = personId, RecordedAt = session.RecordedAt });
            await db.SaveChangesAsync();
        });
        return session;
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
            await Assert.That(await db.GroupAssignments.AnyAsync(m => m.PersonId == created.Id)).IsFalse();
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
            await Assert.That(await db.GroupMeetings.CountAsync()).IsEqualTo(0);
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
        var otherGroup = new Group { Name = "Other group", StartsAt = f.Group.StartsAt };
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
    [Arguments(ChurchMembershipRole.Leader, 7, true)]
    [Arguments(ChurchMembershipRole.Leader, 8, false)]
    [Arguments(ChurchMembershipRole.Staff, 30, true)]
    public async Task GetGroupSession_DaysAfterRecording_CanEditMatchesWhetherACorrectionIsAccepted(ChurchMembershipRole role, int daysAgo, bool expected)
    {
        // The screen offers a correction only when CanEdit is true, so it must agree with what the API then does.
        using var f = await SeedGroupAsync(role, callerLeads: role == ChurchMembershipRole.Leader);
        var session = await AddSessionAsync(f, Today.AddDays(-60), Time.GetUtcNow().AddDays(-daysAgo), f.Member.Id);

        using var read = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions/{session.Id}");
        var meeting = await TrackerReadAsync<SessionResponse>(read);
        using var correct = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}", new AttendeesRequest([f.Leader.Id]), meeting.Version);

        await Assert.That(meeting.CanEdit).IsEqualTo(expected);
        await Assert.That(correct.StatusCode == HttpStatusCode.OK).IsEqualTo(meeting.CanEdit);
        if (correct.StatusCode == HttpStatusCode.OK)
            await Assert.That((await TrackerReadAsync<SessionResponse>(correct)).CanEdit).IsTrue();
    }

    [Test]
    public async Task RecordGroupSession_JustRecorded_CanCorrectIt()
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today, [f.Member.Id]));

        await Assert.That((await TrackerReadAsync<SessionResponse>(response, HttpStatusCode.Created)).CanEdit).IsTrue();
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
    public async Task GetPerson_LeaderReadsAttendedVisitor_ReturnsBasicProfileWithHouseholdFirstNamesAndCannotEdit()
    {
        using var f = await SeedGroupAsync();
        var household = new Household { Label = "Visitor family" };
        var visitor = new Person { FirstName = "Vera", LastName = "Visitor", Email = "vera@example.com", Phone = "555-0101", HouseholdId = household.Id };
        var spouse = new Person { FirstName = "Victor", LastName = "Visitor", Email = "victor@example.com", HouseholdId = household.Id };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.AddRange(household, visitor, spouse);
            await db.SaveChangesAsync();
        });
        await AddSessionAsync(f, Today, visitor.Id);

        using var visitorRead = await SendAsync(f, HttpMethod.Get, $"/people/{visitor.Id}");

        var profile = await TrackerReadAsync<PersonResponse>(visitorRead);
        await Assert.That(profile.Email).IsEqualTo("vera@example.com");
        await Assert.That(profile.Phone).IsEqualTo("555-0101");
        await Assert.That(profile.HouseholdLabel).IsEqualTo("Visitor family");
        await Assert.That(profile.HouseholdMembers).IsEquivalentTo([new HouseholdMemberResponse(spouse.Id, "Victor")]);
        await Assert.That(profile.CanEdit).IsFalse();
        // Household members appear by first name only, never with their own contacts.
        await Assert.That(await visitorRead.Content.ReadAsStringAsync()).DoesNotContain("victor@example.com");
    }

    [Test]
    public async Task GetPerson_LeaderReadsRosterMember_ReturnsCanEdit()
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Get, $"/people/{f.Member.Id}");

        await Assert.That((await TrackerReadAsync<PersonResponse>(response)).CanEdit).IsTrue();
    }

    [Test]
    public async Task GetPerson_StaffReadsPersonOutsideTheirGroupsWithoutHousehold_ReturnsCanEditAndNoHousehold()
    {
        using var f = await SeedGroupAsync(ChurchMembershipRole.Staff, callerLeads: false);

        using var response = await SendAsync(f, HttpMethod.Get, $"/people/{f.Member.Id}");

        var profile = await TrackerReadAsync<PersonResponse>(response);
        await Assert.That(profile.CanEdit).IsTrue();
        await Assert.That(profile.HouseholdLabel).IsNull();
        await Assert.That(profile.HouseholdMembers).IsEmpty();
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_DescriptionReplacedThenOmitted_ReplacesThenClearsDescription()
    {
        using var f = await SeedGroupAsync();
        var path = $"/groups/{f.Group.Id}/sessions";
        using var created = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, [f.Leader.Id], Description: "Prayed for exams"));
        var session = await TrackerReadAsync<SessionResponse>(created, HttpStatusCode.Created);
        using var read = await SendAsync(f, HttpMethod.Get, $"{path}/{session.Id}");
        await Assert.That((await TrackerReadAsync<SessionResponse>(read)).Description).IsEqualTo("Prayed for exams");

        using var replaced = await SendAsync(f, HttpMethod.Put, $"{path}/{session.Id}", new AttendeesRequest([f.Leader.Id], Description: "Shared a meal"), session.Version);
        var afterReplace = await TrackerReadAsync<SessionResponse>(replaced);
        await Assert.That(afterReplace.Description).IsEqualTo("Shared a meal");

        using var cleared = await SendAsync(f, HttpMethod.Put, $"{path}/{session.Id}", new AttendeesRequest([f.Leader.Id]), afterReplace.Version);
        await Assert.That((await TrackerReadAsync<SessionResponse>(cleared)).Description).IsNull();
    }

    [Test]
    [Arguments(2000, HttpStatusCode.Created, 1)]
    [Arguments(2001, HttpStatusCode.BadRequest, 0)]
    public async Task RecordGroupSession_DescriptionLength_SavesOnlyUpTo2000Characters(int length, HttpStatusCode expected, int savedSessions)
    {
        using var f = await SeedGroupAsync();

        var path = $"/groups/{f.Group.Id}/sessions";

        using var response = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, [f.Leader.Id], Description: new string('x', length)));

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        using var history = await SendAsync(f, HttpMethod.Get, path);
        await Assert.That((await TrackerReadAsync<PagedResponse<SessionSummaryResponse>>(history)).Items).Count().IsEqualTo(savedSessions);
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_DescriptionTooLong_ReturnsBadRequestAndKeepsSession()
    {
        using var f = await SeedGroupAsync();
        var path = $"/groups/{f.Group.Id}/sessions";
        using var created = await SendAsync(f, HttpMethod.Post, path, new SessionRequest(Today, [f.Leader.Id], Description: "Prayed for exams"));
        var session = await TrackerReadAsync<SessionResponse>(created, HttpStatusCode.Created);

        using var response = await SendAsync(f, HttpMethod.Put, $"{path}/{session.Id}",
            new AttendeesRequest([f.Leader.Id, f.Member.Id], Description: new string('x', 2001)), session.Version);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var read = await SendAsync(f, HttpMethod.Get, $"{path}/{session.Id}");
        var kept = await TrackerReadAsync<SessionResponse>(read);
        await Assert.That(kept.Description).IsEqualTo("Prayed for exams");
        await Assert.That(kept.Attendees.Select(a => a.Id)).IsEquivalentTo([f.Leader.Id]);
        await Assert.That(kept.Version).IsEqualTo(session.Version);
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
            await Assert.That(await db.GroupAssignments.AnyAsync(m => m.PersonId == visitor.Id)).IsFalse();
        });
    }

    [Test]
    [Arguments(6, HttpStatusCode.OK)]
    [Arguments(8, HttpStatusCode.NotFound)]
    public async Task UpdatePerson_LeaderCreatedPersonDaysAgo_EditableOnlyWithinCreatorWindow(int daysAgo, HttpStatusCode expected)
    {
        // Someone a Leader added off the roster, such as a newcomer added while recording attendance.
        using var f = await SeedGroupAsync();
        var added = new Person { FirstName = "Zedd", LastName = "Newcomer", CreatedByUserId = f.Membership.UserId, CreatedAt = Time.GetUtcNow().AddDays(-daysAgo) };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.People.Add(added);
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Put, $"/people/{added.Id}", new PersonRequest("Zed", "Newcomer"), added.Version);

        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
        await InChurchAsync(f.ChurchId, async db =>
            await Assert.That((await db.People.SingleAsync(p => p.Id == added.Id)).FirstName).IsEqualTo(expected == HttpStatusCode.OK ? "Zed" : "Zedd"));
    }

    [Test]
    public async Task RecordGroupSession_LeaderOfEndedGroup_ReturnsNotFound()
    {
        using var f = await SeedGroupAsync();
        await InChurchAsync(f.ChurchId, db => db.Groups.Where(g => g.Id == f.Group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EndsAt, Time.GetUtcNow().AddDays(-1))));

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today.AddDays(-3), [f.Member.Id]));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_PersonIdsOmitted_ReturnsBadRequestWithoutChanges()
    {
        using var f = await SeedGroupAsync();
        var session = await AddSessionAsync(f, Today, f.Member.Id);

        using var response = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}",
            new { newPeople = new[] { new PersonRequest("Zed", "Newcomer") } }, session.Version);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await InChurchAsync(f.ChurchId, async db =>
        {
            await Assert.That(await db.Attendances.CountAsync(a => a.RemovedAt == null)).IsEqualTo(1);
            await Assert.That(await db.People.AnyAsync(p => p.FirstName == "Zed")).IsFalse();
        });
    }

    [Test]
    public async Task UpdateGroupSessionAttendees_LeaderAddsNewPersonWithSimilarName_RequiresConfirmationWithoutGroupNames()
    {
        using var f = await SeedGroupAsync();
        var session = await AddSessionAsync(f, Today, f.Member.Id);

        using var response = await SendAsync(f, HttpMethod.Put, $"/groups/{f.Group.Id}/sessions/{session.Id}",
            new AttendeesRequest([f.Member.Id], [new PersonRequest("Max", "Member")]), session.Version);

        var match = (await ReadSimilarPeopleAsync(response))[0].Matches[0];
        await Assert.That(match.Id).IsEqualTo(f.Member.Id);
        // The Leader leads the match's group, yet group names go only to those who manage every group.
        await Assert.That(match.Groups).IsNull();
        await InChurchAsync(f.ChurchId, async db =>
            await Assert.That(await db.People.CountAsync(p => p.FirstName == "Max")).IsEqualTo(1));
    }

    [Test]
    public async Task RecordGroupSession_SeveralNewPeople_ReportsMatchesForEachSimilarName()
    {
        using var f = await SeedGroupAsync();
        var ada = await AddPersonAsync(f, "Ada", "Lovelace");

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(Today,
            NewPeople: [new PersonRequest("Max", "Member"), new PersonRequest("Quinn", "Unique"), new PersonRequest("Ada", "Lovelace")]));

        var similar = await ExtensionAsync(await ReadConfirmationAsync(response), "similarPeople");
        var byIndex = similar.EnumerateArray().ToDictionary(s => s.GetProperty("index").GetInt32(),
            s => s.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToArray());
        await Assert.That(byIndex.Keys).IsEquivalentTo([0, 2]);
        await Assert.That(byIndex[0]).IsEquivalentTo([f.Member.Id]);
        await Assert.That(byIndex[2]).IsEquivalentTo([ada.Id]);
    }

    [Test]
    [Arguments("too many people")]
    [Arguments("too many new people")]
    [Arguments("inactive person")]
    [Arguments("inactive new person")]
    public async Task RecordGroupSession_InvalidAttendees_ReturnsBadRequestWithoutChanges(string invalid)
    {
        using var f = await SeedGroupAsync();
        var inactive = await AddPersonAsync(f, "Ida", "Inactive");
        await InChurchAsync(f.ChurchId, db => db.People.Where(p => p.Id == inactive.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false)));
        var request = invalid switch
        {
            "too many people" => new SessionRequest(Today, [.. Enumerable.Range(0, AttendanceService.MaxAttendees + 1).Select(_ => Guid.NewGuid())]),
            "too many new people" => new SessionRequest(Today, NewPeople: [.. Enumerable.Range(0, AttendanceService.MaxNewPeople + 1).Select(i => new PersonRequest($"Person{i}"))]),
            "inactive person" => new SessionRequest(Today, [inactive.Id]),
            _ => new SessionRequest(Today, NewPeople: [new PersonRequest("Zed", IsActive: false)]),
        };

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", request with { Confirm = true });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await InChurchAsync(f.ChurchId, async db =>
        {
            await Assert.That(await db.GroupMeetings.CountAsync()).IsEqualTo(0);
            await Assert.That(await db.People.CountAsync()).IsEqualTo(3);
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
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = late.Id, Role = GroupRole.Member, StartsAt = Time.GetUtcNow().AddDays(-3) });
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions/{session.Id}");

        var result = await TrackerReadAsync<SessionResponse>(response);
        await Assert.That(result.Attendees.Select(p => p.Id)).IsEquivalentTo([f.Leader.Id]);
        await Assert.That(result.Absent.Select(p => p.Id)).IsEquivalentTo([f.Member.Id]);
    }

    [Test]
    public async Task GetGroupRoster_OnDate_ListsActivePeopleAssignedThatDay()
    {
        using var f = await SeedGroupAsync();
        var late = await AddPersonAsync(f, "Lars", "Late");
        var left = await AddPersonAsync(f, "Lea", "Left");
        var inactive = await AddPersonAsync(f, "Ida", "Inactive");
        var elsewhere = await AddPersonAsync(f, "Otto", "Other");
        var otherGroup = new Group { Name = "Other group", StartsAt = f.Group.StartsAt };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.Add(otherGroup);
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = late.Id, Role = GroupRole.Member, StartsAt = Time.GetUtcNow().AddDays(-3) });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = left.Id, Role = GroupRole.Member, StartsAt = f.Group.StartsAt, EndsAt = Time.GetUtcNow().AddDays(-7) });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = inactive.Id, Role = GroupRole.Member, StartsAt = f.Group.StartsAt });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = otherGroup.Id, PersonId = elsewhere.Id, Role = GroupRole.Member, StartsAt = f.Group.StartsAt });
            await db.SaveChangesAsync();
            await db.People.Where(p => p.Id == inactive.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        });

        using var twoWeeksAgo = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={Today.AddDays(-14):yyyy-MM-dd}");
        using var today = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={Today:yyyy-MM-dd}");

        await Assert.That((await TrackerReadAsync<SessionPersonResponse[]>(twoWeeksAgo)).Select(p => p.Id)).IsEquivalentTo([left.Id, f.Leader.Id, f.Member.Id]);
        await Assert.That((await TrackerReadAsync<SessionPersonResponse[]>(today)).Select(p => p.Id)).IsEquivalentTo([late.Id, f.Leader.Id, f.Member.Id]);
    }

    [Test]
    public async Task GetGroupRoster_AssignmentsAtChurchDayEdges_ListsOnlyThoseOnTheRosterDuringThatLocalDay()
    {
        using var f = await SeedGroupAsync();
        var date = Today.AddDays(-14);
        var (start, end) = Calendar.DayBounds(date);
        var joinedLateEvening = await AddPersonAsync(f, "Eve", "Evening");
        var leftDuringDay = await AddPersonAsync(f, "Dee", "Daytime");
        var leftAtMidnightBefore = await AddPersonAsync(f, "Mona", "Midnight");
        var joinedNextMidnight = await AddPersonAsync(f, "Nick", "Next");
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = joinedLateEvening.Id, Role = GroupRole.Member, StartsAt = end.AddHours(-1) });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = leftDuringDay.Id, Role = GroupRole.Member, StartsAt = f.Group.StartsAt, EndsAt = start.AddHours(1) });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = leftAtMidnightBefore.Id, Role = GroupRole.Member, StartsAt = f.Group.StartsAt, EndsAt = start });
            db.GroupAssignments.Add(new GroupAssignment { GroupId = f.Group.Id, PersonId = joinedNextMidnight.Id, Role = GroupRole.Member, StartsAt = end });
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={date:yyyy-MM-dd}");

        await Assert.That((await TrackerReadAsync<SessionPersonResponse[]>(response)).Select(p => p.Id))
            .IsEquivalentTo([f.Leader.Id, f.Member.Id, joinedLateEvening.Id, leftDuringDay.Id]);
    }

    [Test]
    public async Task GetGroupRoster_RosterMemberWithContacts_ReturnsOnlyIdAndName()
    {
        using var f = await SeedGroupAsync();
        await InChurchAsync(f.ChurchId, db => db.People.Where(p => p.Id == f.Member.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Email, "max@example.com").SetProperty(p => p.Phone, "555-0100")));

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={Today:yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetGuid())).Contains(f.Member.Id);
        foreach (var person in json.RootElement.EnumerateArray())
            await Assert.That(person.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(["id", "name"]);
    }

    [Test]
    public async Task GetGroupRoster_GroupCallerDoesNotLead_ReturnsNotFound()
    {
        using var f = await SeedGroupAsync();
        var otherGroup = new Group { Name = "Other group", StartsAt = f.Group.StartsAt };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.Add(otherGroup);
            await db.SaveChangesAsync();
        });

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{otherGroup.Id}/roster?occurredOn={Today:yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments("")]
    [Arguments("?occurredOn=2026-13-01")]
    [Arguments("?occurredOn=9999-12-31")]
    public async Task GetGroupRoster_MissingMalformedOrLastPossibleDate_ReturnsBadRequest(string query)
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster{query}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>The group started 200 days ago and ended at local midnight 10 days ago, so its last day was 11 days ago.</summary>
    [Test]
    [Arguments("dayBeforeStart", HttpStatusCode.BadRequest)]
    [Arguments("startDay", HttpStatusCode.OK)]
    [Arguments("lastDay", HttpStatusCode.OK)]
    [Arguments("endDay", HttpStatusCode.BadRequest)]
    [Arguments("tomorrow", HttpStatusCode.BadRequest)]
    public async Task GetGroupRoster_DateAroundGroupLife_AcceptsOnlyDaysTheGroupExisted(string day, HttpStatusCode expected)
    {
        using var f = await SeedGroupAsync(ChurchMembershipRole.Staff);
        var endDay = Today.AddDays(-10);
        await InChurchAsync(f.ChurchId, db => db.Groups.Where(g => g.Id == f.Group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EndsAt, Calendar.StartOf(endDay))));
        var startDay = Calendar.DateOf(f.Group.StartsAt);
        var date = day switch
        {
            "dayBeforeStart" => startDay.AddDays(-1),
            "startDay" => startDay,
            "lastDay" => endDay.AddDays(-1),
            "endDay" => endDay,
            _ => Today.AddDays(1),
        };

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={date:yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ChurchMembershipRole.Staff, HttpStatusCode.OK)]
    [Arguments(ChurchMembershipRole.Leader, HttpStatusCode.NotFound)]
    public async Task GetGroupRoster_GroupCallerLedHasEnded_OnlyStaffCanRead(ChurchMembershipRole role, HttpStatusCode expected)
    {
        using var f = await SeedGroupAsync(role);
        await InChurchAsync(f.ChurchId, db => db.Groups.Where(g => g.Id == f.Group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.EndsAt, Time.GetUtcNow().AddDays(-1))));

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={Today.AddDays(-7):yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    public async Task GetGroupRoster_LeaderSteppedDown_ReturnsNotFound()
    {
        using var f = await SeedGroupAsync();
        await InChurchAsync(f.ChurchId, db => db.GroupAssignments.Where(a => a.PersonId == f.Leader.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAt, Time.GetUtcNow().AddDays(-1))));

        using var response = await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/roster?occurredOn={Today.AddDays(-7):yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task RecordGroupSession_LastPossibleDate_ReturnsBadRequest()
    {
        using var f = await SeedGroupAsync();

        using var response = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/sessions", new SessionRequest(DateOnly.MaxValue, [f.Member.Id]));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
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
            var row = await db.Attendances.SingleAsync(a => a.MeetingId == old.Id);
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
