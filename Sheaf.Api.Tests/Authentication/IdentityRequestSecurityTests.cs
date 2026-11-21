using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Sheaf.Data;
using Sheaf.Data.Security;
using Sheaf.Data.Tenancy;
using Sheaf.Endpoints;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests.Authentication;

public class IdentityRequestSecurityTests : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    private const string Password = "Security-test-Password1!";
    private const string CookieName = "__Host-sheaf";
    private readonly AdjustableTimeProvider _time = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        string database = Guid.NewGuid().ToString("N");
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database)
            .UseMultiTenancy().UseMembershipSecurity());
        services.AddSingleton<TimeProvider>(_time);
    }

    [Test]
    public async Task Login_RepeatedWrongPassword_LocksOutAndReturns401()
    {
        ApplicationUser user = await CreateUserAsync();
        using HttpClient client = CreateClient();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(user.Email ?? throw new InvalidOperationException("User has no email."), "wrong-password"));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEmpty();
            await Assert.That(response.Headers.Contains(HeaderNames.SetCookie)).IsFalse();
        }

        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> manager = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser stored = await manager.FindByIdAsync(user.Id)
                ?? throw new InvalidOperationException("The test account was not persisted.");
            await Assert.That(await manager.IsLockedOutAsync(stored)).IsTrue();
            DateTimeOffset until = await Assert.That(await manager.GetLockoutEndDateAsync(stored)).IsNotNull();
            await Assert.That(until - DateTimeOffset.UtcNow).IsBetween(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        });

        using HttpResponseMessage correctPassword = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Email ?? throw new InvalidOperationException("User has no email."), Password));
        using HttpResponseMessage unknownAccount = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"{Guid.NewGuid():N}@example.com", Password));
        await Assert.That(correctPassword.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(unknownAccount.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await correctPassword.Content.ReadAsStringAsync()).IsEqualTo(await unknownAccount.Content.ReadAsStringAsync());
        await Assert.That(correctPassword.Headers.Contains(HeaderNames.SetCookie)).IsFalse();
    }

    [Test]
    public async Task Login_RememberMe_IssuesSecureHostCookieWithFourteenDaySlidingLifetime()
    {
        ApplicationUser user = await CreateUserAsync();
        using HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Email ?? throw new InvalidOperationException("User has no email."), Password, true));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        SetCookieHeaderValue header = SetCookieHeaderValue.Parse(response.Headers.GetValues(HeaderNames.SetCookie)
            .Single(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal)));
        await Assert.That(header.Secure).IsTrue();
        await Assert.That(header.HttpOnly).IsTrue();
        await Assert.That(header.Path.ToString()).IsEqualTo("/");
        await Assert.That(header.Domain.HasValue).IsFalse();
        AuthenticationTicket ticket = ReadTicket(ReadCookie(response));
        DateTimeOffset issued = await Assert.That(ticket.Properties.IssuedUtc).IsNotNull();
        DateTimeOffset expires = await Assert.That(ticket.Properties.ExpiresUtc).IsNotNull();
        await Assert.That(expires - issued).IsEqualTo(TimeSpan.FromDays(14));

        _time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        using HttpResponseMessage renewed = await GetUserAsync(client, ReadCookie(response));
        await Assert.That(renewed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        AuthenticationTicket renewedTicket = ReadTicket(ReadCookie(renewed));
        DateTimeOffset renewedExpiry = await Assert.That(renewedTicket.Properties.ExpiresUtc).IsNotNull();
        await Assert.That(renewedExpiry).IsGreaterThan(expires);
    }

    private HttpClient CreateClient() => Factory.CreateDefaultClient(new Uri("https://localhost"));

    private async Task<ApplicationUser> CreateUserAsync()
    {
        string email = $"{Guid.NewGuid():N}@example.com";
        ApplicationUser user = new() { UserName = email, Email = email, EmailConfirmed = true };
        await InScopeAsync(async services =>
        {
            IdentityResult result = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Password);
            await Assert.That(result.Succeeded).IsTrue();
        });
        return user;
    }

    private async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    private static async Task<HttpResponseMessage> GetUserAsync(HttpClient client, string cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/user");
        request.Headers.Add(HeaderNames.Cookie, cookie);
        return await client.SendAsync(request);
    }

    private static string ReadCookie(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Where(value => value.StartsWith(CookieName, StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

    private AuthenticationTicket ReadTicket(string cookie)
    {
        DefaultHttpContext http = new();
        http.Request.Headers.Cookie = cookie;
        CookieAuthenticationOptions options = Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        string value = options.CookieManager.GetRequestCookie(http, CookieName)
            ?? throw new InvalidOperationException("The response contained no Identity cookie.");
        return options.TicketDataFormat.Unprotect(value)
            ?? throw new InvalidOperationException("The Identity cookie could not be unprotected.");
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
