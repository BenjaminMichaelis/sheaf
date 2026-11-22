namespace Sheaf.Authorization;

public static class SheafPolicies
{
    public const string Authenticated = nameof(Authenticated);
    public const string ChurchMember = nameof(ChurchMember);
    public const string TrackerActor = nameof(TrackerActor);
    public const string TrackerStaff = nameof(TrackerStaff);
    public const string PermissionPrefix = "Permission:";

    public static string ForPermission(Permission permission) => PermissionPrefix + permission;
}
