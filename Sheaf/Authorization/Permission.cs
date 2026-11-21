using System.Collections.Frozen;

using Sheaf.Data;

namespace Sheaf.Authorization;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Permission is the required domain name for this enum.")]
public enum Permission
{
    ManageMemberships,
    TrackerAccess
}

public static class RolePermissions
{
    private static readonly FrozenDictionary<ChurchMembershipRole, FrozenSet<Permission>> Map =
        new Dictionary<ChurchMembershipRole, FrozenSet<Permission>>
        {
            [ChurchMembershipRole.Admin] = new[] { Permission.ManageMemberships }.ToFrozenSet(),
            [ChurchMembershipRole.Staff] = new[] { Permission.TrackerAccess }.ToFrozenSet(),
            [ChurchMembershipRole.Leader] = new[] { Permission.TrackerAccess }.ToFrozenSet()
        }.ToFrozenDictionary();

    public static bool HasPermission(ChurchMembershipRole role, Permission permission) =>
        Map.TryGetValue(role, out FrozenSet<Permission>? permissions) && permissions.Contains(permission);
}
