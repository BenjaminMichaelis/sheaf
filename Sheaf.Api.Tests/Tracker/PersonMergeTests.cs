using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Services;

namespace Sheaf.Api.Tests;

public class PersonMergeTests : ChurchApiTestBase
{
    /// <summary>A signed-in caller with a church role, and one group with one meeting.</summary>
    private sealed record Fixture(ChurchMembership Membership, HttpClient Client, string Cookie, Group Group, GroupMeeting Meeting) : IDisposable
    {
        public Guid ChurchId => Membership.ChurchId;
        public void Dispose() => Client.Dispose();
    }

    private async Task<Fixture> SeedAsync(ChurchMembershipRole role = ChurchMembershipRole.Staff)
    {
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(user, role);
        var group = new Group { Name = "Sunday group", StartsAt = Time.GetUtcNow().AddDays(-400) };
        var meeting = new GroupMeeting
        {
            GroupId = group.Id,
            OccurredOn = DateOnly.FromDateTime(Time.GetUtcNow().UtcDateTime).AddDays(-7),
            RecordedAt = Time.GetUtcNow().AddDays(-7)
        };
        await InChurchAsync(membership.ChurchId, async db =>
        {
            db.AddRange(group, meeting);
            await db.SaveChangesAsync();
        });
        var client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return new Fixture(membership, client, cookie, group, meeting);
    }

    private static Task<HttpResponseMessage> SendAsync(Fixture f, HttpMethod method, string path, object? body = null, Guid? ifMatch = null) =>
        TrackerRequestAsync(f.Client, f.Cookie, f.ChurchId, method, path, body, ifMatch);

