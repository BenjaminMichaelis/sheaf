using Sheaf.Data.Poc;

namespace Sheaf.Poc.Capture;

/// <summary>A leader adding someone by hand. For a minor, the phone belongs to the guardian, never the minor.</summary>
public sealed record QuickAddRequest(
    string FirstName,
    string? LastName,
    string? Phone,
    string? HowWeMet,
    bool IsMinor,
    string? GuardianName,
    string? GuardianPhone);

/// <summary>A typed or dictated note about people the leader met.</summary>
public sealed record NoteRequest(string Text, CaptureKind Kind);

/// <summary>The details a proposal would save. Leaders can edit them before approving.</summary>
public sealed record ProposedPerson(
    string FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Dorm,
    string? ClassYear,
    string? HowWeMet,
    bool IsMinor,
    string? NextStep,
    string? Notes);

/// <summary>
/// Who an existing record is, without contact details: a Leader sees that a match exists and why, not the other
/// person's phone or email.
/// </summary>
public sealed record PersonSummary(Guid Id, string Name, PersonSource? Source, string? Dorm, string? ClassYear, string? HowWeMet, string? AddedBy);

public sealed record ProposalResponse(
    Guid Id,
    ProposalKind Kind,
    ProposalStatus Status,
    string Title,
    ProposedPerson Person,
    string[] Reasons,
    string? SourceExcerpt,
    PersonSummary? Target,
    PersonSummary? Duplicate,
    bool AiGenerated,
    DateTimeOffset CreatedAt,
    string OwnerName,
    Guid? CaptureId,
    Guid? ResultPersonId);

public sealed record CaptureResponse(
    Guid Id,
    CaptureKind Kind,
    CaptureStatus Status,
    string Text,
    bool Redacted,
    string[] RedactedCategories,
    bool AiLive,
    string? Summary,
    DateTimeOffset CreatedAt,
    ProposalResponse[] Proposals,
    Guid? PersonId);

/// <summary>
/// Approve or reject a proposal. <paramref name="Edited"/> replaces the proposed details. For a possible match,
/// <paramref name="AsNewPerson"/> adds them as someone new instead of updating the existing record.
/// </summary>
public sealed record ProposalDecisionRequest(bool Approve, ProposedPerson? Edited = null, bool AsNewPerson = false);

public sealed record SensitiveNoteResponse(Guid Id, string OriginalText, string Categories, string LeaderName, DateTimeOffset CreatedAt, bool Reviewed);

public sealed record AuditEntryResponse(Guid Id, DateTimeOffset At, string ActorName, string Action, string Summary, bool AiInvolved, bool HumanEdited);

/// <summary>What Claude returns for a note. Shape is enforced with structured outputs.</summary>
public sealed record Extraction(ExtractedPerson[] People, string Summary);

public sealed record ExtractedPerson(
    string FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Dorm,
    string? ClassYear,
    string? HowWeMet,
    bool MinorSuspected,
    string? NextStep,
    string? Notes,
    string SourceExcerpt);
