using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;
using Sheaf.Data.Poc;
using Sheaf.Poc.Ai;
using Sheaf.Poc.People;
using Sheaf.Services;

namespace Sheaf.Poc.Staff;

/// <summary>Captured → contacted → attended → in a group, for people leaders added in the window.</summary>
public sealed record FunnelResponse(int Days, int Captured, int Contacted, int Attended, int InGroup);

/// <summary>Follow-up work per leader. Counts only: this is about where help is needed, not ranking people.</summary>
public sealed record LeaderRow(string UserId, string Name, int Captured, int Contacted, int OverdueFirstContact, int GroupMembers, int QuietMembers);

public sealed record GapSummary(int OverdueFirstContact, int Unplaced, int QuietMembers, int MissedLastTwo);

public sealed record GapPerson(Guid Id, string Name, string AddedBy, DateTimeOffset AddedAt);

public sealed record GroupHealth(Guid Id, string Name, string Leaders, int RosterSize, double RecentAverage, double PriorAverage,
    DateOnly? LastMeeting, int MissedLastTwo, string Status);

public sealed record StaffOverviewResponse(FunnelResponse Funnel, LeaderRow[] Leaders, GapSummary Gaps, GapPerson[] Overdue, GroupHealth[] Groups);

public sealed record BriefResponse(string Text, bool AiLive, DateTimeOffset GeneratedAt);

/// <summary>
/// Staff dashboards computed from the tracker and POC tables on read. The weekly brief sends Claude totals and group
/// names only: no student names, notes or contact details leave the server.
/// </summary>
public sealed class StaffOverviewService(ApplicationDbContext db, TrackerScope scope, ISheafAi ai)
{
    public const int FunnelDays = 30;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<StaffOverviewResponse> OverviewAsync(string? leaderUserId, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        DateTimeOffset now = scope.Now;
        DateTimeOffset since = now.AddDays(-FunnelDays);
        DateOnly today = scope.Today;

        Dictionary<string, string> leaderNames = (await db.People.Where(p => p.UserId != null)
                .Select(p => new { UserId = p.UserId ?? string.Empty, Name = p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName) })
                .ToListAsync(ct))
            .DistinctBy(p => p.UserId).ToDictionary(p => p.UserId, p => p.Name);

