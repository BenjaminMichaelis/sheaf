using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

[Index(nameof(ChurchId), nameof(UserId), IsUnique = true)]
public class ChurchMembership : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public required string UserId { get; set; }

    public ChurchMembershipRole Role { get; set; }
}

public enum ChurchMembershipRole
{
    Leader = 0,
    Coordinator = 1,
    Staff = 2,
    Admin = 3
}
