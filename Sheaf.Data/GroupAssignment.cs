using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// A person's place in a group with one <see cref="Role"/> over a dated interval. A person has at most one assignment in
/// a group at any instant, so changing role ends the current row and starts a new one, and the history keeps when each
/// role began and ended. Every role is on the group's roster.
/// </summary>
public sealed class GroupAssignment : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid GroupId { get; set; }
    public Guid PersonId { get; set; }
    public GroupRole Role { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}

/// <summary>
/// A person's role within one group. What each role may do in its group is mapped in
/// <c>Sheaf.Authorization.GroupRolePermissions</c>; Member grants nothing.
/// </summary>
public enum GroupRole
{
    Member = 0,
    Leader = 1
}
