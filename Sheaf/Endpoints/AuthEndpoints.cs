using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Sheaf.Authentication;
using Sheaf.Authorization;
using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Endpoints;

internal static class AuthEndpoints
{
    private static readonly string[] PasswordMismatchErrors = ["Passwords do not match."];

    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async Task<Results<Ok<UserInfo>, UnauthorizedHttpResult>> (
            LoginRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                request.RememberMe ?? false,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return TypedResults.Ok(await CreateUserInfoAsync(user, churchAccess, currentChurch, cancellationToken));
            }

            return TypedResults.Unauthorized();
        })
        .WithName("Login")
        .AllowAnonymous()
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/register", async Task<Results<Ok<UserInfo>, ValidationProblem>> (
            RegisterRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken) =>
        {
            if (request.Password != request.ConfirmPassword)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(RegisterRequest.ConfirmPassword)] = PasswordMismatchErrors
                });
            }

            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                // Automatically verify the email.
                // TODO: Implement email verification
                string token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                result = await userManager.ConfirmEmailAsync(user, token);

                if (result.Succeeded)
                {
                    await signInManager.SignInAsync(user, isPersistent: false);
                    return TypedResults.Ok(await CreateUserInfoAsync(user, churchAccess, currentChurch, cancellationToken));
                }
            }

            return TypedResults.ValidationProblem(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()));
        })
        .WithName("Register")
        .AllowAnonymous()
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(SheafPolicies.Authenticated)
        .WithName("Logout")
        .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/church/{churchId}", async Task<Results<Ok<UserInfo>, ProblemHttpResult, UnauthorizedHttpResult>> (
            Guid churchId, ClaimsPrincipal user, SignInManager<ApplicationUser> signInManager,
            IChurchAccessService churchAccess, ICurrentChurch currentChurch,
            ChurchClaimsPrincipalFactory principalFactory, CancellationToken cancellationToken) =>
        {
            if (!signInManager.IsSignedIn(user))
            {
                return TypedResults.Unauthorized();
            }

            ApplicationUser? verifiedUser = await signInManager.ValidateSecurityStampAsync(user);
            if (verifiedUser is null)
            {
                await signInManager.SignOutAsync();
                return TypedResults.Unauthorized();
            }

            ChurchSwitchResult result = await churchAccess.SwitchChurchAsync(verifiedUser, churchId, cancellationToken);
            if (result.Failure == ChurchSwitchFailure.TenantedEntitiesTracked)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://sheaf.dev/problems/church-switch-tracked",
                    title: "Church switch could not be completed.",
                    detail: "Complete or discard pending church changes before switching.");
            }

            if (result.Failure == ChurchSwitchFailure.ChurchNotAccessible || result.Selection is null)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    type: "https://sheaf.dev/problems/church-access-denied",
                    title: "Church access denied.");
            }

            UserInfo info = await CreateUserInfoAsync(
                verifiedUser, churchAccess, currentChurch, cancellationToken, result.Selection);
            principalFactory.SwitchSelection = result.Selection;
            try
            {
                await signInManager.RefreshSignInAsync(verifiedUser);
            }
            finally
            {
                principalFactory.SwitchSelection = null;
            }

            return TypedResults.Ok(info);
        })
        .RequireAuthorization(SheafPolicies.Authenticated)
        .WithMetadata(new ChurchSwitchEndpointMetadata())
        .WithName("SwitchChurch")
        .WithSummary("Switches the active church for the authenticated account.")
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/user", async (
            ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
            IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return TypedResults.Ok(new UserInfo { IsAuthenticated = false });
            }

            var appUser = await userManager.GetUserAsync(user);

            return TypedResults.Ok(appUser is null
                ? new UserInfo { IsAuthenticated = false }
                : await CreateUserInfoAsync(appUser, churchAccess, currentChurch, cancellationToken));
        })
        .WithName("GetCurrentUser")
        .AllowAnonymous()
        .WithSummary("Get the signed-in account, its churches and its current church selection.")
        .Produces<UserInfo>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<UserInfo> CreateUserInfoAsync(
        ApplicationUser user, IChurchAccessService churchAccess,
        ICurrentChurch currentChurch, CancellationToken cancellationToken, ChurchSelection? currentSelection = null)
    {
        IReadOnlyList<ChurchInfo> churches = await churchAccess.GetChurchesForUserAsync(user, cancellationToken);
        Guid? currentChurchId = currentSelection?.ChurchId ?? currentChurch.ChurchId;
        ChurchInfo? selection = churches.SingleOrDefault(church => church.ChurchId == currentChurchId);
        if (selection is not null && currentSelection is not null)
        {
            selection = selection with { Role = currentSelection.Role };
        }

        return new UserInfo
        {
            UserId = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            IsAuthenticated = true,
            Churches = churches,
            CurrentChurch = selection
        };
    }
}

/// <summary>Credentials for a fresh Identity sign-in.</summary>
public sealed record LoginRequest(string Email, string Password, bool? RememberMe = null);
/// <summary>Credentials for a new Identity account.</summary>
public sealed record RegisterRequest(string Email, string Password, string ConfirmPassword);
/// <summary>The authenticated account and its available and currently selected church.</summary>
public sealed record UserInfo
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Email { get; init; } = "";
    public bool IsAuthenticated { get; init; }
    public IReadOnlyList<ChurchInfo> Churches { get; init; } = [];
    public ChurchInfo? CurrentChurch { get; init; }
}
