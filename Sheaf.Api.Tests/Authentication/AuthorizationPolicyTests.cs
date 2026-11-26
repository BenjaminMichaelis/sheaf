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
    private const string CallerHeader = "X-Caller";

    public enum Caller
    {
        Anonymous,
        AccountOnly,
        Leader,
        Coordinator,
        Staff,
        Admin,
        CidOnly,
        MissingMembership,
        MissingRole,
        BadRole,
        StrayRoleClaim
    }

    public static IEnumerable<(ChurchMembershipRole Role, bool Expected)> ManageMembershipsExpectations() =>
        Enum.GetValues<ChurchMembershipRole>().Select(role => (role, role switch
        {
            ChurchMembershipRole.Admin => true,
            ChurchMembershipRole.Staff => false,
            ChurchMembershipRole.Coordinator => false,
            ChurchMembershipRole.Leader => false,
            _ => throw new NotSupportedException($"Decide whether {role} holds ManageMemberships.")
        }));

    [Test]
    [MethodDataSource(nameof(ManageMembershipsExpectations))]
    public async Task HasPermission_ManageMemberships_MatchesExpectedRoles(ChurchMembershipRole role, bool expected)
    {
        await Assert.That(RolePermissions.HasPermission(role, Permission.ManageMemberships)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetPolicyAsync_KnownPermission_DeniesUnauthenticatedIdentityWithAdminChurchClaims()
    {
        IAuthorizationPolicyProvider provider = CreateProvider();
        IAuthorizationService authorization = CreateAuthorizationService();
        AuthorizationPolicy? policy = await provider.GetPolicyAsync(SheafPolicies.ForPermission(Permission.ManageMemberships));
        ClaimsPrincipal unauthenticated = new(new ClaimsIdentity(
            CreatePrincipal(ChurchMembershipRole.Admin).Claims, authenticationType: null, ClaimTypes.Name, SheafClaimTypes.Role));

        AuthorizationPolicy requiredPolicy = await Assert.That(policy).IsNotNull();
        AuthorizationResult denied = await authorization.AuthorizeAsync(unauthenticated, null, requiredPolicy);
        AuthorizationResult allowed = await authorization.AuthorizeAsync(CreatePrincipal(ChurchMembershipRole.Admin), null, requiredPolicy);

        await Assert.That(denied.Succeeded).IsFalse();
        await Assert.That(allowed.Succeeded).IsTrue();
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

        AuthorizationPolicy requiredPolicy = await Assert.That(policy).IsNotNull();
        AuthorizationResult result = await authorization.AuthorizeAsync(CreatePrincipal(ChurchMembershipRole.Admin), null, requiredPolicy);
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
    [MethodDataSource(nameof(ManageMembershipsExpectations))]
    public async Task HandleAsync_ManageMembershipsRequirement_FollowsRolePermissions(ChurchMembershipRole role, bool expected)
    {
        IAuthorizationService authorization = CreateAuthorizationService();

        AuthorizationResult result = await authorization.AuthorizeAsync(
            CreatePrincipal(role), null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ChurchMembershipRole.Leader, ChurchMembershipRole.Coordinator)]
    [Arguments(ChurchMembershipRole.Coordinator, ChurchMembershipRole.Staff)]
    [Arguments(ChurchMembershipRole.Staff, ChurchMembershipRole.Admin)]
    public async Task HasPermission_HigherRole_IncludesEveryLowerRolePermission(ChurchMembershipRole lower, ChurchMembershipRole higher)
    {
        Permission[] missing = [.. Enum.GetValues<Permission>()
            .Where(permission => RolePermissions.HasPermission(lower, permission) && !RolePermissions.HasPermission(higher, permission))];

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [Arguments(ChurchMembershipRole.Leader, false)]
    [Arguments(ChurchMembershipRole.Coordinator, false)]
    [Arguments(ChurchMembershipRole.Staff, true)]
    [Arguments(ChurchMembershipRole.Admin, true)]
    public async Task HasPermission_PeopleManage_MatchesExpectedRoles(ChurchMembershipRole role, bool expected) =>
        await Assert.That(RolePermissions.HasPermission(role, Permission.PeopleManage)).IsEqualTo(expected);

    [Test]
    [Arguments(ChurchMembershipRole.Leader, false)]
    [Arguments(ChurchMembershipRole.Coordinator, false)]
    [Arguments(ChurchMembershipRole.Staff, false)]
    [Arguments(ChurchMembershipRole.Admin, true)]
    public async Task HasPermission_ManageChurch_MatchesExpectedRoles(ChurchMembershipRole role, bool expected) =>
        await Assert.That(RolePermissions.HasPermission(role, Permission.ManageChurch)).IsEqualTo(expected);

    [Test]
    public async Task HandleAsync_StoredRoleLosesPermission_FailsDespiteRoleClaim()
    {
        IAuthorizationService authorization = CreateAuthorizationService(new StoredRoles(_ => ChurchMembershipRole.Leader));

        AuthorizationResult result = await authorization.AuthorizeAsync(
            CreatePrincipal(ChurchMembershipRole.Admin), null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task HandleAsync_MembershipNoLongerStored_Fails()
    {
        IAuthorizationService authorization = CreateAuthorizationService(new StoredRoles(_ => null));

        AuthorizationResult result = await authorization.AuthorizeAsync(
            CreatePrincipal(ChurchMembershipRole.Admin), null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task HandleAsync_MissingUserId_Fails()
    {
        IAuthorizationService authorization = CreateAuthorizationService();

        AuthorizationResult result = await authorization.AuthorizeAsync(
            With(CreatePrincipal(ChurchMembershipRole.Admin), ClaimTypes.NameIdentifier), null, new PermissionRequirement(Permission.ManageMemberships));

        await Assert.That(result.Succeeded).IsFalse();
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
    [Arguments("/api/church/ping", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/ping", Caller.AccountOnly, HttpStatusCode.Forbidden)]
    [Arguments("/api/church/ping", Caller.Leader, HttpStatusCode.OK)]
    [Arguments("/api/church/unmapped", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/unmapped", Caller.AccountOnly, HttpStatusCode.Forbidden)]
    [Arguments("/fallback", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/fallback", Caller.AccountOnly, HttpStatusCode.Forbidden)]
    [Arguments("/fallback", Caller.Leader, HttpStatusCode.OK)]
    [Arguments("/account", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/account", Caller.AccountOnly, HttpStatusCode.OK)]
    [Arguments("/authenticated", Caller.AccountOnly, HttpStatusCode.OK)]
    [Arguments("/public", Caller.Anonymous, HttpStatusCode.OK)]
    [Arguments("/api/church/memberships", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/api/church/memberships", Caller.AccountOnly, HttpStatusCode.Forbidden)]
    [Arguments("/standalone-permission", Caller.Anonymous, HttpStatusCode.Unauthorized)]
    [Arguments("/standalone-permission", Caller.AccountOnly, HttpStatusCode.Forbidden)]
    public async Task Request_PolicyScenario_ReturnsExpectedStatus(string path, Caller caller, HttpStatusCode expected)
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add(CallerHeader, caller.ToString());

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(ManageMembershipsExpectations))]
    public async Task Request_PermissionRoutes_FollowRolePermissions(ChurchMembershipRole role, bool allowed)
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();
        HttpStatusCode expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;

        foreach (string path in new[] { "/api/church/memberships", "/standalone-permission" })
        {
            using HttpRequestMessage request = new(HttpMethod.Get, path);
            request.Headers.Add(CallerHeader, role.ToString());

            using HttpResponseMessage response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments("/api/church/ping", Caller.CidOnly)]
    [Arguments("/api/church/ping", Caller.MissingMembership)]
    [Arguments("/api/church/ping", Caller.MissingRole)]
    [Arguments("/api/church/ping", Caller.BadRole)]
    [Arguments("/api/church/ping", Caller.StrayRoleClaim)]
    [Arguments("/fallback", Caller.CidOnly)]
    [Arguments("/fallback", Caller.MissingMembership)]
    [Arguments("/fallback", Caller.MissingRole)]
    [Arguments("/fallback", Caller.BadRole)]
    [Arguments("/fallback", Caller.StrayRoleClaim)]
    public async Task Request_IncoherentChurchClaims_ReturnsForbidden(string path, Caller caller)
    {
        await using WebApplication app = await StartAppAsync();
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add(CallerHeader, caller.ToString());

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Request_ChurchGroupWithoutFallback_ForbidsAccountOnlyUser()
    {
        await using WebApplication app = await StartAppAsync(withoutFallback: true);
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/church/ping");
        request.Headers.Add(CallerHeader, nameof(Caller.AccountOnly));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static IAuthorizationPolicyProvider CreateProvider() =>
        new ServiceCollection().AddLogging().AddSingleton<IChurchMembershipRoles>(new StoredRoles()).AddSheafAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();

    private static IAuthorizationService CreateAuthorizationService(StoredRoles? stored = null) =>
        new ServiceCollection().AddLogging().AddSingleton<IChurchMembershipRoles>(stored ?? new StoredRoles()).AddSheafAuthorization().BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal CreatePrincipal(ChurchMembershipRole role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-id"),
            new Claim(SheafClaimTypes.ChurchId, Guid.NewGuid().ToString("D")),
            new Claim(SheafClaimTypes.ChurchMembershipId, Guid.NewGuid().ToString("D")),
            new Claim(SheafClaimTypes.Role, role.ToString())
        ], TestScheme, ClaimTypes.Name, SheafClaimTypes.Role));

    private static ClaimsPrincipal Account() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], TestScheme));

    private static ClaimsPrincipal With(ClaimsPrincipal principal, params string[] removedTypes) =>
        new(new ClaimsIdentity(
            principal.Claims.Where(claim => !removedTypes.Contains(claim.Type)), TestScheme, ClaimTypes.Name, SheafClaimTypes.Role));

    private static ClaimsPrincipal Replace(ClaimsPrincipal principal, Claim claim) =>
        new(new ClaimsIdentity(
            principal.Claims.Where(existing => claim.Type == ClaimTypes.Role || existing.Type != claim.Type).Append(claim),
            TestScheme, ClaimTypes.Name, SheafClaimTypes.Role));

    private static async Task<WebApplication> StartAppAsync(bool withoutFallback = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChurchMembershipRoles>(new StoredRoles());
        builder.Services.AddSheafAuthorization();
        if (withoutFallback)
        {
            builder.Services.Configure<AuthorizationOptions>(options => options.FallbackPolicy = null);
        }
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

    /// <summary>Stands in for persisted memberships; by default the stored role matches the role claim.</summary>
    private sealed class StoredRoles(Func<ChurchSelection, ChurchMembershipRole?>? lookup = null) : IChurchMembershipRoles
    {
        public Task<ChurchMembershipRole?> GetRoleAsync(string userId, ChurchSelection selection, CancellationToken ct) =>
            Task.FromResult(lookup is null ? selection.Role : lookup(selection));
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(CallerHeader, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Caller caller = Enum.Parse<Caller>(value.ToString());
            if (caller == Caller.Anonymous)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsPrincipal principal = caller switch
            {
                Caller.AccountOnly => Account(),
                Caller.CidOnly => With(CreatePrincipal(ChurchMembershipRole.Admin), SheafClaimTypes.ChurchMembershipId, SheafClaimTypes.Role),
                Caller.MissingMembership => With(CreatePrincipal(ChurchMembershipRole.Admin), SheafClaimTypes.ChurchMembershipId),
                Caller.MissingRole => With(CreatePrincipal(ChurchMembershipRole.Admin), SheafClaimTypes.Role),
                Caller.BadRole => Replace(CreatePrincipal(ChurchMembershipRole.Admin), new Claim(SheafClaimTypes.Role, "admin")),
                Caller.StrayRoleClaim => Replace(CreatePrincipal(ChurchMembershipRole.Admin), new Claim(ClaimTypes.Role, "Admin")),
                var role => CreatePrincipal(Enum.Parse<ChurchMembershipRole>(role.ToString()))
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
        }
    }
}
