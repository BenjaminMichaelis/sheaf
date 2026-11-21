using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public sealed class Context : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }
    public ContextKind Kind { get; private set; } = ContextKind.Group;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public Guid? GroupTypeId { get; set; }
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}

public enum ContextKind
{
    Group = 0
}
