using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Sheaf.Core.Authentication;
using Sheaf.Data;
using Sheaf.Email;
using Sheaf.Endpoints;

namespace Sheaf.Api.Tests.Authentication;

/// <summary>The account email requirements in README "Accounts and email verification", through the HTTP API.</summary>
public partial class AccountEmailTests : InMemoryApiTest
{
    private const string Password = "Account-test-Password1!";
    private const string OtherPassword = "Account-other-Password2!";
    private const string ThirdPassword = "Account-third-Password3!";
    private const string CookieName = "__Host-sheaf";
    private const string EmailNotConfirmed = "tag:sheaf,2026:problems/email-not-confirmed";
    private readonly CapturingOutbox _outbox = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IEmailOutbox>();
        services.AddSingleton<IEmailOutbox>(_outbox);
    }

    [Test]
    public async Task Register_NewAddress_EmailsCodeWithoutSigningIn()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();

        using HttpResponseMessage response = await RegisterAsync(client, email, Password);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.Headers.Contains(HeaderNames.SetCookie)).IsFalse();
        await Assert.That(CodeIn(_outbox.Single(email))).IsNotNull();
        await LoginRequiresVerificationAsync(client, email, Password);
    }

    [Test]
    public async Task Register_ConfirmedAddress_AnswersLikeNewAddressAndSendsNoticeWithoutCode()
    {
        using HttpClient client = CreateClient();
        ApplicationUser existing = await CreateUserAsync(confirmed: true);

        using HttpResponseMessage taken = await RegisterAsync(client, Address(existing), OtherPassword);
        using HttpResponseMessage fresh = await RegisterAsync(client, NewEmail(), OtherPassword);

        await Assert.That(taken.StatusCode).IsEqualTo(fresh.StatusCode);
        await Assert.That(await taken.Content.ReadAsStringAsync()).IsEqualTo(await fresh.Content.ReadAsStringAsync());
        await Assert.That(CodeIn(_outbox.Single(Address(existing)))).IsNull();
        await LoginAsync(client, Address(existing), Password);
    }

    [Test]
    public async Task Register_InvalidInput_ReturnsFieldErrorsAndSendsNothing()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("not-an-email", Password, OtherPassword, " ", ""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        JsonElement errors = await ErrorsAsync(response);
        foreach (string field in new[]
        {
            nameof(RegisterRequest.Email), nameof(RegisterRequest.ConfirmPassword),
            nameof(RegisterRequest.FirstName), nameof(RegisterRequest.LastName)
        })
        {
            await Assert.That(errors.TryGetProperty(field, out _)).IsTrue().Because(field);
        }
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task Register_ExistingUnverifiedAccount_KeepsPasswordAndEmailsCode()
    {
        // Every account that existed before verification shipped is unverified, so this is the established-member case.
        using HttpClient client = CreateClient();
        ApplicationUser member = await CreateUserAsync(confirmed: false);

        using HttpResponseMessage response = await RegisterAsync(client, Address(member), OtherPassword);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(CodeIn(_outbox.Single(Address(member)))).IsNotNull();
        await LoginRequiresVerificationAsync(client, Address(member), Password);
        using HttpResponseMessage attackerPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(member), OtherPassword));
        await Assert.That(attackerPassword.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Register_SameUnverifiedAddressTwiceWithinAMinute_SendsOneEmail()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();

        using (await RegisterAsync(client, email, Password)) { }
        using (await RegisterAsync(client, email, Password)) { }

        await Assert.That(_outbox.To(email).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Register_AddressWithApostrophe_CreatesAccount()
    {
        using HttpClient client = CreateClient();
        string email = $"o'brien.{Guid.NewGuid():N}@example.com";

        using HttpResponseMessage response = await RegisterAsync(client, email, Password);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(CodeIn(_outbox.Single(email))).IsNotNull();
    }

    [Test]
    public async Task ConfirmEmail_CorrectCode_ConfirmsAndSignsIn()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();
        using (await RegisterAsync(client, email, Password)) { }

        using HttpResponseMessage response = await ConfirmAsync(client, email, Password, $" {LatestCode(email)} ");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserInfo info = await response.Content.ReadFromJsonAsync<UserInfo>() ?? throw new InvalidOperationException("No user info.");
        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That((info.Email, info.FirstName, info.LastName)).IsEqualTo((email, "Lena", "Leader"));
        await Assert.That(ReadCookie(response)).IsNotEmpty();
        await LoginAsync(client, email, Password);
    }

    [Test]
    public async Task ConfirmEmail_AddressRegisteredFirstBySomeoneElse_OwnersPasswordReplacesTheirs()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();
        using (await RegisterAsync(client, email, OtherPassword)) { }
        // The squatter never sees the code; the address owner registers and receives it.
        using (await RegisterAsync(client, email, Password)) { }
        Time.Advance(TimeSpan.FromMinutes(1));
        using (await ResendAsync(client, email)) { }

        using HttpResponseMessage confirmed = await ConfirmAsync(client, email, Password, LatestCode(email));

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await LoginAsync(client, email, Password);
        using HttpResponseMessage squatter = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, OtherPassword));
        await Assert.That(squatter.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProfile_SignedIn_ChangesName()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string cookie = await LoginAsync(client, Address(user), Password);

        using HttpResponseMessage response = await SendAsync(client, cookie, HttpMethod.Put, "/api/auth/profile",
            new UpdateProfileRequest(" Lena ", "Leader"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using HttpRequestMessage read = new(HttpMethod.Get, "/api/auth/user");
        read.Headers.Add(HeaderNames.Cookie, cookie);
        using HttpResponseMessage current = await client.SendAsync(read);
        UserInfo info = await current.Content.ReadFromJsonAsync<UserInfo>() ?? throw new InvalidOperationException("No user info.");
        await Assert.That((info.FirstName, info.LastName)).IsEqualTo(("Lena", "Leader"));
    }

    [Test]
    public async Task UpdateProfile_BlankName_ReturnsFieldError()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string cookie = await LoginAsync(client, Address(user), Password);

        using HttpResponseMessage response = await SendAsync(client, cookie, HttpMethod.Put, "/api/auth/profile",
            new UpdateProfileRequest(" ", "Leader"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await ErrorsAsync(response)).TryGetProperty(nameof(UpdateProfileRequest.FirstName), out _)).IsTrue();
    }

    [Test]
    public async Task UpdateProfile_Anonymous_Returns401()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/auth/profile", new UpdateProfileRequest("Lena", "Leader"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ConfirmEmail_CodeOlderThanTenMinutes_Returns400()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();
        using (await RegisterAsync(client, email, Password)) { }
        string code = LatestCode(email);
        Time.Advance(TimeSpan.FromMinutes(10));

        using HttpResponseMessage response = await ConfirmAsync(client, email, Password, code);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginRequiresVerificationAsync(client, email, Password);
    }

    [Test]
    public async Task ConfirmEmail_FiveWrongCodes_DiscardsCodeWithoutLockingSignIn()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await LoginRequiresVerificationAsync(client, Address(user), Password);
        string code = LatestCode(Address(user));

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await ConfirmAsync(client, Address(user), Password, WrongCode(code));
            await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using HttpResponseMessage discarded = await ConfirmAsync(client, Address(user), Password, code);

        await Assert.That(discarded.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        // A locked account answers 401; an unlocked, unverified one asks for verification.
        await LoginRequiresVerificationAsync(client, Address(user), Password);
    }

    [Test]
    public async Task ConfirmEmail_FourWrongCodesThenRightOne_ConfirmsAndSignsIn()
    {
        // With the test above, pins the limit at exactly five: a fifth guess is still allowed.
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await LoginRequiresVerificationAsync(client, Address(user), Password);
        string code = LatestCode(Address(user));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using HttpResponseMessage failed = await ConfirmAsync(client, Address(user), Password, WrongCode(code));
            await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        using HttpResponseMessage confirmed = await ConfirmAsync(client, Address(user), Password, code);

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ConfirmEmail_UnknownAddress_AnswersLikeWrongCode()
    {
        using HttpClient client = CreateClient();
        string email = NewEmail();
        using (await RegisterAsync(client, email, Password)) { }

        using HttpResponseMessage unknown = await ConfirmAsync(client, NewEmail(), Password, "123456");
        using HttpResponseMessage wrong = await ConfirmAsync(client, email, Password, WrongCode(LatestCode(email)));

        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await ErrorsAsync(unknown)).GetRawText()).IsEqualTo((await ErrorsAsync(wrong)).GetRawText());
    }

    [Test]
    public async Task VerifyUserTokenAsync_GuessLoadedBeforeAnotherGuessSaved_RightGuessRejected()
    {
        // Two parallel guesses load the same state; the one that loses the save is rejected even if right, so parallel
        // requests can't get more guesses than the limit.
        ApplicationUser created = await CreateUserAsync(confirmed: false);
        await using AsyncServiceScope first = Factory.Services.CreateAsyncScope();
        await using AsyncServiceScope second = Factory.Services.CreateAsyncScope();
        UserManager<ApplicationUser> firstUsers = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        UserManager<ApplicationUser> secondUsers = second.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser firstCopy = await firstUsers.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
        string code = await firstUsers.GenerateEmailConfirmationTokenAsync(firstCopy);
        ApplicationUser secondCopy = await secondUsers.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
        const string purpose = UserManager<ApplicationUser>.ConfirmEmailTokenPurpose;
        string provider = firstUsers.Options.Tokens.EmailConfirmationTokenProvider;

        bool wrongGuess = await firstUsers.VerifyUserTokenAsync(firstCopy, provider, purpose, WrongCode(code));
        bool staleRightGuess = await secondUsers.VerifyUserTokenAsync(secondCopy, provider, purpose, code);

        await Assert.That(wrongGuess).IsFalse();
        await Assert.That(staleRightGuess).IsFalse();
    }

    [Test]
    public async Task Login_UnconfirmedWithCorrectPassword_Returns403ProblemAndEmailsCode()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(user), Password));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo(EmailNotConfirmed);
        await Assert.That(response.Headers.Contains(HeaderNames.SetCookie)).IsFalse();
        using HttpResponseMessage confirmed = await ConfirmAsync(client, Address(user), Password, LatestCode(Address(user)));
        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_UnconfirmedWithWrongPassword_AnswersLikeUnknownAccountAndSendsNothing()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);

        using HttpResponseMessage wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(user), OtherPassword));
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(NewEmail(), OtherPassword));

        await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await wrong.Content.ReadAsStringAsync()).IsEqualTo(await unknown.Content.ReadAsStringAsync());
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task Login_UnverifiedAfterDailyCodeCapUsedUp_StillEmailsWorkingCode()
    {
        // Someone else used up the anonymous daily cap; the owner signing in with the right password still gets a code.
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await UseUpDailyConfirmationCodesAsync(user);

        await LoginRequiresVerificationAsync(client, Address(user), Password);
        using HttpResponseMessage confirmed = await ConfirmAsync(client, Address(user), Password, LatestCode(Address(user)));

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_UnverifiedAfterSignInAllowanceUsedUp_SendsNoCode()
    {
        // Someone who registered another person's address knows the password; signing in must not send codes without end.
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await IssueConfirmationCodesAsync(user, EmailCodeTokenProvider.MaxIssuedPerDayWithPassword);

        await LoginRequiresVerificationAsync(client, Address(user), Password);

        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task ValidateAsync_StoredCodeWithoutTheAppsKeys_Rejected()
    {
        // Reading the database isn't enough to check guesses offline: the stored code needs the app's Data Protection keys.
        ApplicationUser created = await CreateUserAsync(confirmed: false);
        bool withoutKeys = true;
        bool withKeys = false;
        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await users.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
            const string purpose = UserManager<ApplicationUser>.ConfirmEmailTokenPurpose;
            string code = await users.GenerateEmailConfirmationTokenAsync(user);

            withoutKeys = await new EmailCodeTokenProvider(Time, new EphemeralDataProtectionProvider())
                .ValidateAsync(purpose, code, users, user);
            withKeys = await services.GetRequiredService<EmailCodeTokenProvider>().ValidateAsync(purpose, code, users, user);
        });

        await Assert.That(withoutKeys).IsFalse();
        await Assert.That(withKeys).IsTrue();
    }

    [Test]
    public async Task ResendConfirmation_WithinAMinute_SendsOneCodeThenAnotherAfter()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);

        using (await ResendAsync(client, Address(user))) { }
        using (await ResendAsync(client, Address(user))) { }
        int withinMinute = _outbox.To(Address(user)).Count;
        Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage later = await ResendAsync(client, Address(user));

        await Assert.That(later.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(withinMinute).IsEqualTo(1);
        await Assert.That(_outbox.To(Address(user)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task ResendConfirmation_UnknownOrConfirmedAddress_Returns202AndSendsNothing()
    {
        using HttpClient client = CreateClient();
        ApplicationUser confirmed = await CreateUserAsync(confirmed: true);

        using HttpResponseMessage unknown = await ResendAsync(client, NewEmail());
        using HttpResponseMessage known = await ResendAsync(client, Address(confirmed));

        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(known.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task ResendConfirmation_DailyCodeCapUsedUp_SendsNothing()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await UseUpDailyConfirmationCodesAsync(user);

        using HttpResponseMessage response = await ResendAsync(client, Address(user));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task CanIssueAsync_DailyCapSentToday_FalseUntilTheNextDay()
    {
        // Through HTTP the per-IP limiter (real clock, 10 a minute) would stop the requests first.
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        await UseUpDailyConfirmationCodesAsync(user);
        bool sameDay = await CanIssueConfirmationAsync(user);
        Time.Advance(TimeSpan.FromDays(1));

        bool nextDay = await CanIssueConfirmationAsync(user);

        await Assert.That(sameDay).IsFalse();
        await Assert.That(nextDay).IsTrue();
    }

    [Test]
    public async Task ForgotPassword_UnknownAddress_Returns202AndSendsNothing()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await ForgotAsync(client, NewEmail());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task ForgotPassword_TwiceWithinAMinute_SendsOneCode()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);

        using (await ForgotAsync(client, Address(user))) { }
        using (await ForgotAsync(client, Address(user))) { }

        await Assert.That(_outbox.To(Address(user)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ResetPassword_EmailedCode_ChangesPasswordAndVerifiesEmail()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: false);
        using (await ForgotAsync(client, Address(user))) { }

        using HttpResponseMessage reset = await ResetAsync(client, Address(user), LatestCode(Address(user)), OtherPassword);

        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await LoginAsync(client, Address(user), OtherPassword);
        using HttpResponseMessage oldPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(user), Password));
        await Assert.That(oldPassword.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ResetPassword_LockedOutAccount_LiftsLockout()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        int attempts = Factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout.MaxFailedAccessAttempts;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(user), OtherPassword))) { }
        }
        using (HttpResponseMessage locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Address(user), Password)))
        {
            await Assert.That(locked.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        using (await ForgotAsync(client, Address(user))) { }

        using HttpResponseMessage reset = await ResetAsync(client, Address(user), LatestCode(Address(user)), ThirdPassword);

        await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await LoginAsync(client, Address(user), ThirdPassword);
    }

    [Test]
    public async Task ResetPassword_OtherSessionAfterCookieCheckInterval_IsSignedOut()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string otherSession = await LoginAsync(client, Address(user), Password);
        await Assert.That(await IsSignedInAsync(client, otherSession)).IsTrue();
        using (await ForgotAsync(client, Address(user))) { }
        using (await ResetAsync(client, Address(user), LatestCode(Address(user)), OtherPassword)) { }
        Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        bool signedIn = await IsSignedInAsync(client, otherSession);

        await Assert.That(signedIn).IsFalse();
    }

    [Test]
    public async Task ResetPassword_CodeUsedOnce_RejectedSecondTime()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        using (await ForgotAsync(client, Address(user))) { }
        string code = LatestCode(Address(user));
        using (HttpResponseMessage first = await ResetAsync(client, Address(user), code, OtherPassword))
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }

        using HttpResponseMessage replay = await ResetAsync(client, Address(user), code, ThirdPassword);

        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginAsync(client, Address(user), OtherPassword);
    }

    [Test]
    public async Task ResetPassword_FiveWrongCodes_DiscardsCodeWithoutLockingSignIn()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        using (await ForgotAsync(client, Address(user))) { }
        string code = LatestCode(Address(user));
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await ResetAsync(client, Address(user), WrongCode(code), OtherPassword);
            await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        using HttpResponseMessage discarded = await ResetAsync(client, Address(user), code, OtherPassword);

        await Assert.That(discarded.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginAsync(client, Address(user), Password);
    }

    [Test]
    public async Task ResetPassword_CodeNeverSent_Returns400()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);

        using HttpResponseMessage response = await ResetAsync(client, Address(user), "123456", OtherPassword);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginAsync(client, Address(user), Password);
    }

    [Test]
    public async Task ChangeEmail_CodeFromNewAddress_MovesSignInAndNotifiesOldAddress()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string oldEmail = Address(user);
        string newEmail = NewEmail();
        string cookie = await LoginAsync(client, oldEmail, Password);

        using HttpResponseMessage started = await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(newEmail, Password));
        using HttpResponseMessage confirmed = await PostAsync(client, cookie, "/api/auth/email/confirm",
            new ConfirmEmailChangeRequest(newEmail, LatestCode(newEmail)));

        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserInfo info = await confirmed.Content.ReadFromJsonAsync<UserInfo>() ?? throw new InvalidOperationException("No user info.");
        await Assert.That(info.Email).IsEqualTo(newEmail);
        await Assert.That(CodeIn(_outbox.Single(oldEmail))).IsNull();
        await LoginAsync(client, newEmail, Password);
        using HttpResponseMessage oldLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(oldEmail, Password));
        await Assert.That(oldLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ChangeEmail_WrongPassword_Returns400AndSendsNothing()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string cookie = await LoginAsync(client, Address(user), Password);

        using HttpResponseMessage response = await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(NewEmail(), OtherPassword));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await ErrorsAsync(response)).TryGetProperty(nameof(ChangeEmailRequest.CurrentPassword), out _)).IsTrue();
        await Assert.That(_outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task ChangeEmail_AddressOfAnotherAccount_AnswersLikeFreeAddressAndSendsNoticeWithoutCode()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        ApplicationUser other = await CreateUserAsync(confirmed: true);
        string cookie = await LoginAsync(client, Address(user), Password);

        using HttpResponseMessage taken = await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(Address(other), Password));
        using HttpResponseMessage free = await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(NewEmail(), Password));

        await Assert.That(taken.StatusCode).IsEqualTo(free.StatusCode);
        await Assert.That(await taken.Content.ReadAsStringAsync()).IsEqualTo(await free.Content.ReadAsStringAsync());
        await Assert.That(CodeIn(_outbox.Single(Address(other)))).IsNull();
    }

    [Test]
    public async Task ConfirmEmailChange_AddressTakenAfterCodeSent_Returns400AndKeepsOldEmail()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string newEmail = NewEmail();
        string cookie = await LoginAsync(client, Address(user), Password);
        using (await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(newEmail, Password))) { }
        string code = LatestCode(newEmail);
        await CreateUserAsync(confirmed: true, newEmail);

        using HttpResponseMessage confirmed = await PostAsync(client, cookie, "/api/auth/email/confirm", new ConfirmEmailChangeRequest(newEmail, code));

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginAsync(client, Address(user), Password);
    }

    [Test]
    public async Task ConfirmEmailChange_PasswordResetAfterCodeSent_Returns400()
    {
        // A reset changes the security stamp, which voids codes sent before it.
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string newEmail = NewEmail();
        using (await ForgotAsync(client, Address(user))) { }
        string resetCode = LatestCode(Address(user));
        string cookie = await LoginAsync(client, Address(user), Password);
        using (await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(newEmail, Password))) { }
        string changeCode = LatestCode(newEmail);
        using (await ResetAsync(client, Address(user), resetCode, OtherPassword)) { }
        cookie = await LoginAsync(client, Address(user), OtherPassword);

        using HttpResponseMessage confirmed = await PostAsync(client, cookie, "/api/auth/email/confirm", new ConfirmEmailChangeRequest(newEmail, changeCode));

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ConfirmEmailChange_WrongCodesThenSignInAgain_CodeStaysDiscarded()
    {
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string newEmail = NewEmail();
        string cookie = await LoginAsync(client, Address(user), Password);
        using (await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(newEmail, Password))) { }
        string code = LatestCode(newEmail);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await PostAsync(client, cookie, "/api/auth/email/confirm",
                new ConfirmEmailChangeRequest(newEmail, WrongCode(code)));
            await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        // A successful sign-in clears the lockout counter; it must not give code guesses back.
        cookie = await LoginAsync(client, Address(user), Password);

        using HttpResponseMessage discarded = await PostAsync(client, cookie, "/api/auth/email/confirm",
            new ConfirmEmailChangeRequest(newEmail, code));

        await Assert.That(discarded.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await LoginAsync(client, Address(user), Password);
    }

    [Test]
    public async Task ConfirmEmailChange_DifferentlyCapitalizedAddress_ChangesEmail()
    {
        // Long addresses depend on SQL Server's column sizes, so AccountCodeStorageTests covers those.
        using HttpClient client = CreateClient();
        ApplicationUser user = await CreateUserAsync(confirmed: true);
        string newEmail = $"New.{Guid.NewGuid():N}@Example.com";
        string cookie = await LoginAsync(client, Address(user), Password);
        using (HttpResponseMessage started = await PostAsync(client, cookie, "/api/auth/email", new ChangeEmailRequest(newEmail, Password)))
        {
            await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        }

        using HttpResponseMessage confirmed = await PostAsync(client, cookie, "/api/auth/email/confirm",
            new ConfirmEmailChangeRequest(newEmail.ToLowerInvariant(), LatestCode(newEmail)));

        await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await LoginAsync(client, newEmail, Password);
    }

    [Test]
    public async Task ChangeEmail_Anonymous_Returns401()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/email", new ChangeEmailRequest(NewEmail(), Password));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private static string Address(ApplicationUser user) => user.Email ?? throw new InvalidOperationException("User has no email.");

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, password, "Lena", "Leader"));

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string password, string code) =>
        client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(email, password, code));

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/resend-confirmation", new EmailRequest(email));

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest(email));

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string code, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, code, newPassword));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string cookie, string path, object body) =>
        SendAsync(client, cookie, HttpMethod.Post, path, body);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string cookie, HttpMethod method, string path, object body)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(HeaderNames.Cookie, cookie);
        return await client.SendAsync(request);
    }

    private static async Task<bool> IsSignedInAsync(HttpClient client, string cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/user");
        request.Headers.Add(HeaderNames.Cookie, cookie);
        using HttpResponseMessage response = await client.SendAsync(request);
        UserInfo info = await response.Content.ReadFromJsonAsync<UserInfo>() ?? throw new InvalidOperationException("No user info.");
        return info.IsAuthenticated;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return ReadCookie(response);
    }

    /// <summary>Signs in to an unverified account, which answers 403 and emails a confirmation code.</summary>
    private static async Task LoginRequiresVerificationAsync(HttpClient client, string email, string password)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static string ReadCookie(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Where(value => value.StartsWith(CookieName, StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").Clone();
    }

    private string LatestCode(string email) =>
        CodeIn(_outbox.To(email)[^1]) ?? throw new InvalidOperationException($"The last email to {email} had no code.");

    private static string? CodeIn(EmailMessage message) =>
        CodePattern().Match(message.Body) is { Success: true } match ? match.Value : null;

    /// <summary>Issues the day's anonymous allowance of confirmation codes, as an attacker calling resend would.</summary>
    private Task UseUpDailyConfirmationCodesAsync(ApplicationUser created) =>
        IssueConfirmationCodesAsync(created, EmailCodeTokenProvider.MaxIssuedPerDay);

    /// <summary>Issues <paramref name="count"/> confirmation codes, then waits out the email cooldown.</summary>
    private async Task IssueConfirmationCodesAsync(ApplicationUser created, int count)
    {
        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await users.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
            for (int sent = 0; sent < count; sent++)
            {
                await users.GenerateEmailConfirmationTokenAsync(user);
            }
        });
        Time.Advance(TimeSpan.FromMinutes(2));
    }

    private async Task<bool> CanIssueConfirmationAsync(ApplicationUser created)
    {
        bool canIssue = false;
        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser user = await users.FindByIdAsync(created.Id) ?? throw new InvalidOperationException("Not stored.");
            canIssue = await services.GetRequiredService<EmailCodeTokenProvider>()
                .CanIssueAsync(users, user, UserManager<ApplicationUser>.ConfirmEmailTokenPurpose);
        });
        return canIssue;
    }

    private async Task<ApplicationUser> CreateUserAsync(bool confirmed, string? email = null)
    {
        email ??= NewEmail();
        ApplicationUser user = new() { UserName = email, Email = email, EmailConfirmed = confirmed, FirstName = "Ada", LastName = "Account" };
        await InScopeAsync(async services =>
        {
            IdentityResult result = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Password);
            await Assert.That(result.Succeeded).IsTrue();
        });
        return user;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    private sealed class CapturingOutbox : IEmailOutbox
    {
        private readonly ConcurrentQueue<EmailMessage> _messages = new();

        public IReadOnlyList<EmailMessage> Messages => [.. _messages];

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _messages.Enqueue(message);
            return ValueTask.CompletedTask;
        }

        public IReadOnlyList<EmailMessage> To(string email) =>
            [.. _messages.Where(message => string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase))];

        public EmailMessage Single(string email) => To(email) is [var only]
            ? only
            : throw new InvalidOperationException($"Expected one email to {email}, found {To(email).Count}.");
    }
}
