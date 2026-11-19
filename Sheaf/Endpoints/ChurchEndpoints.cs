using Sheaf.Authorization;

namespace Sheaf.Endpoints;

public static class ChurchEndpoints
{
    public static RouteGroupBuilder MapChurchGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/church").RequireAuthorization(SheafPolicies.ChurchMember);
}
