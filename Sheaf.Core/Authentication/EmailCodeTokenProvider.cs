using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

using Sheaf.Data;

namespace Sheaf.Core.Authentication;

/// <summary>
/// Six-digit codes for email confirmation, password reset and email change, stored per account and purpose.
/// </summary>
/// <remarks>
/// <para>
/// Identity's built-in email provider derives codes from the clock, so a valid code exists at every moment even if
/// none was sent, and guesses are limited only by account lockout, which a successful sign-in clears. Here a code exists
/// only after it is emailed, works once, expires after <see cref="Lifetime"/>, and is discarded after
/// <see cref="MaxFailures"/> guesses. Each guess is recorded before it is compared, so parallel guesses can't skip the
/// count. At most <see cref="MaxIssuedPerDay"/> codes per purpose per day may be sent on request of someone who hasn't
/// proven anything, and at most <see cref="MaxIssuedPerDayWithPassword"/> in all (<see cref="CanIssueAsync"/>), so
/// guesses are bounded per day even for someone who knows the password, and each batch goes to the owner's inbox.
/// Wrong codes don't count toward sign-in lockout, so guessing can't lock someone out.
/// A code is also tied to the account's email and security stamp when it was sent: changing either voids it.
/// </para>
/// <para>
/// What is stored is protected with ASP.NET Core Data Protection, whose keys are themselves encrypted outside the
/// database in production. Someone who can only read the database therefore can't test the million possible codes
/// offline.
/// </para>
/// <para>
/// Storing and checking a code saves the user through <see cref="UserManager{TUser}.SetAuthenticationTokenAsync"/>,
/// which writes every column of the tracked user. Verify a code before changing anything else on the user, or those
/// changes are saved too.
/// </para>
/// </remarks>
public sealed class EmailCodeTokenProvider(TimeProvider time, IDataProtectionProvider dataProtection)
    : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public const string ProviderName = "SheafEmailCode";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxFailures = 5;
    public const int MaxIssuedPerDay = 10;
    public const int MaxIssuedPerDayWithPassword = 20;
    private static readonly TimeSpan IssueWindow = TimeSpan.FromDays(1);
    private const string ChangeEmailPrefix = "ChangeEmail:";
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Sheaf.Authentication.EmailCode");

    /// <summary>
    /// Whether another code may be sent for <paramref name="purpose"/> today. <paramref name="passwordProven"/> is for a
    /// request that proved the account's password, which may send more, so someone who used up the anonymous allowance
    /// can't keep the owner from verifying; the total is still capped.
    /// </summary>
    public async Task<bool> CanIssueAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string purpose, bool passwordProven = false)
    {
        CodeState? state = await LoadAsync(manager, user, purpose);
        int limit = passwordProven ? MaxIssuedPerDayWithPassword : MaxIssuedPerDay;
        return state is null || time.GetUtcNow() - state.WindowStart >= IssueWindow || state.Issued < limit;
    }

    /// <summary>
    /// Creates and stores a new code, replacing any outstanding one for the purpose. Returns null when a concurrent
    /// update to the account stopped it being saved; nothing was issued then, and asking again works.
    /// </summary>
    public async Task<string?> TryGenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        DateTimeOffset now = time.GetUtcNow();
        CodeState? previous = await LoadAsync(manager, user, purpose);
        (DateTimeOffset windowStart, int issued) = previous is null || now - previous.WindowStart >= IssueWindow
            ? (now, 0)
            : (previous.WindowStart, previous.Issued);

        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        CodeState state = new(_protector.Protect(Convert.ToBase64String(Hash(purpose, user, code))), now + Lifetime, 0, issued + 1, windowStart);
        return (await SaveAsync(manager, user, purpose, state)).Succeeded ? code : null;
    }

    /// <summary>Identity's entry point. Prefer <see cref="TryGenerateAsync"/>, which reports a lost race without throwing.</summary>
    public async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user) =>
        await TryGenerateAsync(purpose, manager, user)
            ?? throw new InvalidOperationException("The code could not be stored because the account changed concurrently.");

    /// <summary>Checks a code. Whitespace is ignored, since people paste codes with spaces or a trailing newline.</summary>
    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        token = string.Concat(token.Where(c => !char.IsWhiteSpace(c)));
        CodeState? state = await LoadAsync(manager, user, purpose);
        if (state?.Code is null || time.GetUtcNow() >= state.ExpiresAt || state.Failures >= MaxFailures)
        {
            return false;
        }

        // Count the guess before comparing. A request whose count didn't save (another guess saved first) is
        // rejected without being compared, so every comparison is counted.
        CodeState counted = state with { Failures = state.Failures + 1 };
        if (!(await SaveAsync(manager, user, purpose, counted)).Succeeded)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(Unprotect(state.Code), Hash(purpose, user, token)))
        {
            if (counted.Failures >= MaxFailures)
            {
                await SaveAsync(manager, user, purpose, counted with { Code = null });
            }
            return false;
        }

        // Single use. If a concurrent request saved first, this save fails and only that request succeeds.
        return (await SaveAsync(manager, user, purpose, counted with { Code = null, Failures = state.Failures })).Succeeded;
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(false);

    /// <summary>Binds the code to its purpose, account, email and security stamp. Stored only under Data Protection.</summary>
    private static byte[] Hash(string purpose, ApplicationUser user, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', purpose, user.Id, user.NormalizedEmail ?? "", user.SecurityStamp ?? "", code)));

    /// <summary>The stored hash, or an empty array (which matches nothing) if it can't be unprotected, e.g. its key was revoked.</summary>
    private byte[] Unprotect(string stored)
    {
        try
        {
            return Convert.FromBase64String(_protector.Unprotect(stored));
        }
        catch (CryptographicException)
        {
            return [];
        }
    }

    /// <summary>
    /// Where a purpose's state is stored. Email-change purposes include the new address, which can be longer than the
    /// token name column, so they share one entry; the hash still binds the address. A new email-change code replaces
    /// the outstanding one, whichever address it was for.
    /// </summary>
    private static string StorageName(string purpose) =>
        purpose.StartsWith(ChangeEmailPrefix, StringComparison.Ordinal) ? "ChangeEmail" : purpose;

    private static async Task<CodeState?> LoadAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string purpose)
    {
        string? value = await manager.GetAuthenticationTokenAsync(user, ProviderName, StorageName(purpose));
        return value is null ? null : JsonSerializer.Deserialize<CodeState>(value);
    }

    private static Task<IdentityResult> SaveAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string purpose, CodeState state) =>
        manager.SetAuthenticationTokenAsync(user, ProviderName, StorageName(purpose), JsonSerializer.Serialize(state));

    /// <summary>
    /// What is stored for one purpose. <see cref="Code"/> is the code's hash under Data Protection, or null when no code
    /// is outstanding.
    /// </summary>
    private sealed record CodeState(string? Code, DateTimeOffset ExpiresAt, int Failures, int Issued, DateTimeOffset WindowStart);
}
