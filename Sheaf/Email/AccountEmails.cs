using Microsoft.AspNetCore.Identity;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Email;

/// <summary>Generates account codes and queues the emails that carry them. Every send is limited by <see cref="EmailCooldown"/>.</summary>
internal sealed class AccountEmails(
    UserManager<ApplicationUser> users, EmailCodeTokenProvider codes, IEmailOutbox outbox, EmailCooldown cooldown)
{
    private const string Expiry = "It expires in 10 minutes and works once.";
    private const string Ignore = "If you didn't ask for this, you can ignore this email.";

    /// <summary>
    /// Sends a sign-up code. <paramref name="passwordProven"/> is for a sign-in with the correct password: it has a
    /// larger daily allowance (<see cref="EmailCodeTokenProvider.MaxIssuedPerDayWithPassword"/>) and its own cooldown,
    /// so someone who uses up the anonymous allowance can't keep the owner from verifying.
    /// </summary>
    public async Task SendConfirmationCodeAsync(ApplicationUser user, bool passwordProven = false)
    {
        const string purpose = UserManager<ApplicationUser>.ConfirmEmailTokenPurpose;
        string key = passwordProven ? $"confirm-signin:{user.Id}" : $"confirm:{user.Id}";
        if (await TryStartAsync(user, key, purpose, passwordProven) && await codes.TryGenerateAsync(purpose, users, user) is { } code)
        {
            outbox.Enqueue(new EmailMessage(EmailOf(user), "Your Sheaf verification code",
                $"Your Sheaf verification code is {code}\n\n{Expiry}\n\n{Ignore}"));
        }
    }

    /// <summary>Tells the owner of an existing account that someone tried to register their address.</summary>
    public void SendAlreadyRegistered(ApplicationUser user)
    {
        if (cooldown.TryStart($"registered:{user.Id}"))
        {
            outbox.Enqueue(new EmailMessage(EmailOf(user), "You already have a Sheaf account",
                "Someone tried to create a Sheaf account with this email address, but you already have one.\n\n"
                + "Sign in instead, or use \"Forgot password\" if you don't remember your password.\n\n" + Ignore));
        }
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user)
    {
        const string purpose = UserManager<ApplicationUser>.ResetPasswordTokenPurpose;
        if (await TryStartAsync(user, $"reset:{user.Id}", purpose) && await codes.TryGenerateAsync(purpose, users, user) is { } code)
        {
            outbox.Enqueue(new EmailMessage(EmailOf(user), "Your Sheaf password reset code",
                $"Your Sheaf password reset code is {code}\n\n{Expiry}\n\n{Ignore}"));
        }
    }

    /// <summary>Sends a code to the new address, or, if another account already uses it, a notice instead.</summary>
    public async Task SendChangeEmailCodeAsync(ApplicationUser user, string newEmail, bool addressInUse)
    {
        // Keyed by address too, so fixing a typo in the new address isn't held back by the cooldown.
        string key = $"change:{user.Id}:{newEmail.ToUpperInvariant()}";
        if (addressInUse)
        {
            if (cooldown.TryStart(key))
            {
                outbox.Enqueue(new EmailMessage(newEmail, "This address already has a Sheaf account",
                    "Someone tried to move a Sheaf account to this email address, but it already belongs to an account.\n\n"
                    + Ignore));
            }
            return;
        }

        string purpose = ChangeEmailPurpose(users, newEmail);
        if (await TryStartAsync(user, key, purpose) && await codes.TryGenerateAsync(purpose, users, user) is { } code)
        {
            outbox.Enqueue(new EmailMessage(newEmail, "Confirm your new Sheaf email address",
                $"Your Sheaf verification code is {code}\n\nEnter it to use this address to sign in. {Expiry}\n\n{Ignore}"));
        }
    }

    /// <summary>Warns the old address after the sign-in email changes, so a takeover doesn't go unnoticed.</summary>
    public void SendEmailChanged(string oldEmail) =>
        outbox.Enqueue(new EmailMessage(oldEmail, "Your Sheaf sign-in email changed",
            "The email address used to sign in to your Sheaf account was just changed, and this address no longer works for it.\n\n"
            + "If you didn't make this change, contact your church's Sheaf administrator."));

    /// <summary>The token purpose for moving to <paramref name="newEmail"/>, normalized so capitalization doesn't matter.</summary>
    internal static string ChangeEmailPurpose(UserManager<ApplicationUser> users, string newEmail) =>
        UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(users.NormalizeEmail(newEmail));

    private async Task<bool> TryStartAsync(ApplicationUser user, string cooldownKey, string purpose, bool passwordProven = false) =>
        await codes.CanIssueAsync(users, user, purpose, passwordProven) && cooldown.TryStart(cooldownKey);

    private static string EmailOf(ApplicationUser user) =>
        user.Email ?? throw new InvalidOperationException("Accounts always have an email address.");
}
