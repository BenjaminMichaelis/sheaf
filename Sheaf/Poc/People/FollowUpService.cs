using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;
using Sheaf.Data.Poc;
using Sheaf.Poc.Ai;
using Sheaf.Services;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Poc.People;

/// <summary>
/// "My people": members of the groups a leader leads plus the people they added themselves. Nudges are worked out when
/// the list loads (no scheduled job): reach someone new within 48 hours, notice two missed meetings, and notice when it
/// has been quiet for two weeks.
/// </summary>
public sealed class FollowUpService(ApplicationDbContext db, TrackerScope scope, ISheafAi ai)
{
    public const int FirstContactHours = 48;
    public const int QuietDays = 14;
    private const int NewWindowDays = 7;

    private sealed record Membership(Guid PersonId, Guid GroupId, DateTimeOffset JoinedAt);

    private sealed record GroupInfo(Guid Id, string Name, DayOfWeek? MeetsOn, Guid[] LastTwoSessions, DateOnly[] LastTwoDates);

    private sealed record Roster(ChurchActor Actor, string LeaderFirstName, List<Person> People, Dictionary<Guid, PocPersonProfile> Profiles,
        List<Membership> Memberships, Dictionary<Guid, GroupInfo> Groups, HashSet<(Guid SessionId, Guid PersonId)> Attended,
        List<PocTouchpoint> Touchpoints, Dictionary<string, string> UserNames);

