using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Sheaf.Core.Authentication;

namespace Sheaf.Authorization;

public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

/// <summary>
/// Grants a permission from the selected membership's persisted role. The church claims only say which membership
/// was selected; the role they carry can be stale until the cookie is revalidated, so it is not trusted here.
/// </summary>
public sealed class PermissionAuthorizationHandler(IChurchMembershipRoles memberships) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.ReadChurchClaims(out ChurchSelection? selection) != ChurchClaimState.Selected
            || selection is null
            || context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId)
        {
            return;
        }

        CancellationToken ct = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        if (await memberships.GetRoleAsync(userId, selection, ct) is { } role
            && RolePermissions.HasPermission(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
