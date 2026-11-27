using System.Collections.Frozen;

using Sheaf.Data;

namespace Sheaf.Authorization;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Permission is the required domain name for this enum.")]
public enum Permission
{
    /// <summary>Decide which accounts can use the church and with which role.</summary>
    ManageMemberships,
    /// <summary>Use the tracker, limited to the groups and people the caller's own assignments reach.</summary>
    TrackerAccess,
    /// <summary>Manage every group: group lifecycle, group types, and leadership.</summary>
    TrackerManage,
    /// <summary>Manage every person: create and edit anyone, activate or deactivate, and resolve duplicates.</summary>
    PeopleManage,
    /// <summary>Connect, inspect, and disconnect external systems such as Planning Center.</summary>
    ManageIntegrations
}

/// <summary>
/// Maps each role to its permissions. Roles form a ladder: an account holds one role per church, so each role
/// includes everything the role below it can do.
/// </summary>
public static class RolePermissions
{
    private static readonly Permission[] Leader = [Permission.TrackerAccess];
    // Coordinator behaves like a Leader until coordinator-specific scope is defined.
    private static readonly Permission[] Coordinator = [.. Leader];
    private static readonly Permission[] Staff = [.. Coordinator, Permission.TrackerManage, Permission.PeopleManage];
    private static readonly Permission[] Admin = [.. Staff, Permission.ManageMemberships, Permission.ManageIntegrations];

    private static readonly FrozenDictionary<ChurchMembershipRole, FrozenSet<Permission>> Map =
        new Dictionary<ChurchMembershipRole, FrozenSet<Permission>>
        {
            [ChurchMembershipRole.Leader] = Leader.ToFrozenSet(),
            [ChurchMembershipRole.Coordinator] = Coordinator.ToFrozenSet(),
            [ChurchMembershipRole.Staff] = Staff.ToFrozenSet(),
            [ChurchMembershipRole.Admin] = Admin.ToFrozenSet()
        }.ToFrozenDictionary();

    public static bool HasPermission(ChurchMembershipRole role, Permission permission) =>
        Map.TryGetValue(role, out FrozenSet<Permission>? permissions) && permissions.Contains(permission);
}
