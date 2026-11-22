using Microsoft.AspNetCore.Authorization;

using Sheaf.Data;

namespace Sheaf.Authorization;

public sealed record TrackerRoleRequirement(bool StaffOnly) : IAuthorizationRequirement;

public sealed class TrackerRoleAuthorizationHandler : AuthorizationHandler<TrackerRoleRequirement, ChurchActor>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TrackerRoleRequirement requirement, ChurchActor resource)
    {
        if (resource.Role == ChurchMembershipRole.Staff
            || (!requirement.StaffOnly && resource.Role == ChurchMembershipRole.Leader))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
