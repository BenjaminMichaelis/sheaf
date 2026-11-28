using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Sheaf.Authentication;
using Sheaf.Authorization;
using Sheaf.Core.Authentication;
using Sheaf.Data;
using Sheaf.Email;

namespace Sheaf.Endpoints;

/// <summary>
/// Sign-up, email verification, password reset and sign-in email changes. Anonymous endpoints answer the same way
/// whether or not an address has an account, so they can't be used to find out who uses Sheaf. Request shape is
/// checked by minimal API validation (DataAnnotations on the request records) before a handler runs.
/// </summary>
internal static class AccountEndpoints
{
    internal const string EmailNotConfirmedProblem = "tag:sheaf,2026:problems/email-not-confirmed";

    internal static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        // Every account endpoint shares the sign-in rate limit. Success response types are inferred from each handler's
        // TypedResults; WithAccountResponses adds the responses they all share.
        RouteGroupBuilder account = group.MapGroup("").RequireRateLimiting(RequestSecurityExtensions.AuthRateLimitPolicy);
        RouteGroupBuilder anonymous = account.MapGroup("").AllowAnonymous();
        RouteGroupBuilder signedIn = account.MapGroup("").RequireAuthorization(SheafPolicies.Authenticated);

        anonymous.MapPost("/register", RegisterAsync).WithName("Register").WithAccountResponses()
            .WithSummary("Create an account and email a verification code. The response is the same if the address already has an account.");
        anonymous.MapPost("/confirm-email", ConfirmEmailAsync).WithName("ConfirmEmail").WithAccountResponses()
            .WithSummary("Verify an account's email with the emailed code, set its password, then sign in.");
        anonymous.MapPost("/resend-confirmation", ResendConfirmationAsync).WithName("ResendConfirmation").WithAccountResponses()
            .WithSummary("Email a new verification code if the address has an unverified account. At most one per minute.");
        anonymous.MapPost("/forgot-password", ForgotPasswordAsync).WithName("ForgotPassword").WithAccountResponses()
            .WithSummary("Email a password reset code if the address has an account. At most one per minute.");
        anonymous.MapPost("/reset-password", ResetPasswordAsync).WithName("ResetPassword").WithAccountResponses()
            .WithSummary("Set a new password with an emailed reset code. This also verifies the email and signs out other sessions within five minutes.");
        signedIn.MapPut("/profile", UpdateProfileAsync).WithName("UpdateProfile").WithAccountResponses()
            .WithSummary("Change the signed-in account's name.");
        signedIn.MapPost("/email", ChangeEmailAsync).WithName("ChangeEmail").WithAccountResponses()
            .WithSummary("Start changing the sign-in email: checks the current password and emails a code to the new address.");
        signedIn.MapPost("/email/confirm", ConfirmEmailChangeAsync).WithName("ConfirmEmailChange").WithAccountResponses()
            .WithSummary("Finish changing the sign-in email with the code sent to the new address. The old address is notified.");

