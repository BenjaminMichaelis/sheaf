using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data.Poc;

/// <summary>Where a person's record came from. Drives the consent story in the demo.</summary>
public enum PersonSource
{
    /// <summary>A leader added them after meeting in person (the default consent route).</summary>
    LeaderEntered = 0,
    /// <summary>Seeded as if imported from Planning Center. The POC does not call Planning Center.</summary>
    PlanningCenterSimulated = 1,
    /// <summary>Seeded as an existing church member.</summary>
    Seeded = 2,
}

/// <summary>
/// POC-only details hung off <see cref="Person"/> so the shared Person table stays as it is on main.
/// One row per person.
/// </summary>
public sealed class PocPersonProfile : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Guid PersonId { get; set; }

    [MaxLength(100)]
    public string? Dorm { get; set; }

    /// <summary>Freshman, Sophomore, Junior, Senior, Grad, or null for non-students.</summary>
    [MaxLength(20)]
    public string? ClassYear { get; set; }

    [MaxLength(200)]
    public string? HowWeMet { get; set; }

    public PersonSource Source { get; set; }

    /// <summary>Under 18: no phone stored, no AI drafts, contact goes through the guardian.</summary>
    public bool IsMinor { get; set; }

    public Guid? GuardianPersonId { get; set; }

    /// <summary>Set by "Don't contact". Blocks drafts and nudges for every leader.</summary>
    public DateTimeOffset? DoNotContactAt { get; set; }

    [MaxLength(450)]
    public string? DoNotContactByUserId { get; set; }

    /// <summary>What the leader plans to do next, usually from an approved AI proposal.</summary>
    [MaxLength(300)]
    public string? NextStep { get; set; }

    /// <summary>Interests and needs worth remembering, such as "plays guitar" or "needs a ride on Sundays".</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public enum TouchpointKind
{
    Met = 0,
    Texted = 1,
    Called = 2,
    Coffee = 3,
    SawThem = 4,
    Other = 5,
}

/// <summary>A leader reaching out to or spending time with someone. Drives "last touchpoint" and follow-up nudges.</summary>
public sealed class PocTouchpoint : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Guid PersonId { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    public DateTimeOffset At { get; set; }
    public TouchpointKind Kind { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>The message sent was drafted by AI (and reviewed by the leader before sending).</summary>
    public bool AiDrafted { get; set; }
}

public enum CaptureKind
{
    QuickAdd = 0,
    Note = 1,
    Voice = 2,
}

public enum CaptureStatus
{
    /// <summary>Proposals are waiting for review.</summary>
    Ready = 0,
    /// <summary>Every proposal has been decided, or none were needed.</summary>
    Done = 1,
    /// <summary>The AI call failed; the leader can retry.</summary>
    Failed = 2,
}

/// <summary>Something a leader wrote, said or quick-added after meeting people. Text is stored after redaction.</summary>
public sealed class PocCapture : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public CaptureKind Kind { get; set; }
    public CaptureStatus Status { get; set; }

    /// <summary>The note as Claude saw it: sensitive sentences are already replaced.</summary>
    [MaxLength(4000)]
    public required string Text { get; set; }

    public bool Redacted { get; set; }

    /// <summary>Whether Claude (rather than the canned fallback) read the note.</summary>
    public bool AiLive { get; set; }

    [MaxLength(500)]
    public string? Summary { get; set; }
}

/// <summary>The original wording of a note that mentioned something sensitive. Only Staff can read it.</summary>
public sealed class PocSensitiveNote : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Guid CaptureId { get; set; }

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(4000)]
    public required string OriginalText { get; set; }

    /// <summary>Comma-separated categories the filter matched, such as "health, mental health".</summary>
    [MaxLength(200)]
    public required string Categories { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }
}

public enum ProposalKind
{
    /// <summary>Add someone new from a note.</summary>
    NewPerson = 0,
    /// <summary>A note mentions someone who may already be in the church; update them instead of adding.</summary>
    MatchExisting = 1,
    /// <summary>Two existing records look like the same person.</summary>
    MergeDuplicates = 2,
}

public enum ProposalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>A change suggested by AI or by the duplicate check. Nothing happens until a person approves it.</summary>
public sealed class PocProposal : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public Guid? CaptureId { get; set; }

    /// <summary>The leader whose review queue shows it.</summary>
    [MaxLength(450)]
    public required string OwnerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public ProposalKind Kind { get; set; }
    public ProposalStatus Status { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>The proposed person details as JSON (<c>ProposedPerson</c>).</summary>
    [MaxLength(4000)]
    public required string Payload { get; set; }

    /// <summary>Why it was suggested, one reason per line.</summary>
    [MaxLength(1000)]
    public required string Reasons { get; set; }

    /// <summary>The words in the note the suggestion came from.</summary>
    [MaxLength(1000)]
    public string? SourceExcerpt { get; set; }

    /// <summary>For a match or merge: the existing person it would update.</summary>
    public Guid? TargetPersonId { get; set; }

    /// <summary>For a merge: the record that would be folded into the target.</summary>
    public Guid? DuplicatePersonId { get; set; }

    public bool AiGenerated { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    /// <summary>The person created or updated when it was approved.</summary>
    public Guid? ResultPersonId { get; set; }
}

/// <summary>One line in the audit log: what AI proposed and what a person did about it.</summary>
public sealed class PocAuditEntry : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }
    public DateTimeOffset At { get; set; }

    [MaxLength(450)]
    public string? ActorUserId { get; set; }

    [MaxLength(60)]
    public required string Action { get; set; }

    [MaxLength(500)]
    public required string Summary { get; set; }

    /// <summary>Whether AI produced the content this entry is about.</summary>
    public bool AiInvolved { get; set; }

    /// <summary>Whether a person changed the AI's suggestion before approving it.</summary>
    public bool HumanEdited { get; set; }

    public Guid? ProposalId { get; set; }
    public Guid? PersonId { get; set; }
}

public static class PocModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<PocPersonProfile>(entity =>
        {
            entity.ToTable("PocPersonProfiles");
            entity.HasOne<Church>().WithMany().HasForeignKey(p => p.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(p => p.GuardianPersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => new { p.ChurchId, p.PersonId }).IsUnique();
        });

        builder.Entity<PocTouchpoint>(entity =>
        {
            entity.ToTable("PocTouchpoints");
            entity.HasOne<Church>().WithMany().HasForeignKey(t => t.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Person>().WithMany().HasForeignKey(t => t.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(t => new { t.ChurchId, t.PersonId, t.At });
        });

        builder.Entity<PocCapture>(entity =>
        {
            entity.ToTable("PocCaptures");
            entity.HasOne<Church>().WithMany().HasForeignKey(c => c.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasIndex(c => new { c.ChurchId, c.CreatedByUserId, c.CreatedAt });
        });

        builder.Entity<PocSensitiveNote>(entity =>
        {
            entity.ToTable("PocSensitiveNotes");
            entity.HasOne<Church>().WithMany().HasForeignKey(n => n.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<PocCapture>().WithMany().HasForeignKey(n => n.CaptureId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PocProposal>(entity =>
        {
            entity.ToTable("PocProposals");
            entity.HasOne<Church>().WithMany().HasForeignKey(p => p.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<PocCapture>().WithMany().HasForeignKey(p => p.CaptureId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => new { p.ChurchId, p.OwnerUserId, p.Status });
        });

        builder.Entity<PocAuditEntry>(entity =>
        {
            entity.ToTable("PocAuditEntries");
            entity.HasOne<Church>().WithMany().HasForeignKey(a => a.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasIndex(a => new { a.ChurchId, a.At });
        });
    }
}
