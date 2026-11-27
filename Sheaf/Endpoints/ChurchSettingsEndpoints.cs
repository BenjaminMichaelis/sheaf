using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;
using Sheaf.Services;

namespace Sheaf.Endpoints;

/// <summary>Church-wide settings for the selected church. Any member reads them; only Admins change them.</summary>
public static class ChurchSettingsEndpoints
{
    public static void MapChurchSettings(this IEndpointRouteBuilder app)
    {
        var church = app.MapChurchGroup().WithTags("Church");

        church.MapGet("/settings", async Task<Results<Ok<ChurchSettingsResponse>, NotFound>> (ICurrentChurch current, ApplicationDbContext db, CancellationToken ct) =>
                await db.Churches.AsNoTracking().SingleOrDefaultAsync(c => c.Id == current.ChurchId, ct) is { } settings
                    ? TypedResults.Ok(Response(settings))
                    : TypedResults.NotFound())
            .WithName("GetChurchSettings")
            .WithSummary("Get the selected church's name, the time zone its meeting dates are kept in, and its group labels");

        // ChurchSettingsRequest validates itself (IValidatableObject); invalid requests get a 400 ValidationProblem before this runs.
        church.MapPut("/settings", async Task<Results<Ok<ChurchSettingsResponse>, NotFound>> (ChurchSettingsRequest request, ICurrentChurch current, ApplicationDbContext db, CancellationToken ct) =>
            {
                if (await db.Churches.SingleOrDefaultAsync(c => c.Id == current.ChurchId, ct) is not { } settings)
                    return TypedResults.NotFound();
                // Changing the zone re-reads every stored meeting date in the new zone; dates themselves do not move.
                settings.TimeZoneId = request.TimeZoneId;
                settings.GroupLabel = TrackerRules.Trimmed(request.GroupLabel);
                settings.GroupLabelPlural = TrackerRules.Trimmed(request.GroupLabelPlural);
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(Response(settings));
            })
            .WithName("UpdateChurchSettings")
            .WithSummary("Admins set the church's time zone and its own words for a group; omitted labels reset to Group/Groups")
            .RequirePermission(Permission.ManageChurch)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static ChurchSettingsResponse Response(Church church) => new(church.Name, church.TimeZoneId,
        church.GroupLabel ?? Church.DefaultGroupLabel, church.GroupLabelPlural ?? Church.DefaultGroupLabelPlural);
}

/// <summary>
/// Church-wide settings an Admin may change. The whole set is replaced: omitted group labels go back to the defaults.
/// Labels are trimmed, and a blank label counts as omitted.
/// </summary>
public sealed record ChurchSettingsRequest(string TimeZoneId, string? GroupLabel = null, string? GroupLabelPlural = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ChurchCalendar.IsValidTimeZone(TimeZoneId))
            yield return new("Use an IANA time zone name, such as America/Chicago.", [nameof(TimeZoneId)]);
        var label = TrackerRules.Trimmed(GroupLabel);
        var plural = TrackerRules.Trimmed(GroupLabelPlural);
        // Both words or neither, so a church never ends up with "Village" next to "Groups".
        if (label is null && plural is not null)
            yield return new("Give the singular label too, or neither label.", [nameof(GroupLabel)]);
        if (plural is null && label is not null)
            yield return new("Give the plural label too, or neither label.", [nameof(GroupLabelPlural)]);
        // Measured after trimming, which is why this is not a [StringLength] attribute.
        if (label?.Length > Church.MaxLabelLength)
            yield return new($"Use at most {Church.MaxLabelLength} characters.", [nameof(GroupLabel)]);
        if (plural?.Length > Church.MaxLabelLength)
            yield return new($"Use at most {Church.MaxLabelLength} characters.", [nameof(GroupLabelPlural)]);
    }
}

/// <summary>
/// The selected church's name, the IANA time zone its meeting dates are kept in, and the words it uses for one group
/// and several groups (defaults "Group" and "Groups").
/// </summary>
public sealed record ChurchSettingsResponse(string Name, string TimeZoneId, string GroupLabel, string GroupLabelPlural);