        return group;
    }

    /// <summary>Request validation (400), the request-origin check (403) and the rate limit (429).</summary>
    private static RouteHandlerBuilder WithAccountResponses(this RouteHandlerBuilder endpoint) => endpoint
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests);

    private static async Task<Results<Accepted, ValidationProblem>> RegisterAsync(
        RegisterRequest request, UserManager<ApplicationUser> userManager, ApplicationDbContext db, AccountEmails emails)
    {
        string email = request.Email.Trim();
        ApplicationUser candidate = new()
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim()
        };
        // Password rules don't depend on whether the address is taken, so checking them first reveals nothing.
        if (await PasswordProblemAsync(userManager, candidate, request.Password) is { } passwordProblem)
        {
            return passwordProblem;
        }

        ApplicationUser? existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            IdentityResult created = await SaveUniqueEmailAsync(userManager, db, candidate,
                () => userManager.CreateAsync(candidate, request.Password));
            if (created.Succeeded)
            {
                await emails.SendConfirmationCodeAsync(candidate);
                return TypedResults.Accepted((string?)null);
            }
            existing = created.Errors.Any(IsDuplicate) ? await userManager.FindByEmailAsync(email) : null;
            if (existing is null)
            {
                return ToValidationProblem(created);
            }
        }
        else
        {
            // Creating an account hashes the password; do the same work here so response time doesn't tell an existing
            // address from a new one.
            _ = userManager.PasswordHasher.HashPassword(candidate, request.Password);
        }

        // Registration never changes an existing account: every account was marked unverified when verification
        // shipped, so overwriting unverified accounts would let anyone replace a member's password. Whoever owns the
        // address gets a code, and confirming with it sets the password they choose (see ConfirmEmailAsync).
        if (existing.EmailConfirmed)
        {
            await emails.SendAlreadyRegisteredAsync(existing);
        }
        else
        {
            await emails.SendConfirmationCodeAsync(existing);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<UserInfo>, ValidationProblem>> ConfirmEmailAsync(
        ConfirmEmailRequest request, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken)
    {
        string email = request.Email.Trim();
        ApplicationUser? user = await userManager.FindByEmailAsync(email);
        if (await PasswordProblemAsync(userManager, user ?? Candidate(email), request.Password) is { } passwordProblem)
        {
            return passwordProblem;
        }
        if (user is null || user.EmailConfirmed
            || !await userManager.VerifyUserTokenAsync(user, userManager.Options.Tokens.EmailConfirmationTokenProvider,
                UserManager<ApplicationUser>.ConfirmEmailTokenPurpose, request.Code))
        {
            return InvalidCode();
        }

        // The code proves the caller owns the address, so their password wins over whatever was set when the account
        // was created: someone who registered another person's address first can't keep a way in.
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, request.Password);
        if (await ProvenOwnerAsync(userManager, user) is { } problem)
        {
            return problem;
        }
        await signInManager.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(await AuthEndpoints.CreateUserInfoAsync(user, churchAccess, currentChurch, cancellationToken));
    }

    private static async Task<Accepted> ResendConfirmationAsync(
        EmailRequest request, UserManager<ApplicationUser> userManager, AccountEmails emails)
    {
        if (await userManager.FindByEmailAsync(request.Email.Trim()) is { EmailConfirmed: false } user)
        {
            await emails.SendConfirmationCodeAsync(user);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Accepted> ForgotPasswordAsync(
        EmailRequest request, UserManager<ApplicationUser> userManager, AccountEmails emails)
    {
        if (await userManager.FindByEmailAsync(request.Email.Trim()) is { } user)
        {
            await emails.SendPasswordResetCodeAsync(user);
        }
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem>> ResetPasswordAsync(
        ResetPasswordRequest request, UserManager<ApplicationUser> userManager)
    {
        string email = request.Email.Trim();
        ApplicationUser? user = await userManager.FindByEmailAsync(email);
        if (await PasswordProblemAsync(userManager, user ?? Candidate(email), request.NewPassword) is { } passwordProblem)
        {
            return passwordProblem;
        }
        if (user is null
            || !await userManager.VerifyUserTokenAsync(user, userManager.Options.Tokens.PasswordResetTokenProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose, request.Code))
        {
            return InvalidCode();
        }

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, request.NewPassword);
        return await ProvenOwnerAsync(userManager, user) is { } problem ? problem : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<UserInfo>, ValidationProblem, UnauthorizedHttpResult>> UpdateProfileAsync(
        UpdateProfileRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
        IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        IdentityResult saved = await userManager.UpdateAsync(user);
        return saved.Succeeded
            ? TypedResults.Ok(await AuthEndpoints.CreateUserInfoAsync(user, churchAccess, currentChurch, cancellationToken))
            : ToValidationProblem(saved);
    }

    private static async Task<Results<Accepted, ValidationProblem, UnauthorizedHttpResult>> ChangeEmailAsync(
        ChangeEmailRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, AccountEmails emails)
    {
        if (await userManager.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        string newEmail = request.NewEmail.Trim();
        if (string.Equals(userManager.NormalizeEmail(newEmail), user.NormalizedEmail, StringComparison.Ordinal))
        {
            return Problem(nameof(ChangeEmailRequest.NewEmail), "This is already your sign-in email.");
        }
        if (!await CheckPasswordAsync(userManager, user, request.CurrentPassword))
        {
            return Problem(nameof(ChangeEmailRequest.CurrentPassword), "The password is incorrect.");
        }

        bool inUse = await userManager.FindByEmailAsync(newEmail) is not null;
        await emails.SendChangeEmailCodeAsync(user, newEmail, inUse);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<UserInfo>, ValidationProblem, UnauthorizedHttpResult>> ConfirmEmailChangeAsync(
        ConfirmEmailChangeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager, ChurchClaimsPrincipalFactory principalFactory, ApplicationDbContext db, AccountEmails emails,
        IChurchAccessService churchAccess, ICurrentChurch currentChurch, CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        string newEmail = request.NewEmail.Trim();
        if (!await userManager.VerifyUserTokenAsync(user, userManager.Options.Tokens.ChangeEmailTokenProvider,
            AccountEmails.ChangeEmailPurpose(userManager, newEmail), request.Code))
        {
            return InvalidCode();
        }

        // One save: the login name follows the email, and the new security stamp signs out every other session.
        string oldEmail = user.Email ?? "";
        user.Email = newEmail;
        user.UserName = newEmail;
        user.EmailConfirmed = true;
        IdentityResult changed = await SaveUniqueEmailAsync(userManager, db, user, () => userManager.UpdateSecurityStampAsync(user));
        if (!changed.Succeeded)
        {
            // Another account took the address after the code was sent.
            return InvalidCode();
        }
        await emails.SendEmailChangedAsync(oldEmail);

        principalFactory.RenewalPrincipal = principal;
        try
        {
            await signInManager.RefreshSignInAsync(user);
        }
        finally
        {
            principalFactory.RenewalPrincipal = null;
        }
        return TypedResults.Ok(await AuthEndpoints.CreateUserInfoAsync(user, churchAccess, currentChurch, cancellationToken));
    }

    /// <summary>
    /// Checks a password the way sign-in does: a locked-out account always fails, a wrong password counts toward
    /// lockout, and a right one clears earlier failures. Used where <see cref="SignInManager{TUser}"/> can't be: the
    /// account isn't allowed to sign in yet, or the caller is already signed in. Wrong codes are limited separately, by
    /// <see cref="EmailCodeTokenProvider"/>, so clearing the password count gives no code guesses back.
    /// </summary>
    internal static async Task<bool> CheckPasswordAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string? password)
    {
        if (await userManager.IsLockedOutAsync(user))
        {
            return false;
        }
        if (!await userManager.CheckPasswordAsync(user, password ?? ""))
        {
            await userManager.AccessFailedAsync(user);
            return false;
        }
        await userManager.ResetAccessFailedCountAsync(user);
        return true;
    }

    /// <summary>
    /// Saves an account after its holder proved they own its email with a code: the email is verified, any lockout is
    /// lifted, and a new security stamp voids other sessions and outstanding codes. One save, so it can't half-apply.
    /// </summary>
    private static async Task<ValidationProblem?> ProvenOwnerAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        user.EmailConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        IdentityResult saved = await userManager.UpdateSecurityStampAsync(user);
        return saved.Succeeded ? null : ToValidationProblem(saved);
    }

    /// <summary>
    /// Runs a save that sets <paramref name="user"/>'s email. Identity checks uniqueness before saving, so two requests
    /// racing for one address can both pass it; the unique index then stops the second, which is reported as a duplicate
    /// email. The rejected change is discarded so a later save in the same request doesn't retry it.
    /// </summary>
    private static async Task<IdentityResult> SaveUniqueEmailAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, ApplicationUser user, Func<Task<IdentityResult>> save)
    {
        try
        {
            return await save();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 2601/2627: SQL Server unique index or constraint violation. Anything else is a real failure.
            db.Entry(user).State = EntityState.Detached;
            return IdentityResult.Failed(userManager.ErrorDescriber.DuplicateEmail(user.Email ?? ""));
        }
    }

    private static async Task<ValidationProblem?> PasswordProblemAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, string password)
    {
        List<IdentityError> errors = [];
        foreach (IPasswordValidator<ApplicationUser> validator in userManager.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(userManager, user, password)).Errors);
        }
        return errors.Count == 0 ? null : ToValidationProblem(IdentityResult.Failed([.. errors]));
    }

    /// <summary>
    /// A stand-in account for password rules when the address has no account, so the answer doesn't differ. It is never
    /// saved, and no name is known for it.
    /// </summary>
    private static ApplicationUser Candidate(string email) =>
        new() { UserName = email, Email = email, FirstName = "", LastName = "" };

    private static bool IsDuplicate(IdentityError error) =>
        error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName);

    private static ValidationProblem InvalidCode() =>
        Problem(nameof(ConfirmEmailRequest.Code), "The code is incorrect or has expired. Request a new one and try again.");

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static ValidationProblem ToValidationProblem(IdentityResult result) =>
        TypedResults.ValidationProblem(result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
}

