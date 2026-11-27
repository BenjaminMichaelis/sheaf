using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public sealed class GroupType : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>
    /// A retired type is kept on the groups that already have it, so their history is unchanged, but it cannot be given
    /// to a group again.
    /// </summary>
    public bool IsRetired { get; set; }
}
