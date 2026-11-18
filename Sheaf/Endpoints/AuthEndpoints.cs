using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Sheaf.Data;

namespace Sheaf.Endpoints;

internal static class AuthEndpoints
{
    private static readonly string[] PasswordMismatchErrors = ["Passwords do not match."];

    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                request.RememberMe ?? false,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return Results.Ok(new UserInfo
                {
                    UserId = user.Id,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    IsAuthenticated = true
                });
            }

            return Results.Unauthorized();
        })
        .WithName("Login")
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/register", async Task<Results<Ok<UserInfo>, ValidationProblem>> (RegisterRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
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
                    return TypedResults.Ok(new UserInfo
                    {
                        UserId = user.Id,
                        UserName = user.UserName ?? "",
                        Email = user.Email ?? "",
                        IsAuthenticated = true
                    });
                }
            }

            return TypedResults.ValidationProblem(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()));
        })
        .WithName("Register")
        .Produces<UserInfo>(StatusCodes.Status200OK)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout")
        .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/user", async (ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
        {
            if (!user.Identity?.IsAuthenticated ?? true)
            {
                return Results.Ok(new UserInfo { IsAuthenticated = false });
            }

            var appUser = await userManager.GetUserAsync(user);

            return Results.Ok(new UserInfo
            {
                UserId = userManager.GetUserId(user) ?? "",
                UserName = user.Identity?.Name ?? "",
                Email = appUser?.Email ?? user.Identity?.Name ?? "",
                IsAuthenticated = true
            });
        })
        .WithName("GetCurrentUser")
        .Produces<UserInfo>(StatusCodes.Status200OK);

        return app;
    }
}

public record LoginRequest(string Email, string Password, bool? RememberMe = null);
public record RegisterRequest(string Email, string Password, string ConfirmPassword);
public record UserInfo
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Email { get; init; } = "";
    public bool IsAuthenticated { get; init; }
}
