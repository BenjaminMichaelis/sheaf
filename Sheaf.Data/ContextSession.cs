using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// One meeting of a group on a local date. Who came is recorded in <see cref="Attendance"/>; who was absent is the
/// roster on that date minus the attendees, so it is never stored. A group may meet more than once on a date.
/// </summary>
public sealed class ContextSession : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid ContextId { get; set; }
    /// <summary>The meeting's calendar date in the church's time zone (see <see cref="ChurchCalendar"/>).</summary>
    public DateOnly OccurredOn { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    /// <summary>
    /// The account that recorded the meeting, or null for imports. Not a foreign key, so the audit trail survives the
    /// account being deleted.
    /// </summary>
    [MaxLength(450)]
    public string? RecordedByUserId { get; set; }
    /// <summary>Changes on every write to the session or its attendance; clients send it back as If-Match.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
