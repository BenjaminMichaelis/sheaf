using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

namespace Sheaf.Endpoints;

/// <summary>Church-wide settings for the selected church. Any member reads them; only Admins change them.</summary>
public static class ChurchSettingsEndpoints
{
    public static void MapChurchSettings(this IEndpointRouteBuilder app)
    {
        var church = app.MapChurchGroup().WithTags("Church");

        church.MapGet("/settings", async Task<Results<Ok<ChurchSettingsResponse>, NotFound>> (ICurrentChurch current, ApplicationDbContext db, CancellationToken ct) =>
                await FindAsync(current, db, ct) is { } settings ? TypedResults.Ok(Response(settings)) : TypedResults.NotFound())
            .WithName("GetChurchSettings")
            .WithSummary("Get the selected church's name and the time zone its meeting dates are kept in");

        church.MapPut("/settings", async Task<Results<Ok<ChurchSettingsResponse>, ValidationProblem, NotFound>> (ChurchSettingsRequest request, ICurrentChurch current, ApplicationDbContext db, CancellationToken ct) =>
            {
                if (!ChurchCalendar.IsValidTimeZone(request.TimeZoneId))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(ChurchSettingsRequest.TimeZoneId)] = ["Use an IANA time zone name, such as America/Chicago."]
                    });
                }
                if (await FindAsync(current, db, ct) is not { } settings)
                    return TypedResults.NotFound();
                // Changing the zone re-reads every stored meeting date in the new zone; dates themselves do not move.
                settings.TimeZoneId = request.TimeZoneId;
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(Response(settings));
            })
            .WithName("UpdateChurchSettings")
            .WithSummary("Admins set the IANA time zone the church's meeting dates are kept in")
            .RequirePermission(Permission.ManageChurch)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static Task<Church?> FindAsync(ICurrentChurch current, ApplicationDbContext db, CancellationToken ct) =>
        db.Churches.SingleOrDefaultAsync(c => c.Id == current.ChurchId, ct);

    private static ChurchSettingsResponse Response(Church church) => new(church.Name, church.TimeZoneId);
}

/// <summary>Church-wide settings an Admin may change.</summary>
public sealed record ChurchSettingsRequest(string TimeZoneId);

/// <summary>The selected church's name and the IANA time zone its meeting dates are kept in.</summary>
public sealed record ChurchSettingsResponse(string Name, string TimeZoneId);