/// <summary>Messages and limits shared by the account request records.</summary>
internal static class AccountValidation
{
    internal const string EnterEmail = "Enter a valid email address.";
    internal const string EnterCode = "Enter the six-digit code from the email.";
    internal const int CodeMaxLength = 12;
}

/// <summary>A new account. Its name is what Staff see when they invite or link it.</summary>
public sealed record RegisterRequest(
    [Required(ErrorMessage = AccountValidation.EnterEmail), EmailAddress(ErrorMessage = AccountValidation.EnterEmail),
        StringLength(ApplicationUser.EmailMaxLength, ErrorMessage = AccountValidation.EnterEmail)] string Email,
    [Required] string Password,
    // nameof(Password) isn't in scope inside a positional parameter list, so the name is spelled out.
    [property: Compare("Password", ErrorMessage = "Passwords do not match.")] string ConfirmPassword,
    [Required(ErrorMessage = "Enter a first name."), StringLength(ApplicationUser.NameMaxLength)] string FirstName,
    [Required(ErrorMessage = "Enter a last name."), StringLength(ApplicationUser.NameMaxLength)] string LastName);

/// <summary>
/// The emailed code and the password to sign in with from now on. The code proves the caller owns the address, so this
/// password replaces any set at registration.
/// </summary>
public sealed record ConfirmEmailRequest(
    [Required(ErrorMessage = AccountValidation.EnterEmail)] string Email,
    [Required] string Password,
    [Required(ErrorMessage = AccountValidation.EnterCode), StringLength(AccountValidation.CodeMaxLength)] string Code);

