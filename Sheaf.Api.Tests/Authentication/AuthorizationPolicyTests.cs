using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sheaf.Authorization;
using Sheaf.Core.Authentication;
using Sheaf.Data;
using Sheaf.Endpoints;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests.Authentication;

public class AuthorizationPolicyTests
{
    private const string TestScheme = "Test";
    private const string RoleHeader = "X-Role";
    private const string AccountOnlyValue = "account";

    [Test]
    [Arguments(ChurchMembershipRole.Admin, true)]
    [Arguments(ChurchMembershipRole.Staff, false)]
    [Arguments(ChurchMembershipRole.Coordinator, false)]
    [Arguments(ChurchMembershipRole.Leader, false)]
    public async Task HasPermission_ManageMemberships_AllowsOnlyAdmin(ChurchMembershipRole role, bool expected)
    {
        await Assert.That(RolePermissions.HasPermission(role, Permission.ManageMemberships)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetPolicyAsync_KnownPermission_RequiresChurchMemberAndPermission()
    {
        IAuthorizationPolicyProvider provider = CreateProvider();

        AuthorizationPolicy? policy = await provider.GetPolicyAsync(SheafPolicies.ForPermission(Permission.ManageMemberships));

        await Assert.That(policy).IsNotNull();
        await Assert.That(policy!.Requirements.OfType<PermissionRequirement>().Single().Permission)
            .IsEqualTo(Permission.ManageMemberships);
        await Assert.That(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Count()).IsEqualTo(1);
        await Assert.That(policy.Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Any(requirement => requirement.ClaimType == SheafClaimTypes.ChurchId)).IsTrue();
    }

    [Test]
    [Arguments("Permission:Nope")]
    [Arguments("Permission:")]
    [Arguments("Permission:0")]
    [Arguments("Permission:managememberships")]
    [Arguments("Permission: ManageMemberships")]
    public async Task GetPolicyAsync_UnknownOrMalformedPermission_DeniesEvenChurchAdmin(string policyName)
    {
        IAuthorizationPolicyProvider provider = CreateProvider();
        IAuthorizationService authorization = CreateAuthorizationService();
        AuthorizationPolicy? policy = await provider.GetPolicyAsync(policyName);

        await Assert.That(policy).IsNotNull();
        AuthorizationResult result = await authorization.AuthorizeAsync(CreatePrincipal(ChurchMembershipRole.Admin), null, policy!);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task GetPolicyAsync_NonPermissionName_DelegatesToRegisteredPolicies()
    {
        IAuthorizationPolicyProvider provider = CreateProvider();

        await Assert.That(await provider.GetPolicyAsync(SheafPolicies.ChurchMember)).IsNotNull();
        await Assert.That(await provider.GetPolicyAsync(SheafPolicies.Authenticated)).IsNotNull();
        await Assert.That(await provider.GetPolicyAsync("Missing")).IsNull();
        await Assert.That(await provider.GetFallbackPolicyAsync()).IsNotNull();
    }

    [Test]
    [Arguments(ChurchMembershipRole.Admin, true)]
    [Arguments(ChurchMembershipRole.Staff, false)]
    public async Task HandleAsync_ManageMembershipsRequirement_FollowsRolePermissions(ChurchMembershipRole role, bool expected)
    {
        IAuthorizationService authorization = CreateAuthorizationService();

        AuthorizationResult result = await authorization.AuthorizeAsync(
            CreatePrincipal(role), null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsEqualTo(expected);
    }

    [Test]
    public async Task HandleAsync_InvalidChurchClaims_Fails()
    {
        IAuthorizationService authorization = CreateAuthorizationService();
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(SheafClaimTypes.ChurchId, Guid.NewGuid().ToString("D")), new Claim(SheafClaimTypes.Role, "Admin")],
            TestScheme, ClaimTypes.Name, SheafClaimTypes.Role));

        AuthorizationResult result = await authorization.AuthorizeAsync(
            principal, null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    [Arguments("/api/church/ping", null, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/ping", AccountOnlyValue, HttpStatusCode.Forbidden)]
    [Arguments("/api/church/ping", nameof(ChurchMembershipRole.Leader), HttpStatusCode.OK)]
    [Arguments("/api/church/unmapped", null, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/unmapped", AccountOnlyValue, HttpStatusCode.Forbidden)]
    [Arguments("/fallback", null, HttpStatusCode.Unauthorized)]
    [Arguments("/fallback", AccountOnlyValue, HttpStatusCode.Forbidden)]
    [Arguments("/fallback", nameof(ChurchMembershipRole.Leader), HttpStatusCode.OK)]
    [Arguments("/account", null, HttpStatusCode.Unauthorized)]
    [Arguments("/account", AccountOnlyValue, HttpStatusCode.OK)]
    [Arguments("/authenticated", AccountOnlyValue, HttpStatusCode.OK)]
    [Arguments("/public", null, HttpStatusCode.OK)]
    [Arguments("/api/church/memberships", null, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/memberships", AccountOnlyValue, HttpStatusCode.Forbidden)]
    [Arguments("/api/church/memberships", nameof(ChurchMembershipRole.Staff), HttpStatusCode.Forbidden)]
    [Arguments("/api/church/memberships", nameof(ChurchMembershipRole.Admin), HttpStatusCode.OK)]
    [Arguments("/standalone-permission", null, HttpStatusCode.Unauthorized)]
    [Arguments("/standalone-permission", AccountOnlyValue, HttpStatusCode.Forbidden)]
    [Arguments("/standalone-permission", nameof(ChurchMembershipRole.Staff), HttpStatusCode.Forbidden)]
    [Arguments("/standalone-permission", nameof(ChurchMembershipRole.Admin), HttpStatusCode.OK)]
    public async Task Request_PolicyScenario_ReturnsExpectedStatus(string path, string? role, HttpStatusCode expected)
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    private static IAuthorizationPolicyProvider CreateProvider() =>
        new ServiceCollection().AddLogging().AddSheafAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();

    private static IAuthorizationService CreateAuthorizationService() =>
        new ServiceCollection().AddLogging().AddSheafAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal CreatePrincipal(ChurchMembershipRole role) =>
        new(new ClaimsIdentity(
        [
            new Claim(SheafClaimTypes.ChurchId, Guid.NewGuid().ToString("D")),
            new Claim(SheafClaimTypes.ChurchMembershipId, Guid.NewGuid().ToString("D")),
            new Claim(SheafClaimTypes.Role, role.ToString())
        ], TestScheme, ClaimTypes.Name, SheafClaimTypes.Role));

    private static async Task<WebApplication> StartAppAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSheafAuthorization();
        builder.Services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(TestScheme, null);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/public", () => "ok").AllowAnonymous();
        app.MapGet("/fallback", () => "ok");
        app.MapGet("/account", () => "ok").RequireAuthorization();
        app.MapGet("/authenticated", () => "ok").RequireAuthorization(SheafPolicies.Authenticated);
        RouteGroupBuilder church = app.MapChurchGroup();
        church.MapGet("/ping", () => "ok");
        church.MapGet("/memberships", () => "ok").RequirePermission(Permission.ManageMemberships);
        app.MapGet("/standalone-permission", () => "ok").RequirePermission(Permission.ManageMemberships);

        await app.StartAsync();
        return app;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsPrincipal principal = value == AccountOnlyValue
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], TestScheme))
                : CreatePrincipal(Enum.Parse<ChurchMembershipRole>(value.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
        }
    }
}
