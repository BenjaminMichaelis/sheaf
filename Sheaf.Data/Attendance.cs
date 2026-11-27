using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// A person who came to a <see cref="GroupMeeting"/>. Rows are never deleted: taking someone off the list sets
/// <see cref="RemovedAt"/>, so the record of who was marked present, and by whom, survives the edit.
/// </summary>
public sealed class Attendance : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid MeetingId { get; set; }
    public Guid PersonId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    /// <summary>The account that marked the person present, or null for imports. Not a foreign key, like <see cref="Person.CreatedByUserId"/>.</summary>
    [MaxLength(450)]
    public string? RecordedByUserId { get; set; }
    /// <summary>When the person was taken off the list, or null while they count as present.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
    /// <summary>The account that took the person off the list. Not a foreign key.</summary>
    [MaxLength(450)]
    public string? RemovedByUserId { get; set; }
}
