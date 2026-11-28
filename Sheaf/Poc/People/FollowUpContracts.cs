using Sheaf.Data.Poc;

namespace Sheaf.Poc.People;

/// <summary>Why someone needs attention now. Higher urgency sorts first.</summary>
public sealed record NudgeResponse(string Kind, string Label, int Urgency);

public sealed record TouchpointResponse(Guid Id, TouchpointKind Kind, DateTimeOffset At, string? Note, bool AiDrafted, string ByName);

public sealed record MyPersonResponse(
    Guid Id,
    string Name,
    string? Phone,
    string? Dorm,
    string? ClassYear,
    string? HowWeMet,
    PersonSource? Source,
    bool IsMinor,
    bool DoNotContact,
    string? GroupName,
    string? NextStep,
    DateTimeOffset AddedAt,
    bool AddedByMe,
    TouchpointResponse? LastTouchpoint,
    NudgeResponse? Nudge);

public sealed record GuardianResponse(Guid Id, string Name, string? Phone);

public sealed record PersonDetailResponse(
    MyPersonResponse Person,
    string? Email,
    string? Notes,
    GuardianResponse? Guardian,
    TouchpointResponse[] Touchpoints,
    int MeetingsAttended,
    int MeetingsHeld,
    string? DraftBlockedReason);

public sealed record TouchpointRequest(TouchpointKind Kind, string? Note, bool AiDrafted = false);

/// <summary>Optional steer for the draft, such as "invite to Tuesday" or "check in after exams".</summary>
public sealed record DraftRequest(string? Intent);

public sealed record DraftResponse(string Text, bool AiLive);

public sealed record DoNotContactRequest(bool DoNotContact);

public sealed record NextStepRequest(string? NextStep);