/// <summary>The account holder's name, as Staff see it when they invite or link the account.</summary>
public sealed record UpdateProfileRequest(
    [Required(ErrorMessage = "Enter a first name."), StringLength(ApplicationUser.NameMaxLength)] string FirstName,
    [Required(ErrorMessage = "Enter a last name."), StringLength(ApplicationUser.NameMaxLength)] string LastName);

/// <summary>An address to send a code to.</summary>
public sealed record EmailRequest([Required(ErrorMessage = AccountValidation.EnterEmail)] string Email);

/// <summary>An emailed reset code and the new password.</summary>
public sealed record ResetPasswordRequest(
    [Required(ErrorMessage = AccountValidation.EnterEmail)] string Email,
    [Required(ErrorMessage = AccountValidation.EnterCode), StringLength(AccountValidation.CodeMaxLength)] string Code,
    [Required] string NewPassword);

/// <summary>The new sign-in address, with the current password to show the account holder is asking.</summary>
public sealed record ChangeEmailRequest(
    [Required(ErrorMessage = AccountValidation.EnterEmail), EmailAddress(ErrorMessage = AccountValidation.EnterEmail),
        StringLength(ApplicationUser.EmailMaxLength, ErrorMessage = AccountValidation.EnterEmail)] string NewEmail,
    [Required] string CurrentPassword);

/// <summary>The new address and the code sent to it.</summary>
public sealed record ConfirmEmailChangeRequest(
    [Required(ErrorMessage = AccountValidation.EnterEmail)] string NewEmail,
    [Required(ErrorMessage = AccountValidation.EnterCode), StringLength(AccountValidation.CodeMaxLength)] string Code);
