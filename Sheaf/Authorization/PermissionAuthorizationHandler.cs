using Microsoft.AspNetCore.Authorization;

using Sheaf.Core.Authentication;

namespace Sheaf.Authorization;

public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.ReadChurchClaims(out ChurchSelection? selection) == ChurchClaimState.Selected
            && selection is not null
            && RolePermissions.HasPermission(selection.Role, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