    private async Task<Person> AddPersonAsync(Fixture f, string firstName, string? lastName = null, string? email = null, string? phone = null,
        Guid? householdId = null, string? userId = null, int daysAgo = 0)
    {
        var person = new Person
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = phone,
            HouseholdId = householdId,
            UserId = userId,
            CreatedAt = Time.GetUtcNow().AddDays(-daysAgo)
        };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.People.Add(person);
            await db.SaveChangesAsync();
        });
        return person;
    }

    private async Task<Household> AddHouseholdAsync(Fixture f, string label)
    {
        var household = new Household { Label = label };
        await InChurchAsync(f.ChurchId, async db =>
        {
            db.Households.Add(household);
            await db.SaveChangesAsync();
        });
        return household;
    }

    private Task SeedAsync(Fixture f, params object[] rows) =>
        InChurchAsync(f.ChurchId, async db =>
        {
            db.AddRange(rows);
            await db.SaveChangesAsync();
        });

    private static Task<HttpResponseMessage> MergeAsync(Fixture f, Person survivor, Person duplicate, PersonMergeFields? fields = null) =>
        SendAsync(f, HttpMethod.Post, $"/people/{survivor.Id}/merge", new PersonMergeRequest(duplicate.Id, duplicate.Version, fields), survivor.Version);

    private static async Task<PersonResponse> GetPersonAsync(Fixture f, Guid id) =>
        await TrackerReadAsync<PersonResponse>(await SendAsync(f, HttpMethod.Get, $"/people/{id}"));

    private static async Task<DuplicateCandidateResponse[]> GetCandidatesAsync(Fixture f) =>
        (await TrackerReadAsync<PagedResponse<DuplicateCandidateResponse>>(await SendAsync(f, HttpMethod.Get, "/people/duplicates?limit=200"))).Items;

    [Test]
    [Arguments(ChurchMembershipRole.Leader)]
    [Arguments(ChurchMembershipRole.Coordinator)]
    public async Task MergePerson_WithoutPeopleManage_ReturnsForbiddenAndChangesNothing(ChurchMembershipRole role)
    {
        using var f = await SeedAsync(role);
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", email: "ann@example.com");

        var response = await MergeAsync(f, survivor, duplicate);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await InChurchAsync(f.ChurchId, async db =>
            await Assert.That(await db.People.AnyAsync(p => p.MergedIntoId != null)).IsFalse());
    }

    [Test]
    public async Task MergePerson_FieldsOmitted_KeepsSurvivorValuesAndFillsBlanksFromDuplicate()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee", email: "ann@example.com");
        var duplicate = await AddPersonAsync(f, "Annie", "Leigh", email: "annie@example.com", phone: "555-0100");

        var merged = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        await Assert.That(merged.Id).IsEqualTo(survivor.Id);
        await Assert.That(merged.FirstName).IsEqualTo("Ann");
        await Assert.That(merged.LastName).IsEqualTo("Lee");
        await Assert.That(merged.Email).IsEqualTo("ann@example.com");
        await Assert.That(merged.Phone).IsEqualTo("555-0100");
        await Assert.That(merged.MergedIntoId).IsNull();
        await Assert.That(merged.Version).IsNotEqualTo(survivor.Version);
    }

    [Test]
    public async Task MergePerson_FieldChoices_TakesTheChosenSideEvenWhenBlank()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Jon", "Smith", email: "jon@example.com", phone: "555-0100");
        var duplicate = await AddPersonAsync(f, "John", "Smyth", email: null, phone: "555-0199");

        var merged = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate, new PersonMergeFields(
            FirstName: PersonMergeChoice.Duplicate,
            LastName: PersonMergeChoice.Survivor,
            Email: PersonMergeChoice.Duplicate,
            Phone: PersonMergeChoice.Duplicate)));

        await Assert.That(merged.FirstName).IsEqualTo("John");
        await Assert.That(merged.LastName).IsEqualTo("Smith");
        await Assert.That(merged.Email).IsNull();
        await Assert.That(merged.Phone).IsEqualTo("555-0199");
    }

    [Test]
    public async Task MergePerson_Merged_DuplicateIsKeptAsInactiveReadOnlyHistory()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", email: "ann@example.com");
        var other = await AddPersonAsync(f, "Ann", "Leeson");

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        var history = await GetPersonAsync(f, duplicate.Id);
        await Assert.That(history.MergedIntoId).IsEqualTo(survivor.Id);
        await Assert.That(history.IsActive).IsFalse();
        await Assert.That(history.CanEdit).IsFalse();
        await Assert.That(history.Email).IsEqualTo("ann@example.com");
        var lookup = await TrackerReadAsync<PersonLookupResponse[]>(await SendAsync(f, HttpMethod.Get, "/people/lookup?name=Ann"));
        await Assert.That(lookup.Select(p => p.Id)).DoesNotContain(duplicate.Id);
        var edit = await SendAsync(f, HttpMethod.Put, $"/people/{duplicate.Id}", new PersonRequest("Ann", "Lee", IsActive: true), history.Version);
        await Assert.That(edit.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var current = await GetPersonAsync(f, other.Id);
        var again = await SendAsync(f, HttpMethod.Post, $"/people/{other.Id}/merge", new PersonMergeRequest(duplicate.Id, history.Version), current.Version);
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var assign = await SendAsync(f, HttpMethod.Post, $"/groups/{f.Group.Id}/members", new AssignmentRequest(duplicate.Id, Time.GetUtcNow()));
        await Assert.That(assign.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MergePerson_SurvivorWasMergedTarget_EarlierDuplicatesNameTheNewSurvivor()
    {
        using var f = await SeedAsync();
        var first = await AddPersonAsync(f, "Ann", "Lee");
        var second = await AddPersonAsync(f, "Ann", "Lee");
        var third = await AddPersonAsync(f, "Ann", "Lee");
        var afterFirstMerge = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, second, first));

        var response = await SendAsync(f, HttpMethod.Post, $"/people/{third.Id}/merge",
            new PersonMergeRequest(second.Id, afterFirstMerge.Version), third.Version);

        await TrackerReadAsync<PersonResponse>(response);
        await Assert.That((await GetPersonAsync(f, first.Id)).MergedIntoId).IsEqualTo(third.Id);
        await Assert.That((await GetPersonAsync(f, second.Id)).MergedIntoId).IsEqualTo(third.Id);
    }

    [Test]
    public async Task MergePerson_InactiveSurvivorAndActiveDuplicate_SurvivorIsActive()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        await InChurchAsync(f.ChurchId, async db =>
        {
            var stored = await db.People.SingleAsync(p => p.Id == survivor.Id);
            stored.IsActive = false;
            stored.Version = Guid.NewGuid();
            survivor.Version = stored.Version;
            await db.SaveChangesAsync();
        });
        var duplicate = await AddPersonAsync(f, "Ann", "Lee");

        var merged = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        await Assert.That(merged.IsActive).IsTrue();
    }

    [Test]
    public async Task MergePerson_StaleSurvivorOrDuplicateVersion_ReturnsPreconditionFailedAndChangesNothing()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee");

        var staleSurvivor = await SendAsync(f, HttpMethod.Post, $"/people/{survivor.Id}/merge",
            new PersonMergeRequest(duplicate.Id, duplicate.Version), Guid.NewGuid());
        var staleDuplicate = await SendAsync(f, HttpMethod.Post, $"/people/{survivor.Id}/merge",
            new PersonMergeRequest(duplicate.Id, Guid.NewGuid()), survivor.Version);
        var missingIfMatch = await SendAsync(f, HttpMethod.Post, $"/people/{survivor.Id}/merge",
            new PersonMergeRequest(duplicate.Id, duplicate.Version));

        await Assert.That(staleSurvivor.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(staleDuplicate.StatusCode).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(missingIfMatch.StatusCode).IsEqualTo(HttpStatusCode.PreconditionRequired);
        await Assert.That((await GetPersonAsync(f, duplicate.Id)).MergedIntoId).IsNull();
    }

    [Test]
    public async Task MergePerson_IntoItself_ReturnsBadRequest()
    {
        using var f = await SeedAsync();
        var person = await AddPersonAsync(f, "Ann", "Lee");

        var response = await MergeAsync(f, person, person);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MergePerson_BothLinkedToAccounts_ReturnsConflictWithReasonAndChangesNothing()
    {
        using var f = await SeedAsync();
        var firstAccount = await CreateUserAsync();
        var secondAccount = await CreateUserAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee", userId: firstAccount.Id);
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", userId: secondAccount.Id);

        var response = await MergeAsync(f, survivor, duplicate);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>() ?? throw new InvalidOperationException("Missing problem.");
        var reason = await ExtensionAsync(problem, PersonMergeService.ConflictReasonKey);
        await Assert.That(reason.GetString()).IsEqualTo(PersonMergeService.BothLinkedReason);
        await Assert.That((await GetPersonAsync(f, duplicate.Id)).MergedIntoId).IsNull();
    }

    [Test]
    public async Task MergePerson_OnlyDuplicateLinkedToAccount_MovesTheLinkToSurvivor()
    {
        using var f = await SeedAsync();
        var account = await CreateUserAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", userId: account.Id);

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        await InChurchAsync(f.ChurchId, async db =>
        {
            var linked = await db.People.Where(p => p.UserId == account.Id).Select(p => p.Id).ToArrayAsync();
            await Assert.That(linked).IsEquivalentTo([survivor.Id]);
        });
    }

    [Test]
    public async Task MergePerson_DuplicateAttended_MovesAttendanceToSurvivorAndCountsSharedMeetingOnce()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee");
        var otherMeeting = new GroupMeeting { GroupId = f.Group.Id, OccurredOn = f.Meeting.OccurredOn.AddDays(-7), RecordedAt = f.Meeting.RecordedAt.AddDays(-7) };
        await SeedAsync(f, otherMeeting,
            new Attendance { MeetingId = f.Meeting.Id, PersonId = survivor.Id, RecordedAt = f.Meeting.RecordedAt },
            new Attendance { MeetingId = f.Meeting.Id, PersonId = duplicate.Id, RecordedAt = f.Meeting.RecordedAt.AddMinutes(5) },
            new Attendance { MeetingId = otherMeeting.Id, PersonId = duplicate.Id, RecordedAt = otherMeeting.RecordedAt });

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        var shared = await TrackerReadAsync<SessionResponse>(await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions/{f.Meeting.Id}"));
        await Assert.That(shared.Attendees.Select(a => a.Id)).IsEquivalentTo([survivor.Id]);
        var moved = await TrackerReadAsync<SessionResponse>(await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/sessions/{otherMeeting.Id}"));
        await Assert.That(moved.Attendees.Select(a => a.Id)).IsEquivalentTo([survivor.Id]);
        var records = await TrackerReadAsync<PagedResponse<AttendanceRecordResponse>>(
            await SendAsync(f, HttpMethod.Get, $"/attendance-records?personId={duplicate.Id}"));
        await Assert.That(records.Items).IsEmpty();
    }

    [Test]
    public async Task MergePerson_OverlappingAssignmentsInOneGroup_JoinsThemIntoOneLeaderInterval()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee");
        var now = Time.GetUtcNow();
        await SeedAsync(f,
            new GroupAssignment { GroupId = f.Group.Id, PersonId = survivor.Id, Role = GroupRole.Member, StartsAt = now.AddDays(-100) },
            new GroupAssignment { GroupId = f.Group.Id, PersonId = duplicate.Id, Role = GroupRole.Leader, StartsAt = now.AddDays(-300), EndsAt = now.AddDays(-50) });

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        var leaders = await TrackerReadAsync<PagedResponse<AssignmentResponse>>(await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/leaders"));
        var members = await TrackerReadAsync<PagedResponse<AssignmentResponse>>(await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/members"));
        await Assert.That(members.Items).IsEmpty();
        var joined = await Assert.That(leaders.Items).HasSingleItem();
        await Assert.That(joined.PersonId).IsEqualTo(survivor.Id);
        await Assert.That(joined.StartsAt).IsEqualTo(now.AddDays(-300));
        await Assert.That(joined.EndsAt).IsNull();
    }

    [Test]
    public async Task MergePerson_SeparateAssignmentsInOneGroup_KeepsBothIntervalsForSurvivor()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee");
        var duplicate = await AddPersonAsync(f, "Ann", "Lee");
        var now = Time.GetUtcNow();
        await SeedAsync(f,
            new GroupAssignment { GroupId = f.Group.Id, PersonId = duplicate.Id, Role = GroupRole.Member, StartsAt = now.AddDays(-300), EndsAt = now.AddDays(-200) },
            new GroupAssignment { GroupId = f.Group.Id, PersonId = survivor.Id, Role = GroupRole.Member, StartsAt = now.AddDays(-100) });

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        var members = await TrackerReadAsync<PagedResponse<AssignmentResponse>>(await SendAsync(f, HttpMethod.Get, $"/groups/{f.Group.Id}/members"));
        await Assert.That(members.Items.Select(a => a.PersonId)).IsEquivalentTo([survivor.Id, survivor.Id]);
        await Assert.That(members.Items.Select(a => a.StartsAt)).IsEquivalentTo([now.AddDays(-300), now.AddDays(-100)]);
    }

    [Test]
    public async Task MergePerson_DuplicateHouseholdLeftEmpty_DeletesThatHousehold()
    {
        using var f = await SeedAsync();
        var survivorHome = await AddHouseholdAsync(f, "The Lee Family");
        var duplicateHome = await AddHouseholdAsync(f, "Ann's place");
        var survivor = await AddPersonAsync(f, "Ann", "Lee", householdId: survivorHome.Id);
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", householdId: duplicateHome.Id);

        var merged = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        await Assert.That(merged.HouseholdId).IsEqualTo(survivorHome.Id);
        var gone = await SendAsync(f, HttpMethod.Get, $"/households/{duplicateHome.Id}");
        await Assert.That(gone.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MergePerson_ChooseDuplicateHousehold_MovesSurvivorAndKeepsOccupiedHouseholds()
    {
        using var f = await SeedAsync();
        var survivorHome = await AddHouseholdAsync(f, "The Lee Family");
        var duplicateHome = await AddHouseholdAsync(f, "The Leigh Family");
        var survivor = await AddPersonAsync(f, "Ann", "Lee", householdId: survivorHome.Id);
        var spouse = await AddPersonAsync(f, "Ben", "Lee", householdId: survivorHome.Id);
        var duplicate = await AddPersonAsync(f, "Ann", "Leigh", householdId: duplicateHome.Id);

        var merged = await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate,
            new PersonMergeFields(Household: PersonMergeChoice.Duplicate)));

        await Assert.That(merged.HouseholdId).IsEqualTo(duplicateHome.Id);
        var newHome = await TrackerReadAsync<HouseholdResponse>(await SendAsync(f, HttpMethod.Get, $"/households/{duplicateHome.Id}"));
        await Assert.That(newHome.Members.Select(m => m.Id)).IsEquivalentTo([survivor.Id]);
        var oldHome = await TrackerReadAsync<HouseholdResponse>(await SendAsync(f, HttpMethod.Get, $"/households/{survivorHome.Id}"));
        await Assert.That(oldHome.Members.Select(m => m.Id)).IsEquivalentTo([spouse.Id]);
    }

    [Test]
    public async Task GetDuplicatePeople_SameNameEmailOrPhone_ListsEachPairOnceEarlierFirstWithReasons()
    {
        using var f = await SeedAsync();
        var original = await AddPersonAsync(f, "Ann", "Lee", email: "ann@example.com", phone: "(555) 010-0100", daysAgo: 30);
        var sameName = await AddPersonAsync(f, "ann", "LEE", daysAgo: 1);
        var sameEmail = await AddPersonAsync(f, "Annie", "Smith", email: "ANN@example.com", daysAgo: 2);
        var samePhone = await AddPersonAsync(f, "Grace", "Hall", phone: "555.010.0100", daysAgo: 3);
        await AddPersonAsync(f, "Ann", "Leeson", email: "other@example.com", phone: "555-0199");

        var candidates = await GetCandidatesAsync(f);

        var pairs = candidates.Where(c => c.Person.Id == original.Id).ToDictionary(c => c.Other.Id, c => c.Reasons);
        await Assert.That(pairs.Keys).IsEquivalentTo([sameName.Id, sameEmail.Id, samePhone.Id]);
        await Assert.That(pairs[sameName.Id]).IsEquivalentTo([DuplicateReason.Name]);
        await Assert.That(pairs[sameEmail.Id]).IsEquivalentTo([DuplicateReason.Email]);
        await Assert.That(pairs[samePhone.Id]).IsEquivalentTo([DuplicateReason.Phone]);
        await Assert.That(candidates.Any(c => c.Other.Id == original.Id)).IsFalse();
    }

    [Test]
    public async Task GetDuplicatePeople_WithoutPeopleManage_ReturnsForbidden()
    {
        using var f = await SeedAsync(ChurchMembershipRole.Leader);

        var response = await SendAsync(f, HttpMethod.Get, "/people/duplicates");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetDuplicatePeople_AfterMerge_NoLongerListsThePair()
    {
        using var f = await SeedAsync();
        var survivor = await AddPersonAsync(f, "Ann", "Lee", daysAgo: 2);
        var duplicate = await AddPersonAsync(f, "Ann", "Lee", daysAgo: 1);
        await Assert.That(await GetCandidatesAsync(f)).HasSingleItem();

        await TrackerReadAsync<PersonResponse>(await MergeAsync(f, survivor, duplicate));

        await Assert.That(await GetCandidatesAsync(f)).IsEmpty();
    }

    [Test]
    public async Task DismissDuplicatePeople_Pair_StopsSuggestingItInEitherOrder()
    {
        using var f = await SeedAsync();
        var first = await AddPersonAsync(f, "Ann", "Lee", daysAgo: 2);
        var second = await AddPersonAsync(f, "Ann", "Lee", daysAgo: 1);
        var third = await AddPersonAsync(f, "Ann", "Lee");

        var dismissed = await SendAsync(f, HttpMethod.Post, "/people/duplicates/dismissals", new DuplicateDismissalRequest(second.Id, first.Id));
        var again = await SendAsync(f, HttpMethod.Post, "/people/duplicates/dismissals", new DuplicateDismissalRequest(first.Id, second.Id));

        await Assert.That(dismissed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var pairs = (await GetCandidatesAsync(f)).Select(c => (c.Person.Id, c.Other.Id)).ToArray();
        await Assert.That(pairs).IsEquivalentTo([(first.Id, third.Id), (second.Id, third.Id)]);
    }

    [Test]
    public async Task DismissDuplicatePeople_SamePersonTwice_ReturnsBadRequest()
    {
        using var f = await SeedAsync();
        var person = await AddPersonAsync(f, "Ann", "Lee");

        var response = await SendAsync(f, HttpMethod.Post, "/people/duplicates/dismissals", new DuplicateDismissalRequest(person.Id, person.Id));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