        // People leaders added (not imported or seeded members), with what has happened since.
        var captured = await (
            from p in db.People.Where(p => p.IsActive && p.UserId == null && p.CreatedByUserId != null)
            join pr in db.PocPersonProfiles on p.Id equals pr.PersonId
            where pr.Source == PersonSource.LeaderEntered
            select new
            {
                p.Id,
                Name = p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName),
                AddedBy = p.CreatedByUserId ?? string.Empty,
                p.CreatedAt,
                pr.DoNotContactAt,
                pr.IsMinor,
                Contacted = db.PocTouchpoints.Any(t => t.PersonId == p.Id && t.Kind != TouchpointKind.Met),
                Attended = db.Attendances.Any(a => a.PersonId == p.Id && a.RemovedAt == null),
                InGroup = db.GroupAssignments.Any(m => m.Role == GroupRole.Member && m.PersonId == p.Id && m.StartsAt <= now && (m.EndsAt == null || m.EndsAt > now)),
            }).ToListAsync(ct);

        var window = captured.Where(c => c.CreatedAt >= since && (leaderUserId == null || c.AddedBy == leaderUserId)).ToList();
        FunnelResponse funnel = new(FunnelDays, window.Count, window.Count(c => c.Contacted), window.Count(c => c.Attended), window.Count(c => c.InGroup));

        // A minor is reached through their guardian, so they never count as waiting for a text.
        var overdue = captured.Where(c => !c.Contacted && c.DoNotContactAt == null && !c.IsMinor && now - c.CreatedAt > TimeSpan.FromHours(FollowUpService.FirstContactHours)).ToList();

        // Groups: roster, attendance trend and who has gone quiet.
        var groups = await db.Groups.Where(c => c.EndsAt == null || c.EndsAt > now).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var leaders = await (
            from l in db.GroupAssignments.Where(l => l.Role == GroupRole.Leader && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now))
            join p in db.People on l.PersonId equals p.Id
            select new { ContextId = l.GroupId, p.FirstName, UserId = p.UserId ?? string.Empty }).ToListAsync(ct);
        var members = await db.GroupAssignments.Where(m => m.Role == GroupRole.Member && m.StartsAt <= now && (m.EndsAt == null || m.EndsAt > now))
            .Select(m => new { ContextId = m.GroupId, m.PersonId }).ToListAsync(ct);
        DateOnly trendSince = today.AddDays(-56);
        var sessions = await db.GroupMeetings.Where(s => s.OccurredOn >= trendSince)
            .Select(s => new { s.Id, ContextId = s.GroupId, s.OccurredOn, Count = db.Attendances.Count(a => a.MeetingId == s.Id && a.RemovedAt == null) })
            .ToListAsync(ct);
        Guid[] lastTwoIds = [.. sessions.GroupBy(s => s.ContextId).SelectMany(g => g.OrderByDescending(s => s.OccurredOn).Take(2)).Select(s => s.Id)];
        HashSet<(Guid, Guid)> attendedRecent = [.. (await db.Attendances.Where(a => lastTwoIds.Contains(a.MeetingId) && a.RemovedAt == null)
            .Select(a => new { SessionId = a.MeetingId, a.PersonId }).ToListAsync(ct)).Select(a => (a.SessionId, a.PersonId))];
        Guid[] memberIds = [.. members.Select(m => m.PersonId).Distinct()];
        Dictionary<Guid, DateTimeOffset> lastTouch = (await db.PocTouchpoints.Where(t => memberIds.Contains(t.PersonId))
                .GroupBy(t => t.PersonId).Select(g => new { g.Key, Last = g.Max(t => t.At) }).ToListAsync(ct))
            .ToDictionary(t => t.Key, t => t.Last);
        bool Quiet(Guid personId) => !lastTouch.TryGetValue(personId, out DateTimeOffset at) || now - at > TimeSpan.FromDays(FollowUpService.QuietDays);

        List<GroupHealth> health = [];
        int missedTotal = 0;
        foreach (var group in groups)
        {
            var recent = sessions.Where(s => s.ContextId == group.Id).OrderByDescending(s => s.OccurredOn).ToList();
            double recentAvg = recent.Take(4).Select(s => s.Count).DefaultIfEmpty().Average();
            double priorAvg = recent.Skip(4).Take(4).Select(s => s.Count).DefaultIfEmpty().Average();
            Guid[] lastTwo = [.. recent.Take(2).Select(s => s.Id)];
            Guid[] roster = [.. members.Where(m => m.ContextId == group.Id).Select(m => m.PersonId)];
            int missed = lastTwo.Length == 2 ? roster.Count(p => !lastTwo.Any(s => attendedRecent.Contains((s, p)))) : 0;
            missedTotal += missed;
            DateOnly? last = recent.FirstOrDefault()?.OccurredOn;
            string status = last is null || last < today.AddDays(-14) ? "No recent meetings"
                : priorAvg > 0 && recentAvg < priorAvg * 0.85 ? "Slipping"
                : priorAvg > 0 && recentAvg > priorAvg * 1.1 ? "Growing"
                : "Steady";
            health.Add(new GroupHealth(group.Id, group.Name, string.Join(" & ", leaders.Where(l => l.ContextId == group.Id).Select(l => l.FirstName)),
                roster.Length, Math.Round(recentAvg, 1), Math.Round(priorAvg, 1), last, missed, status));
        }

        LeaderRow[] leaderRows = [.. leaders.Select(l => l.UserId).Where(u => u.Length > 0).Distinct()
            .Concat(captured.Select(c => c.AddedBy)).Distinct()
            .Where(u => leaderNames.ContainsKey(u))
            .Select(userId =>
            {
                Guid[] led = [.. leaders.Where(l => l.UserId == userId).Select(l => l.ContextId)];
                Guid[] theirMembers = [.. members.Where(m => led.Contains(m.ContextId)).Select(m => m.PersonId).Distinct()];
                var theirs = captured.Where(c => c.AddedBy == userId && c.CreatedAt >= since).ToList();
                return new LeaderRow(userId, leaderNames[userId], theirs.Count, theirs.Count(c => c.Contacted),
                    overdue.Count(c => c.AddedBy == userId), theirMembers.Length, theirMembers.Count(Quiet));
            })
            .OrderByDescending(r => r.OverdueFirstContact).ThenBy(r => r.Name)];

        GapSummary gaps = new(overdue.Count, captured.Count(c => !c.InGroup && c.DoNotContactAt == null), memberIds.Count(Quiet), missedTotal);
        GapPerson[] overdueList = [.. overdue.OrderBy(c => c.CreatedAt).Take(10)
            .Select(c => new GapPerson(c.Id, c.Name, leaderNames.GetValueOrDefault(c.AddedBy, "A leader"), c.CreatedAt))];

        return new StaffOverviewResponse(funnel, leaderRows, gaps, overdueList, [.. health.OrderBy(h => h.Status == "Slipping" ? 0 : h.Status == "No recent meetings" ? 1 : 2).ThenBy(h => h.Name)]);
    }

    public async Task<BriefResponse> BriefAsync(CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        StaffOverviewResponse overview = await OverviewAsync(null, ct);

        // Totals and group/leader names only. The overdue list (student names) is deliberately left out.
        var aggregates = new
        {
            WindowDays = overview.Funnel.Days,
            overview.Funnel,
            overview.Gaps,
            Groups = overview.Groups.Select(g => new { g.Name, g.Leaders, g.RosterSize, g.RecentAverage, g.PriorAverage, g.MissedLastTwo, g.Status }),
            Leaders = overview.Leaders.Select(l => new { FirstName = l.Name.Split(' ')[0], l.Captured, l.Contacted, l.OverdueFirstContact, l.QuietMembers }),
        };
        string facts = JsonSerializer.Serialize(aggregates, JsonOptions);
        string text = await ai.CompleteTextAsync(BriefPrompt, facts, () => CannedBrief(overview), AiEffort.Medium, ct);

        db.PocAuditEntries.Add(new PocAuditEntry
        {
            At = scope.Now,
            ActorUserId = actor.UserId,
            Action = "ai.brief",
            Summary = $"{(ai.IsLive ? "Claude" : "The fallback")} wrote the weekly brief from church-wide totals (no individual names sent)",
            AiInvolved = true,
        });
        await db.SaveChangesAsync(ct);
        return new BriefResponse(text.Trim(), ai.IsLive, scope.Now);
    }

    private const string BriefPrompt = """
        You write a short weekly brief for the campus pastor of a college ministry, from the totals below. Write three
        short paragraphs in plain, warm language:
        1. How outreach went: people met, followed up with, came to a group, joined a group.
        2. Where follow-up is slipping, and which leaders could use a hand (frame it as support, never blame).
        3. Group health: which groups are growing, which are slipping, and one concrete suggestion for the week.
        Use only these numbers; do not invent any. Do not rank or score leaders. Under 200 words. No headings, no
        bullet points, no markdown.
        """;

    private static string CannedBrief(StaffOverviewResponse o)
    {
        FunnelResponse f = o.Funnel;
        string slipping = string.Join(" and ", o.Groups.Where(g => g.Status == "Slipping").Select(g => g.Name));
        string growing = string.Join(" and ", o.Groups.Where(g => g.Status == "Growing").Select(g => g.Name));
        string help = string.Join(" and ", o.Leaders.Where(l => l.OverdueFirstContact > 0).OrderByDescending(l => l.OverdueFirstContact).Take(2).Select(l => l.Name.Split(' ')[0]));

        return $"Leaders met {f.Captured} new people in the last {f.Days} days. {f.Contacted} have had a follow-up, {f.Attended} came to a group, and {f.InGroup} joined one."
            + $"\n\n{o.Gaps.OverdueFirstContact} people are past the 48-hour window without a first text"
            + (help.Length > 0 ? $"; {help} might appreciate a hand this week." : ".")
            + $" {o.Gaps.QuietMembers} group members haven't heard from anyone in two weeks."
            + "\n\n" + (growing.Length > 0 ? $"{growing} {(growing.Contains(" and ", StringComparison.Ordinal) ? "are" : "is")} growing. " : string.Empty)
            + (slipping.Length > 0 ? $"{slipping} {(slipping.Contains(" and ", StringComparison.Ordinal) ? "are" : "is")} slipping, so a check-in with those leaders would be timely. " : "Groups are holding steady. ")
            + $"{o.Gaps.MissedLastTwo} members missed the last two meetings of their group.";
    }
}