    public async Task<MyPersonResponse[]> MyPeopleAsync(CancellationToken ct)
    {
        Roster roster = await LoadAsync(null, ct);
        return [.. roster.People.Select(p => Summarize(roster, p))
            .OrderByDescending(p => p.Nudge?.Urgency ?? 0)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<PersonDetailResponse> DetailAsync(Guid personId, CancellationToken ct)
    {
        Roster roster = await LoadAsync(personId, ct);
        Person person = roster.People.SingleOrDefault(p => p.Id == personId) ?? throw Missing();
        PocPersonProfile? profile = roster.Profiles.GetValueOrDefault(person.Id);

        GuardianResponse? guardian = null;
        if (profile?.GuardianPersonId is { } guardianId)
        {
            guardian = await db.People.Where(p => p.Id == guardianId)
                .Select(p => new GuardianResponse(p.Id, p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName), p.Phone))
                .SingleOrDefaultAsync(ct);
        }

        // Attendance across every meeting of their groups in the last eight weeks.
        Guid[] groupIds = [.. roster.Memberships.Where(m => m.PersonId == person.Id).Select(m => m.GroupId)];
        DateOnly since = scope.Today.AddDays(-56);
        var sessions = await db.GroupMeetings.Where(s => groupIds.Contains(s.GroupId) && s.OccurredOn >= since).Select(s => s.Id).ToListAsync(ct);
        int attended = await db.Attendances.CountAsync(a => sessions.Contains(a.MeetingId) && a.PersonId == person.Id && a.RemovedAt == null, ct);

        TouchpointResponse[] touchpoints = [.. roster.Touchpoints.Where(t => t.PersonId == person.Id).OrderByDescending(t => t.At).Select(t => ToResponse(roster, t))];
        return new PersonDetailResponse(Summarize(roster, person), profile?.IsMinor == true ? null : person.Email, profile?.Notes, guardian, touchpoints,
            attended, sessions.Count, DraftBlockedReason(profile));
    }

    public async Task<TouchpointResponse> LogTouchpointAsync(Guid personId, TouchpointRequest request, CancellationToken ct)
    {
        Roster roster = await LoadAsync(personId, ct);
        Person person = roster.People.SingleOrDefault(p => p.Id == personId) ?? throw Missing();
        if (!Enum.IsDefined(request.Kind) || request.Note?.Length > 500)
        {
            throw Invalid();
        }

        PocTouchpoint touchpoint = new()
        {
            PersonId = person.Id,
            UserId = roster.Actor.UserId,
            At = scope.Now,
            Kind = request.Kind,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            AiDrafted = request.AiDrafted,
        };
        db.PocTouchpoints.Add(touchpoint);
        if (request.AiDrafted)
        {
            Audit(roster.Actor, $"Sent {person.FirstName} a text that AI drafted and they reviewed", person.Id, aiInvolved: true);
        }

        await db.SaveChangesAsync(ct);
        roster.Touchpoints.Add(touchpoint);
        return ToResponse(roster, touchpoint);
    }

    public async Task<DraftResponse> DraftAsync(Guid personId, DraftRequest request, CancellationToken ct)
    {
        Roster roster = await LoadAsync(personId, ct);
        Person person = roster.People.SingleOrDefault(p => p.Id == personId) ?? throw Missing();
        PocPersonProfile? profile = roster.Profiles.GetValueOrDefault(person.Id);
        // Never draft for a minor or someone marked Don't contact, whatever the client asks.
        if (DraftBlockedReason(profile) is not null)
        {
            throw Denied();
        }

        MyPersonResponse summary = Summarize(roster, person);
        GroupInfo? ownGroup = GroupOf(roster, person.Id);
        // Someone not in a group yet can be invited to the leader's own.
        GroupInfo? group = ownGroup ?? roster.Groups.Values.FirstOrDefault();
        string context = string.Join('\n', new[]
        {
            $"Leader's first name: {roster.LeaderFirstName}",
            $"Their first name: {person.FirstName}",
            profile?.HowWeMet is { } met ? $"How they met: {met}" : null,
            profile?.ClassYear is { } year ? $"Class year: {year}" : null,
            profile?.Dorm is { } dorm ? $"Dorm: {dorm}" : null,
            profile?.Notes is { } notes ? $"Things to remember: {notes}" : null,
            group is null ? null : (ownGroup is null ? "Leader's small group they could be invited to: " : "Their small group: ") + group.Name + (group.MeetsOn is { } day ? $", meets {day}s" : string.Empty),
            summary.NextStep is { } next ? $"Leader's planned next step: {next}" : null,
            summary.Nudge is { } nudge ? $"Why now: {nudge.Label}" : null,
            string.IsNullOrWhiteSpace(request.Intent) ? null : $"What the leader wants to say: {request.Intent.Trim()}",
        }.Where(line => line is not null));

        string text = await ai.CompleteTextAsync(DraftPrompt, context, () => CannedDraft(roster, person, summary, group, request.Intent), AiEffort.Low, ct);
        text = text.Trim().Trim('"');
        Audit(roster.Actor, $"AI drafted a text to {person.FirstName} for the leader to review", person.Id, aiInvolved: true);
        await db.SaveChangesAsync(ct);
        return new DraftResponse(text.Length > 600 ? text[..600] : text, ai.IsLive);
    }

    public async Task<MyPersonResponse> SetDoNotContactAsync(Guid personId, DoNotContactRequest request, CancellationToken ct)
    {
        Roster roster = await LoadAsync(personId, ct);
        Person person = roster.People.SingleOrDefault(p => p.Id == personId) ?? throw Missing();
        PocPersonProfile profile = await ProfileForUpdateAsync(person.Id, ct);
        profile.DoNotContactAt = request.DoNotContact ? scope.Now : null;
        profile.DoNotContactByUserId = request.DoNotContact ? roster.Actor.UserId : null;
        roster.Profiles[person.Id] = profile;
        Audit(roster.Actor, request.DoNotContact
            ? $"Marked {person.Name} Don't contact: no drafts or nudges for any leader"
            : $"Cleared Don't contact for {person.Name}", person.Id);
        await db.SaveChangesAsync(ct);
        return Summarize(roster, person);
    }

    public async Task<MyPersonResponse> SetNextStepAsync(Guid personId, NextStepRequest request, CancellationToken ct)
    {
        Roster roster = await LoadAsync(personId, ct);
        Person person = roster.People.SingleOrDefault(p => p.Id == personId) ?? throw Missing();
        if (request.NextStep?.Length > 300)
        {
            throw Invalid();
        }

        PocPersonProfile profile = await ProfileForUpdateAsync(person.Id, ct);
        profile.NextStep = string.IsNullOrWhiteSpace(request.NextStep) ? null : request.NextStep.Trim();
        roster.Profiles[person.Id] = profile;
        await db.SaveChangesAsync(ct);
        return Summarize(roster, person);
    }

    private const string DraftPrompt = """
        You write a short text message from a volunteer college ministry leader to a student they know. Write in the
        leader's voice: warm, casual, and brief, like a real text from a peer, not an organization. One to three short
        sentences, under 280 characters. At most one emoji. No hashtags, no sign-off, no quotation marks.
        Use only the facts given. Do not mention that they were noted as missing, quiet or "on a list"; just reach out
        naturally. Never mention health, family problems, money, or anything private. Return only the message text.
        """;

    private static string CannedDraft(Roster roster, Person person, MyPersonResponse summary, GroupInfo? group, string? intent)
    {
        string day = group?.MeetsOn is { } meets ? $"{meets}" : "this week";
        string invite = group is null
            ? "A few of us do a small group during the week, want to come check it out?"
            : $"We've got {group.Name} on {day}s if you want to come check it out, happy to walk over together.";
        string? kind = intent?.ToLowerInvariant() switch
        {
            { } i when i.Contains("group", StringComparison.Ordinal) => "invite",
            { } i when i.Contains("coffee", StringComparison.Ordinal) => "coffee",
            { } i when i.Contains("check", StringComparison.Ordinal) => "quiet",
            _ => summary.Nudge?.Kind,
        };

        return kind switch
        {
            "missed" => $"Hey {person.FirstName}! Missed you at {group?.Name ?? "group"} the last couple weeks. Hope the semester's treating you ok. Would love to see you {day}!",
            "quiet" => $"Hey {person.FirstName}, it's been a minute! How's everything going?",
            "coffee" => $"Hey {person.FirstName}! Want to grab coffee this week? My treat.",
            "invite" => $"Hey {person.FirstName}! {invite}",
            _ => $"Hey {person.FirstName}! It's {roster.LeaderFirstName}. Great meeting you{WhereWeMet(summary.HowWeMet)}! {invite}",
        };
    }

    /// <summary>" at the welcome week BBQ" from "Welcome week BBQ"; nothing when it does not read as a place.</summary>
    private static string WhereWeMet(string? howWeMet) => howWeMet switch
    {
        null => string.Empty,
        _ when howWeMet.StartsWith("Friend", StringComparison.OrdinalIgnoreCase) => string.Empty,
        _ when howWeMet.StartsWith("Sunday", StringComparison.OrdinalIgnoreCase) || howWeMet.StartsWith("Smith", StringComparison.OrdinalIgnoreCase) => $" at {howWeMet}",
        _ => $" at the {char.ToLowerInvariant(howWeMet[0])}{howWeMet[1..]}",
    };

    private static string? DraftBlockedReason(PocPersonProfile? profile) =>
        profile?.DoNotContactAt is not null ? "Marked Don't contact."
        : profile?.IsMinor == true ? "Under 18: reach out through their parent or guardian instead."
        : null;

    private MyPersonResponse Summarize(Roster roster, Person person)
    {
        PocPersonProfile? profile = roster.Profiles.GetValueOrDefault(person.Id);
        PocTouchpoint? last = roster.Touchpoints.Where(t => t.PersonId == person.Id).MaxBy(t => t.At);
        GroupInfo? group = GroupOf(roster, person.Id);
        bool doNotContact = profile?.DoNotContactAt is not null;
        bool isMinor = profile?.IsMinor == true;
        return new MyPersonResponse(person.Id, person.Name, isMinor ? null : person.Phone, profile?.Dorm, profile?.ClassYear, profile?.HowWeMet,
            profile?.Source, isMinor, doNotContact, group?.Name, profile?.NextStep, person.CreatedAt, person.CreatedByUserId == roster.Actor.UserId,
            last is null ? null : ToResponse(roster, last), doNotContact ? null : Nudge(roster, person, profile, group, last));
    }

    private NudgeResponse? Nudge(Roster roster, Person person, PocPersonProfile? profile, GroupInfo? group, PocTouchpoint? last)
    {
        DateTimeOffset now = scope.Now;
        string guardianNote = profile?.IsMinor == true ? " · contact their guardian" : string.Empty;
        bool followedUp = roster.Touchpoints.Any(t => t.PersonId == person.Id && t.Kind != TouchpointKind.Met);

        if (person.CreatedByUserId == roster.Actor.UserId && !followedUp && now - person.CreatedAt < TimeSpan.FromDays(NewWindowDays))
        {
            DateTimeOffset due = person.CreatedAt.AddHours(FirstContactHours);
            return due > now
                ? new NudgeResponse("new", $"New: reach out within {Math.Max(1, (int)Math.Ceiling((due - now).TotalHours))} hours{guardianNote}", 2)
                : new NudgeResponse("new", $"Added {(int)(now - person.CreatedAt).TotalDays} days ago, no follow-up yet{guardianNote}", 3);
        }

        if (group is { LastTwoSessions.Length: 2 }
            && roster.Memberships.Any(m => m.PersonId == person.Id && m.GroupId == group.Id
                && DateOnly.FromDateTime(m.JoinedAt.UtcDateTime) <= group.LastTwoDates.Min())
            && !group.LastTwoSessions.Any(s => roster.Attended.Contains((s, person.Id))))
        {
            return new NudgeResponse("missed", $"Missed the last 2 {group.Name} meetings{guardianNote}", 2);
        }

        DateTimeOffset lastContact = last?.At ?? person.CreatedAt;
        int quietDays = (int)(now - lastContact).TotalDays;
        if (quietDays >= QuietDays)
        {
            return new NudgeResponse("quiet", last is null ? $"No touchpoint yet{guardianNote}" : $"Quiet for {quietDays} days{guardianNote}", 1);
        }

        return null;
    }

    private static GroupInfo? GroupOf(Roster roster, Guid personId) =>
        roster.Memberships.Where(m => m.PersonId == personId).Select(m => roster.Groups.GetValueOrDefault(m.GroupId)).OfType<GroupInfo>().FirstOrDefault();

    private static TouchpointResponse ToResponse(Roster roster, PocTouchpoint t) =>
        new(t.Id, t.Kind, t.At, t.Note, t.AiDrafted, roster.UserNames.GetValueOrDefault(t.UserId, "A leader"));

    private async Task<PocPersonProfile> ProfileForUpdateAsync(Guid personId, CancellationToken ct)
    {
        PocPersonProfile? profile = await db.PocPersonProfiles.SingleOrDefaultAsync(p => p.PersonId == personId, ct);
        if (profile is null)
        {
            profile = new PocPersonProfile { PersonId = personId, Source = PersonSource.Seeded };
            db.PocPersonProfiles.Add(profile);
        }

        return profile;
    }

    /// <summary>Loads everyone on the caller's list (or just <paramref name="only"/>) with what the nudges need.</summary>
    private async Task<Roster> LoadAsync(Guid? only, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        DateTimeOffset now = scope.Now;

        Guid[] groupIds = await db.GroupAssignments.Where(l => l.Role == GroupRole.Leader)
            .Where(l => l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)
                && db.People.Any(p => p.Id == l.PersonId && p.UserId == actor.UserId)
                && db.Groups.Any(c => c.Id == l.GroupId && (c.EndsAt == null || c.EndsAt > now)))
            .Select(l => l.GroupId).Distinct().ToArrayAsync(ct);
        List<Membership> memberships = await db.GroupAssignments
            .Where(m => m.Role == GroupRole.Member && groupIds.Contains(m.GroupId) && m.StartsAt <= now && (m.EndsAt == null || m.EndsAt > now))
            .Select(m => new Membership(m.PersonId, m.GroupId, m.StartsAt)).ToListAsync(ct);
        Guid[] memberIds = [.. memberships.Select(m => m.PersonId)];

        IQueryable<Person> people = db.People.Where(p => p.IsActive && p.UserId == null
            && (memberIds.Contains(p.Id) || p.CreatedByUserId == actor.UserId));
        if (only is { } id)
        {
            people = people.Where(p => p.Id == id);
        }

        List<Person> list = await people.ToListAsync(ct);
        Guid[] ids = [.. list.Select(p => p.Id)];
        Dictionary<Guid, PocPersonProfile> profiles = await db.PocPersonProfiles.Where(p => ids.Contains(p.PersonId)).ToDictionaryAsync(p => p.PersonId, ct);
        List<PocTouchpoint> touchpoints = await db.PocTouchpoints.Where(t => ids.Contains(t.PersonId)).ToListAsync(ct);

        var groups = await db.Groups.Where(c => groupIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var sessions = await db.GroupMeetings.Where(s => groupIds.Contains(s.GroupId))
            .OrderByDescending(s => s.OccurredOn).Select(s => new { s.Id, s.GroupId, s.OccurredOn }).ToListAsync(ct);
        Dictionary<Guid, GroupInfo> groupInfo = groups.ToDictionary(g => g.Id, g =>
        {
            var lastTwo = sessions.Where(s => s.GroupId == g.Id).Take(2).ToList();
            DayOfWeek? day = lastTwo.Count > 0 ? lastTwo[0].OccurredOn.DayOfWeek : null;
            return new GroupInfo(g.Id, g.Name, day, [.. lastTwo.Select(s => s.Id)], [.. lastTwo.Select(s => s.OccurredOn)]);
        });
        Guid[] recentSessionIds = [.. groupInfo.Values.SelectMany(g => g.LastTwoSessions)];
        HashSet<(Guid, Guid)> attended = [.. (await db.Attendances
                .Where(a => recentSessionIds.Contains(a.MeetingId) && a.RemovedAt == null && ids.Contains(a.PersonId))
                .Select(a => new { SessionId = a.MeetingId, a.PersonId }).ToListAsync(ct))
            .Select(a => (a.SessionId, a.PersonId))];

        var leaders = await db.People.Where(p => p.UserId != null)
            .Select(p => new { UserId = p.UserId ?? string.Empty, p.FirstName, p.LastName }).ToListAsync(ct);
        Dictionary<string, string> userNames = leaders.DistinctBy(p => p.UserId)
            .ToDictionary(p => p.UserId, p => p.FirstName + (p.LastName is null ? string.Empty : " " + p.LastName));
        string leaderFirst = leaders.FirstOrDefault(p => p.UserId == actor.UserId)?.FirstName ?? "your leader";

        return new Roster(actor, leaderFirst, list, profiles, memberships, groupInfo, attended, touchpoints, userNames);
    }

    private void Audit(ChurchActor actor, string summary, Guid personId, bool aiInvolved = false) =>
        db.PocAuditEntries.Add(new PocAuditEntry
        {
            At = scope.Now,
            ActorUserId = actor.UserId,
            Action = aiInvolved ? "ai.draft" : "person.contact",
            Summary = summary,
            AiInvolved = aiInvolved,
            PersonId = personId,
        });
}
