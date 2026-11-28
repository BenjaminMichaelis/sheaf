using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;
using Sheaf.Data.Poc;
using Sheaf.Poc.Ai;
using Sheaf.Poc.Demo;
using Sheaf.Services;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Poc.Capture;

/// <summary>
/// Capture → AI → approve. Notes are redacted before Claude reads them; Claude's output only ever becomes a proposal;
/// a person approves each one, and every step is written to the audit log.
/// </summary>
public sealed partial class CaptureService(ApplicationDbContext db, TrackerScope scope, ISheafAi ai, ILogger<CaptureService> logger)
{
    private const int MaxNoteLength = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] ClassYears = ["Freshman", "Sophomore", "Junior", "Senior", "Grad"];

    public async Task<CaptureResponse> QuickAddAsync(QuickAddRequest request, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        DateTimeOffset now = scope.Now;
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw Invalid();
        }

        ProposedPerson details = new(request.FirstName.Trim(), Trim(request.LastName), request.IsMinor ? null : Trim(request.Phone), null,
            null, null, Trim(request.HowWeMet), request.IsMinor, null, null);
        Person person = AddPerson(details, actor, now);

        if (request.IsMinor && !string.IsNullOrWhiteSpace(request.GuardianName))
        {
            string[] parts = request.GuardianName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            Person guardian = new()
            {
                FirstName = parts[0],
                LastName = parts.Length > 1 ? parts[1] : person.LastName,
                Phone = Trim(request.GuardianPhone),
                CreatedAt = now,
                CreatedByUserId = actor.UserId,
            };
            db.People.Add(guardian);
            db.PocPersonProfiles.Add(new PocPersonProfile { PersonId = guardian.Id, Source = PersonSource.LeaderEntered, HowWeMet = $"Guardian of {person.FirstName}" });
            db.PocPersonProfiles.Local.Single(p => p.PersonId == person.Id).GuardianPersonId = guardian.Id;
        }

        PocCapture capture = new()
        {
            CreatedByUserId = actor.UserId,
            CreatedAt = now,
            Kind = CaptureKind.QuickAdd,
            Status = CaptureStatus.Done,
            Text = $"Added {person.Name}" + (details.HowWeMet is null ? string.Empty : $" ({details.HowWeMet})"),
            Summary = request.IsMinor ? "Under 18: no phone stored for them; contact goes through their guardian." : null,
        };
        db.PocCaptures.Add(capture);
        Audit(actor, now, "person.added", $"Added {person.Name} with quick add" + (request.IsMinor ? " (under 18, guardian contact only)" : string.Empty), personId: person.Id);

        // A quick add skips the AI, but still gets the duplicate check against everyone already in the church.
        if (PersonMatcher.Best(details, await ExistingPeopleAsync(person.Id, ct)) is { } match)
        {
            capture.Status = CaptureStatus.Ready;
            db.PocProposals.Add(new PocProposal
            {
                CaptureId = capture.Id,
                OwnerUserId = actor.UserId,
                CreatedAt = now,
                Kind = ProposalKind.MergeDuplicates,
                Title = person.Name == match.Person.Name
                    ? $"{person.Name} may already be in the church"
                    : $"Is {person.Name} the same person as {match.Person.Name}?",
                Payload = Serialize(details),
                Reasons = string.Join('\n', match.Reasons),
                TargetPersonId = match.Person.Id,
                DuplicatePersonId = person.Id,
            });
        }

        await db.SaveChangesAsync(ct);
        return (await CapturesAsync(actor, db.PocCaptures.Where(c => c.Id == capture.Id), ct)).Single() with { PersonId = person.Id };
    }

    public async Task<CaptureResponse> NoteAsync(NoteRequest request, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        string text = request.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxNoteLength || request.Kind == CaptureKind.QuickAdd)
        {
            throw Invalid();
        }

        // Redact before anything else touches the text. The original is stored only in the Staff-only inbox.
        RedactionResult redaction = SensitiveFilter.Redact(text);
        DateTimeOffset now = scope.Now;
        PocCapture capture = new()
        {
            CreatedByUserId = actor.UserId,
            CreatedAt = now,
            Kind = request.Kind,
            Status = CaptureStatus.Ready,
            Text = redaction.Text,
            Redacted = redaction.Redacted,
            AiLive = ai.IsLive,
        };
        db.PocCaptures.Add(capture);
        if (redaction.Redacted)
        {
            db.PocSensitiveNotes.Add(new PocSensitiveNote
            {
                CaptureId = capture.Id,
                CreatedByUserId = actor.UserId,
                CreatedAt = now,
                OriginalText = text,
                Categories = string.Join(", ", redaction.Categories),
            });
            Audit(actor, now, "note.redacted", $"Kept {string.Join(", ", redaction.Categories)} details out of AI and sent them to the pastor inbox");
        }

        Extraction extraction;
        try
        {
            extraction = await ai.CompleteJsonAsync(ExtractionPrompt(), redaction.Text, () => CannedExtractor.Extract(redaction.Text), AiEffort.Medium, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogExtractionFailed(logger, exception);
            extraction = CannedExtractor.Extract(redaction.Text);
            capture.AiLive = false;
        }

        capture.Summary = Truncate(extraction.Summary, 500);
        List<(Person, PocPersonProfile?)> existing = await ExistingPeopleAsync(null, ct);
        foreach (ExtractedPerson found in extraction.People.Where(p => !string.IsNullOrWhiteSpace(p.FirstName)).Take(10))
        {
            ProposedPerson details = Normalize(found);
            MatchCandidate? match = PersonMatcher.Best(details, existing);
            db.PocProposals.Add(new PocProposal
            {
                CaptureId = capture.Id,
                OwnerUserId = actor.UserId,
                CreatedAt = now,
                Kind = match is null ? ProposalKind.NewPerson : ProposalKind.MatchExisting,
                Title = match is null ? $"Add {Name(details)}" : $"Is this the same {match.Person.Name}?",
                Payload = Serialize(details),
                Reasons = match is null ? "Mentioned in your note" : string.Join('\n', match.Reasons),
                SourceExcerpt = Truncate(found.SourceExcerpt, 1000),
                TargetPersonId = match?.Person.Id,
                AiGenerated = true,
            });
        }

        string noteKind = request.Kind == CaptureKind.Voice ? "voice note" : "typed note";
        Audit(actor, now, "ai.extracted", $"{(capture.AiLive ? "Claude" : "The fallback extractor")} read a {noteKind} and suggested {extraction.People.Length} " + (extraction.People.Length == 1 ? "person" : "people"), aiInvolved: true);
        if (extraction.People.Length == 0)
        {
            capture.Status = CaptureStatus.Done;
        }

        await db.SaveChangesAsync(ct);
        return (await CapturesAsync(actor, db.PocCaptures.Where(c => c.Id == capture.Id), ct)).Single();
    }

    public async Task<CaptureResponse[]> RecentCapturesAsync(CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        return await CapturesAsync(actor, db.PocCaptures.Where(c => c.CreatedByUserId == actor.UserId).OrderByDescending(c => c.CreatedAt).Take(20), ct);
    }

    public async Task<ProposalResponse[]> ProposalsAsync(bool all, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        IQueryable<PocProposal> proposals = db.PocProposals.Where(p => p.Status == ProposalStatus.Pending);
        if (!all || !TrackerScope.ManagesPeople(actor))
        {
            proposals = proposals.Where(p => p.OwnerUserId == actor.UserId);
        }

        return await ResponsesAsync(await proposals.OrderBy(p => p.CreatedAt).ToListAsync(ct), ct);
    }

    public async Task<ProposalResponse> DecideAsync(Guid id, ProposalDecisionRequest request, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        DateTimeOffset now = scope.Now;
        PocProposal proposal = await db.PocProposals.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        if (proposal.OwnerUserId != actor.UserId && !TrackerScope.ManagesPeople(actor))
        {
            throw Missing();
        }

        if (proposal.Status != ProposalStatus.Pending)
        {
            throw Conflict();
        }

        ProposedPerson original = Deserialize(proposal.Payload);
        ProposedPerson details = request.Edited is null ? original : request.Edited with { FirstName = request.Edited.FirstName.Trim() };
        bool edited = details != original;
        if (string.IsNullOrWhiteSpace(details.FirstName))
        {
            throw Invalid();
        }

        proposal.Status = request.Approve ? ProposalStatus.Approved : ProposalStatus.Rejected;
        proposal.DecidedAt = now;
        proposal.DecidedByUserId = actor.UserId;
        string source = proposal.AiGenerated ? "AI suggestion" : "duplicate check";

        if (!request.Approve)
        {
            Audit(actor, now, "proposal.rejected", $"Rejected {source}: {proposal.Title}", proposal.AiGenerated, proposalId: proposal.Id);
        }
        else if (proposal.Kind == ProposalKind.NewPerson || (proposal.Kind == ProposalKind.MatchExisting && request.AsNewPerson))
        {
            Person person = AddPerson(details, actor, now);
            proposal.ResultPersonId = person.Id;
            Audit(actor, now, "person.added", $"Approved {source}: added {person.Name}" + (details.IsMinor ? " (under 18, no phone stored)" : string.Empty),
                proposal.AiGenerated, edited, proposal.Id, person.Id);
        }
        else if (proposal.Kind == ProposalKind.MatchExisting)
        {
            Person target = await db.People.SingleAsync(p => p.Id == proposal.TargetPersonId, ct);
            await ApplyDetailsAsync(target, details, ct);
            // The approver met them too; their meeting joins the shared history.
            db.PocTouchpoints.Add(new PocTouchpoint { PersonId = target.Id, UserId = actor.UserId, At = proposal.CreatedAt, Kind = TouchpointKind.Met, Note = Truncate(Trim(details.HowWeMet), 500) });
            proposal.ResultPersonId = target.Id;
            Audit(actor, now, "person.updated", $"Approved {source}: updated {target.Name} instead of adding a duplicate", proposal.AiGenerated, edited, proposal.Id, target.Id);
        }
        else
        {
            Person target = await db.People.SingleAsync(p => p.Id == proposal.TargetPersonId, ct);
            Person duplicate = await db.People.SingleAsync(p => p.Id == proposal.DuplicatePersonId, ct);
            PocPersonProfile? duplicateProfile = await db.PocPersonProfiles.SingleOrDefaultAsync(p => p.PersonId == duplicate.Id, ct);
            await ApplyDetailsAsync(target, new ProposedPerson(target.FirstName, target.LastName, duplicate.Phone, duplicate.Email, duplicateProfile?.Dorm,
                duplicateProfile?.ClassYear, duplicateProfile?.HowWeMet, duplicateProfile?.IsMinor ?? false, duplicateProfile?.NextStep, duplicateProfile?.Notes), ct);
            // POC: the duplicate is archived, not deleted, so its history stays readable.
            duplicate.IsActive = false;
            duplicate.Version = Guid.NewGuid();
            proposal.ResultPersonId = target.Id;
            Audit(actor, now, "person.merged", $"Merged {duplicate.Name} into {target.Name}; {duplicate.Name} archived", false, edited, proposal.Id, target.Id);
        }

        if (proposal.CaptureId is { } captureId
            && !await db.PocProposals.AnyAsync(p => p.CaptureId == captureId && p.Status == ProposalStatus.Pending && p.Id != proposal.Id, ct))
        {
            await db.PocCaptures.Where(c => c.Id == captureId).ExecuteUpdateAsync(c => c.SetProperty(x => x.Status, CaptureStatus.Done), ct);
        }

        await db.SaveChangesAsync(ct);
        return (await ResponsesAsync([proposal], ct)).Single();
    }

    public async Task<SensitiveNoteResponse[]> InboxAsync(CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        List<PocSensitiveNote> notes = await db.PocSensitiveNotes.OrderBy(n => n.ReviewedAt != null).ThenByDescending(n => n.CreatedAt).Take(50).ToListAsync(ct);
        Dictionary<string, string> names = await UserNamesAsync(ct);
        return [.. notes.Select(n => new SensitiveNoteResponse(n.Id, n.OriginalText, n.Categories, names.GetValueOrDefault(n.CreatedByUserId, "A leader"), n.CreatedAt, n.ReviewedAt is not null))];
    }

    public async Task MarkReviewedAsync(Guid id, CancellationToken ct)
    {
        ChurchActor actor = await scope.ActorAsync(ct);
        PocSensitiveNote note = await db.PocSensitiveNotes.SingleOrDefaultAsync(n => n.Id == id, ct) ?? throw Missing();
        note.ReviewedAt = scope.Now;
        note.ReviewedByUserId = actor.UserId;
        Audit(actor, scope.Now, "inbox.reviewed", "Reviewed a sensitive note in the pastor inbox");
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuditEntryResponse[]> AuditLogAsync(CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        List<PocAuditEntry> entries = await db.PocAuditEntries.OrderByDescending(a => a.At).Take(100).ToListAsync(ct);
        Dictionary<string, string> names = await UserNamesAsync(ct);
        return [.. entries.Select(a => new AuditEntryResponse(a.Id, a.At, a.ActorUserId is null ? "System" : names.GetValueOrDefault(a.ActorUserId, "Someone"),
            a.Action, a.Summary, a.AiInvolved, a.HumanEdited))];
    }

    private Person AddPerson(ProposedPerson details, ChurchActor actor, DateTimeOffset now)
    {
        Person person = new()
        {
            FirstName = Truncate(details.FirstName.Trim(), 100) ?? details.FirstName,
            LastName = Truncate(Trim(details.LastName), 100),
            // A minor's own phone is never stored; contact goes through the guardian.
            Phone = details.IsMinor ? null : Truncate(Trim(details.Phone), 50),
            Email = details.IsMinor ? null : Truncate(Trim(details.Email), 320),
            CreatedAt = now,
            CreatedByUserId = actor.UserId,
        };
        db.People.Add(person);
        // Meeting them is the first touchpoint; the 48-hour first-contact nudge counts from here.
        db.PocTouchpoints.Add(new PocTouchpoint { PersonId = person.Id, UserId = actor.UserId, At = now, Kind = TouchpointKind.Met, Note = Truncate(Trim(details.HowWeMet), 500) });
        db.PocPersonProfiles.Add(new PocPersonProfile
        {
            PersonId = person.Id,
            Source = PersonSource.LeaderEntered,
            Dorm = Truncate(Trim(details.Dorm), 100),
            ClassYear = Truncate(Trim(details.ClassYear), 20),
            HowWeMet = Truncate(Trim(details.HowWeMet), 200),
            IsMinor = details.IsMinor,
            NextStep = details.IsMinor ? null : Truncate(Trim(details.NextStep), 300),
            Notes = Truncate(Trim(details.Notes), 1000),
        });
        return person;
    }

    /// <summary>Fills gaps on an existing person without overwriting what is already known.</summary>
    private async Task ApplyDetailsAsync(Person target, ProposedPerson details, CancellationToken ct)
    {
        PocPersonProfile? profile = await db.PocPersonProfiles.SingleOrDefaultAsync(p => p.PersonId == target.Id, ct);
        if (profile is null)
        {
            profile = new PocPersonProfile { PersonId = target.Id, Source = PersonSource.LeaderEntered };
            db.PocPersonProfiles.Add(profile);
        }

        if (!profile.IsMinor)
        {
            target.Phone ??= Truncate(Trim(details.Phone), 50);
            target.Email ??= Truncate(Trim(details.Email), 320);
            profile.NextStep = Truncate(Trim(details.NextStep), 300) ?? profile.NextStep;
        }

        profile.Dorm ??= Truncate(Trim(details.Dorm), 100);
        profile.ClassYear ??= Truncate(Trim(details.ClassYear), 20);
        profile.HowWeMet ??= Truncate(Trim(details.HowWeMet), 200);
        if (Trim(details.Notes) is { } notes && profile.Notes?.Contains(notes, StringComparison.OrdinalIgnoreCase) != true)
        {
            profile.Notes = Truncate(profile.Notes is null ? notes : $"{profile.Notes}\n{notes}", 1000);
        }

        target.Version = Guid.NewGuid();
    }

    private async Task<List<(Person, PocPersonProfile?)>> ExistingPeopleAsync(Guid? excluding, CancellationToken ct)
    {
        var rows = await (
            from p in db.People.Where(p => p.IsActive && p.UserId == null && p.Id != excluding)
            join pr in db.PocPersonProfiles on p.Id equals pr.PersonId into profiles
            from pr in profiles.DefaultIfEmpty()
            select new { Person = p, Profile = pr }).ToListAsync(ct);
        return [.. rows.Select(r => (r.Person, (PocPersonProfile?)r.Profile))];
    }

    private async Task<CaptureResponse[]> CapturesAsync(ChurchActor actor, IQueryable<PocCapture> query, CancellationToken ct)
    {
        List<PocCapture> captures = await query.ToListAsync(ct);
        Guid[] ids = [.. captures.Select(c => c.Id)];
        List<PocProposal> proposals = await db.PocProposals.Where(p => p.CaptureId != null && ids.Contains(p.CaptureId.Value)).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        ProposalResponse[] responses = await ResponsesAsync(proposals, ct);
        // Leaders see which kinds of detail were held back, never the held-back text itself.
        var categories = await db.PocSensitiveNotes.Where(n => ids.Contains(n.CaptureId)).Select(n => new { n.CaptureId, n.Categories }).ToListAsync(ct);
        return [.. captures.Select(c => new CaptureResponse(c.Id, c.Kind, c.Status, c.Text, c.Redacted,
            [.. categories.Where(n => n.CaptureId == c.Id).SelectMany(n => n.Categories.Split(", "))],
            c.AiLive, c.Summary, c.CreatedAt, [.. responses.Where(r => r.CaptureId == c.Id)], null))];
    }

    private async Task<ProposalResponse[]> ResponsesAsync(IReadOnlyList<PocProposal> proposals, CancellationToken ct)
    {
        Guid[] personIds = [.. proposals.SelectMany(p => new[] { p.TargetPersonId, p.DuplicatePersonId }).OfType<Guid>().Distinct()];
        var people = await (
            from p in db.People.Where(p => personIds.Contains(p.Id))
            join pr in db.PocPersonProfiles on p.Id equals pr.PersonId into profiles
            from pr in profiles.DefaultIfEmpty()
            select new { p.Id, p.FirstName, p.LastName, p.CreatedByUserId, Profile = pr }).ToListAsync(ct);
        Dictionary<string, string> names = await UserNamesAsync(ct);

        PersonSummary? Summary(Guid? id) => people.FirstOrDefault(p => p.Id == id) is { } p
            ? new PersonSummary(p.Id, p.FirstName + (p.LastName is null ? string.Empty : " " + p.LastName), p.Profile?.Source, p.Profile?.Dorm,
                p.Profile?.ClassYear, p.Profile?.HowWeMet, p.CreatedByUserId is null ? null : names.GetValueOrDefault(p.CreatedByUserId))
            : null;

        return [.. proposals.Select(p => new ProposalResponse(p.Id, p.Kind, p.Status, p.Title, Deserialize(p.Payload),
            p.Reasons.Split('\n', StringSplitOptions.RemoveEmptyEntries), p.SourceExcerpt, Summary(p.TargetPersonId), Summary(p.DuplicatePersonId),
            p.AiGenerated, p.CreatedAt, names.GetValueOrDefault(p.OwnerUserId, "A leader"), p.CaptureId, p.ResultPersonId))];
    }

    /// <summary>Leaders' display names, from the Person linked to each account.</summary>
    private async Task<Dictionary<string, string>> UserNamesAsync(CancellationToken ct) =>
        (await db.People.Where(p => p.UserId != null)
                .Select(p => new { UserId = p.UserId ?? string.Empty, Name = p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName) })
                .ToListAsync(ct))
            .DistinctBy(p => p.UserId)
            .ToDictionary(p => p.UserId, p => p.Name);

    private void Audit(ChurchActor actor, DateTimeOffset at, string action, string summary, bool aiInvolved = false, bool humanEdited = false, Guid? proposalId = null, Guid? personId = null) =>
        db.PocAuditEntries.Add(new PocAuditEntry
        {
            At = at,
            ActorUserId = actor.UserId,
            Action = action,
            Summary = Truncate(summary, 500) ?? summary,
            AiInvolved = aiInvolved,
            HumanEdited = humanEdited,
            ProposalId = proposalId,
            PersonId = personId,
        });

    private static ProposedPerson Normalize(ExtractedPerson found)
    {
        string? classYear = ClassYears.FirstOrDefault(y => string.Equals(y, found.ClassYear?.Trim(), StringComparison.OrdinalIgnoreCase));
        string? dorm = DemoData.Dorms.FirstOrDefault(d => string.Equals(d, found.Dorm?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Trim(found.Dorm);
        return new ProposedPerson(found.FirstName.Trim(), Trim(found.LastName), found.MinorSuspected ? null : Trim(found.Phone),
            found.MinorSuspected ? null : Trim(found.Email), dorm, classYear, Trim(found.HowWeMet), found.MinorSuspected,
            found.MinorSuspected ? null : Trim(found.NextStep), Trim(found.Notes));
    }

    private static string ExtractionPrompt() => $"""
        You help volunteer leaders in a college campus ministry keep track of people they meet. A leader wrote or dictated
        the note below after meeting people. Extract each person the leader met or talked about meeting.

        Rules:
        - Only include people the note says the leader met, talked with, or wants to follow up with. Skip the leader
          themself, other leaders, staff, and famous or fictional people.
        - Never invent details. Leave a field null unless the note states it.
        - phone and email exactly as written.
        - dorm: one of {string.Join(", ", DemoData.Dorms)} when the note names one of them; otherwise null.
        - classYear: one of Freshman, Sophomore, Junior, Senior, Grad, or null.
        - howWeMet: a few words on where or how they met, such as "Welcome week BBQ".
        - minorSuspected: true if anything suggests they are under 18 (high school, an age under 18, a parent dropping
          them off). When true, set phone, email and nextStep to null.
        - nextStep: one short, concrete thing the leader could do next, written as an instruction to the leader, such
          as "Text him about Tuesday's guys' group". Null if nothing fits.
        - notes: interests or practical needs worth remembering, such as "plays guitar; needs a ride on Sundays". Never
          include health, mental health, family crisis, legal, sexuality or money details.
        - Text in square brackets, like "{SensitiveFilter.Placeholder}", was removed on purpose. Do not guess at it.
        - sourceExcerpt: the exact words from the note this person came from.
        - summary: one friendly sentence to the leader about what you found.
        """;

    private static string Name(ProposedPerson p) => p.LastName is null ? p.FirstName : $"{p.FirstName} {p.LastName}";

    private static string Serialize(ProposedPerson person) => JsonSerializer.Serialize(person, JsonOptions);

    private static ProposedPerson Deserialize(string payload) =>
        JsonSerializer.Deserialize<ProposedPerson>(payload, JsonOptions) ?? throw new InvalidOperationException("Proposal payload is empty.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI extraction failed; falling back to the canned extractor.")]
    private static partial void LogExtractionFailed(ILogger logger, Exception exception);
}
