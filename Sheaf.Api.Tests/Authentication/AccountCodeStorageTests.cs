using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;

namespace Sheaf.Api.Tests.Authentication;

/// <summary>Account codes against SQL Server, whose column limits the in-memory tests can't see.</summary>
public class AccountCodeStorageTests : ChurchApiTestBase
{
    [Test]
    public async Task VerifyUserTokenAsync_ChangeEmailCodeForMaximumLengthAddress_StoresAndAccepts()
    {
        ApplicationUser created = await CreateUserAsync();
        string newEmail = $"{new string('a', 64)}@{new string('b', 179)}.example.com";
        await Assert.That(newEmail.Length).IsEqualTo(256);
        bool accepted = false;

        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await users.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
            string normalized = users.NormalizeEmail(newEmail);
            string code = await users.GenerateChangeEmailTokenAsync(user, normalized);
            accepted = await users.VerifyUserTokenAsync(user, users.Options.Tokens.ChangeEmailTokenProvider,
                UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(normalized), code);
        });

        await Assert.That(accepted).IsTrue();
    }
}
