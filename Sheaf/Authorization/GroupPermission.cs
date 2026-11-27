using System.Collections.Frozen;

using Sheaf.Data;

namespace Sheaf.Authorization;

/// <summary>
/// What a person's current <see cref="GroupRole"/> lets their account do in that one group. Church-wide permissions
/// (<see cref="Permission"/>) are checked on endpoints; these depend on the group being accessed, so
/// <see cref="Services.TrackerScope"/> checks them once the group is known. <see cref="Permission.TrackerManage"/> holders
/// may do all of these in every group.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Matches Permission.")]
public enum GroupPermission
{
    /// <summary>See the group, its roster history, and its meetings.</summary>
    View,
    /// <summary>Add people to the group as Members and end their membership.</summary>
    ManageMembers,
    /// <summary>Record the group's meetings and who came.</summary>
    RecordAttendance
}

/// <summary>
/// Maps each group role to what it may do in its group. Member grants nothing: being on a roster never gives an
/// account access. Assigning or changing Leaders is church-wide (<see cref="Permission.TrackerManage"/>), not listed here.
/// </summary>
public static class GroupRolePermissions
{
    private static readonly FrozenDictionary<GroupRole, FrozenSet<GroupPermission>> Map =
        new Dictionary<GroupRole, FrozenSet<GroupPermission>>
        {
            [GroupRole.Member] = FrozenSet<GroupPermission>.Empty,
            [GroupRole.Leader] = new[] { GroupPermission.View, GroupPermission.ManageMembers, GroupPermission.RecordAttendance }.ToFrozenSet()
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<GroupPermission, GroupRole[]> RolesByPermission =
        Enum.GetValues<GroupPermission>().ToFrozenDictionary(
            permission => permission,
            permission => Map.Where(entry => entry.Value.Contains(permission)).Select(entry => entry.Key).ToArray());

    public static bool HasPermission(GroupRole role, GroupPermission permission) =>
        Map.TryGetValue(role, out FrozenSet<GroupPermission>? permissions) && permissions.Contains(permission);

    /// <summary>The roles that grant <paramref name="permission"/>, for filtering assignments in a query.</summary>
    public static IReadOnlyList<GroupRole> RolesWith(GroupPermission permission) => RolesByPermission[permission];
}
