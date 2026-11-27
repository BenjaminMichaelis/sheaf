using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// A group people belong to and lead over dated intervals (<see cref="GroupAssignment"/>) and whose meetings are recorded
/// (<see cref="GroupMeeting"/>). Ending a group keeps its history.
/// </summary>
public sealed class Group : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public Guid? GroupTypeId { get; set; }
    /// <summary>Changes on every write to the group or its assignments, so assigning serializes against ending it.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
