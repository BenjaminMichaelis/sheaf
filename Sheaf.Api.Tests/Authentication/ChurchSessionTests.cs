using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Sheaf.Core.Authentication;
using Sheaf.Core.Tests;
using Sheaf.Data;
using Sheaf.Endpoints;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

public class ChurchSessionTests : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    private const string Password = "Church-session-Password1!";
    private const string CookieName = ".AspNetCore.Identity.Application";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<ChurchMembershipRole>() }
    };

    private readonly ControlledTimeProvider _time = new();
    private readonly ConcurrentQueue<DirectoryRead> _auditReads = new();
    private readonly QueryRecorder _queries = new();
    private string _connectionString = "";

    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    protected override Task SetupAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(Sql.ConnectionString)
        {
            InitialCatalog = $"SheafChurchSessions_{Guid.NewGuid():N}"
        }.ConnectionString;
        return Task.CompletedTask;
    }

    protected override void ConfigureTestConfiguration(IConfigurationBuilder config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _connectionString
        });

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ILoggerProvider>(new DirectoryAuditProvider(_auditReads));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseAzureSql(_connectionString).AddInterceptors(_queries));
    }

    [Before(Test)]
    public async Task MigrateAsync()
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    [Test]
    public async Task Login_MemberOfOneChurch_ReturnsChurchAndRole()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user, ChurchMembershipRole.Coordinator);
        using HttpClient client = CreateClient();

        (UserInfo info, string cookie) = await LoginAsync(client, user);
        await Assert.That(info.UserId).IsEqualTo(user.Id);
        await Assert.That(info.CurrentChurch?.ChurchId).IsEqualTo(membership.ChurchId);
        await Assert.That(info.CurrentChurch?.Role).IsEqualTo(ChurchMembershipRole.Coordinator);
        await Assert.That(info.Churches.Select(church => church.ChurchId)).IsEquivalentTo([membership.ChurchId]);
        await AssertSelectionAsync(ReadTicket(cookie).Principal, membership);

        using HttpResponseMessage response = await GetUserAsync(client, cookie);
        UserInfo roundtrip = await ReadUserAsync(response);
        await Assert.That(roundtrip.IsAuthenticated).IsTrue();
        await Assert.That(roundtrip.CurrentChurch).IsEqualTo(info.CurrentChurch);
        using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(content.RootElement.GetProperty("currentChurch").GetProperty("role").GetString())
            .IsEqualTo("Coordinator");
        await Assert.That(_queries.Commands.Any(command => command.Contains("[ChurchId] = @", StringComparison.Ordinal)
            && command.Contains("FROM [ChurchMemberships]", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Login_WithoutMembership_HasNoChurchAccess()
    {
        ApplicationUser user = await CreateUserAsync();
        using HttpClient client = CreateClient();

        (UserInfo info, string cookie) = await LoginAsync(client, user);

        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(info.Churches).IsEmpty();
        await AssertUnselectedAsync(ReadTicket(cookie).Principal, user);
        using HttpResponseMessage response = await GetUserAsync(client, cookie);
        UserInfo roundtrip = await ReadUserAsync(response);
        await Assert.That(roundtrip.IsAuthenticated).IsTrue();
        await Assert.That(roundtrip.CurrentChurch).IsNull();
        using HttpRequestMessage logout = new(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add(HeaderNames.Cookie, cookie);
        using HttpResponseMessage logoutResponse = await client.SendAsync(logout);
        await Assert.That(logoutResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Login_MultipleMemberships_ReturnsChurchListWithoutCurrentChurch()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership first = await AddMembershipAsync(user);
        ChurchMembership second = await AddMembershipAsync(user, ChurchMembershipRole.Staff);
        using HttpClient client = CreateClient();

        (UserInfo info, string cookie) = await LoginAsync(client, user);

        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(info.Churches.Select(church => church.ChurchId)).IsEquivalentTo([first.ChurchId, second.ChurchId]);
        await AssertUnselectedAsync(ReadTicket(cookie).Principal, user);
        using HttpResponseMessage response = await GetUserAsync(client, cookie);
        await Assert.That((await ReadUserAsync(response)).CurrentChurch).IsNull();
    }

    [Test]
    public async Task Register_NewAccount_ReturnsAuthenticatedUnselectedCookie()
    {
        using HttpClient client = CreateClient();
        string email = $"{Guid.NewGuid():N}@example.com";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, Password, Password));
        UserInfo info = await ReadUserAsync(response);
        ClaimsPrincipal principal = ReadTicket(ReadCookie(response)).Principal;

        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That(info.Email).IsEqualTo(email);
        await Assert.That(info.Churches).IsEmpty();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(principal.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo(info.UserId);
        await Assert.That(principal.ReadChurchClaims(out ChurchSelection? selection)).IsEqualTo(ChurchClaimState.Unselected);
        await Assert.That(selection).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Login_AnotherAccountIsSignedIn_UsesOnlyNewAccountsMemberships(bool newAccountHasMembership)
    {
        ApplicationUser previous = await CreateUserAsync();
        ChurchMembership previousMembership = await AddMembershipAsync(previous, ChurchMembershipRole.Admin);
        ApplicationUser next = await CreateUserAsync();
        ChurchMembership? nextMembership = newAccountHasMembership ? await AddMembershipAsync(next) : null;
        using HttpClient client = CreateClient();
        (_, string previousCookie) = await LoginAsync(client, previous);

        (UserInfo info, string nextCookie) = await LoginAsync(client, next, previousCookie);

        await Assert.That(info.UserId).IsEqualTo(next.Id);
        await Assert.That(info.CurrentChurch?.ChurchId).IsEqualTo(nextMembership?.ChurchId);
        await Assert.That(info.Churches.Any(church => church.ChurchId == previousMembership.ChurchId)).IsFalse();
        ClaimsPrincipal principal = ReadTicket(nextCookie).Principal;
        if (nextMembership is not null)
        {
            await AssertSelectionAsync(principal, nextMembership);
        }
        else
        {
            await AssertUnselectedAsync(principal, next);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateAsync_StoredChurchAuthority_ReplacesOrOmitsAuthorityAndPreservesIdentityClaims(bool hasMembership)
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership? membership = hasMembership ? await AddMembershipAsync(user) : null;
        await InScopeAsync(async services =>
        {
            UserManager<ApplicationUser> manager = services.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser stored = await manager.FindByIdAsync(user.Id)
                ?? throw new InvalidOperationException("The test account was not persisted.");
            IdentityResult result = await manager.AddClaimsAsync(stored,
            [
                new Claim(SheafClaimTypes.ChurchId, Guid.NewGuid().ToString("D")),
                new Claim(SheafClaimTypes.ChurchMembershipId, Guid.NewGuid().ToString("D")),
                new Claim(SheafClaimTypes.Role, "Admin"),
                new Claim(SheafClaimTypes.Role, "Staff"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("preference", "retained")
            ]);
            await Assert.That(result.Succeeded).IsTrue();
        });
        using HttpClient client = CreateClient();

        (_, string cookie) = await LoginAsync(client, user);
        ClaimsPrincipal principal = ReadTicket(cookie).Principal;

        if (membership is not null)
        {
            await AssertSelectionAsync(principal, membership);
        }
        else
        {
            await AssertUnselectedAsync(principal, user);
        }
        await Assert.That(principal.FindFirstValue("preference")).IsEqualTo("retained");
        await Assert.That(principal.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo(user.Id);
        await Assert.That(principal.FindFirstValue("AspNet.Identity.SecurityStamp")).IsEqualTo(user.SecurityStamp);
        await Assert.That(principal.FindFirstValue(ClaimTypes.Email)).IsEqualTo(user.Email);
        await Assert.That(principal.FindAll(ClaimTypes.Role)).IsEmpty();
        await Assert.That(principal.IsInRole("Admin")).IsFalse();
    }

    [Test]
    public async Task ValidatePrincipal_SelectedMembershipRoleChanged_RenewsWithCurrentRoleAfterFiveMinutes()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        await InChurchAsync(membership.ChurchId, async db =>
        {
            ChurchMembership stored = await db.ChurchMemberships.SingleAsync();
            stored.Role = ChurchMembershipRole.Staff;
            await db.SaveChangesAsync();
        });

        _time.Advance(TimeSpan.FromMinutes(5));
        using HttpResponseMessage before = await GetUserAsync(client, cookie);
        await Assert.That(before.Headers.Contains(HeaderNames.SetCookie)).IsFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage renewed = await GetUserAsync(client, cookie);
        string renewedCookie = ReadCookie(renewed);
        membership.Role = ChurchMembershipRole.Staff;
        await AssertSelectionAsync(ReadTicket(renewedCookie).Principal, membership);
        UserInfo info = await ReadUserAsync(renewed);
        await Assert.That(info.CurrentChurch?.Role).IsEqualTo(ChurchMembershipRole.Staff);
    }

    [Test]
    public async Task ValidatePrincipal_SelectedMembershipRemoved_RemainsUnselectedAcrossLaterRenewals()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership removed = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        string? stamp = user.SecurityStamp;
        ChurchMembership remaining = await AddMembershipAsync(user, ChurchMembershipRole.Coordinator);
        await InChurchAsync(removed.ChurchId, db => db.ChurchMemberships.ExecuteDeleteAsync());

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using HttpResponseMessage renewal = await GetUserAsync(client, cookie);
        string renewedCookie = ReadCookie(renewal);
        UserInfo info = await ReadUserAsync(renewal);
        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(info.Churches.Select(church => church.ChurchId)).IsEquivalentTo([remaining.ChurchId]);
        await AssertUnselectedAsync(ReadTicket(renewedCookie).Principal, user);
        await Assert.That(ReadTicket(renewedCookie).Principal.FindFirstValue("AspNet.Identity.SecurityStamp")).IsEqualTo(stamp);

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using HttpResponseMessage laterRenewal = await GetUserAsync(client, renewedCookie);
        await Assert.That((await ReadUserAsync(laterRenewal)).CurrentChurch).IsNull();
        await AssertUnselectedAsync(ReadTicket(ReadCookie(laterRenewal)).Principal, user);

        (UserInfo fresh, _) = await LoginAsync(client, user, ReadCookie(laterRenewal));
        await Assert.That(fresh.CurrentChurch?.ChurchId).IsEqualTo(remaining.ChurchId);
    }

    [Test]
    public async Task ValidatePrincipal_UnselectedAccountGainsOneMembership_DoesNotAutoSelectOnRenewal()
    {
        ApplicationUser user = await CreateUserAsync();
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        ChurchMembership membership = await AddMembershipAsync(user);

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using HttpResponseMessage response = await GetUserAsync(client, cookie);
        UserInfo info = await ReadUserAsync(response);

        await Assert.That(info.IsAuthenticated).IsTrue();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(info.Churches.Select(church => church.ChurchId)).IsEquivalentTo([membership.ChurchId]);
        await AssertUnselectedAsync(ReadTicket(ReadCookie(response)).Principal, user);
    }

    [Test]
    [Arguments("missing-membership")]
    [Arguments("duplicate-church")]
    [Arguments("empty-church")]
    [Arguments("invalid-role")]
    public async Task ValidatePrincipal_MalformedCookie_RejectsWithoutDirectoryOrAutoSelection(string scenario)
    {
        ApplicationUser user = await CreateUserAsync();
        _ = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        AuthenticationTicket ticket = ReadTicket(cookie);
        ClaimsIdentity identity = ticket.Principal.Identities.Single();
        switch (scenario)
        {
            case "missing-membership":
                identity.RemoveClaim(identity.FindFirst(SheafClaimTypes.ChurchMembershipId)
                    ?? throw new InvalidOperationException("Missing test membership claim."));
                break;
            case "duplicate-church":
                identity.AddClaim(new Claim(SheafClaimTypes.ChurchId, Guid.NewGuid().ToString("D")));
                break;
            case "empty-church":
            case "invalid-role":
                string type = scenario == "empty-church" ? SheafClaimTypes.ChurchId : SheafClaimTypes.Role;
                identity.RemoveClaim(identity.FindFirst(type)
                    ?? throw new InvalidOperationException("Missing test church claim."));
                identity.AddClaim(new Claim(type, scenario == "empty-church" ? Guid.Empty.ToString("D") : "Owner"));
                break;
        }

        int auditCount = _auditReads.Count;
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using HttpResponseMessage response = await GetUserAsync(client, ProtectTicket(ticket));
        UserInfo info = await ReadUserAsync(response);

        await Assert.That(info.IsAuthenticated).IsFalse();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(info.Churches).IsEmpty();
        await Assert.That(_auditReads.Count).IsEqualTo(auditCount);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidatePrincipal_InvalidTupleOrSecurityStamp_ClearsPreviouslyInitializedChurch(bool malformedTuple)
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        if (malformedTuple)
        {
            AuthenticationTicket malformed = ReadTicket(cookie);
            ClaimsIdentity identity = malformed.Principal.Identities.Single();
            identity.RemoveClaim(identity.FindFirst(SheafClaimTypes.Role)
                ?? throw new InvalidOperationException("Missing test role claim."));
            cookie = ProtectTicket(malformed);
        }
        else
        {
            await InScopeAsync(async services =>
            {
                UserManager<ApplicationUser> manager = services.GetRequiredService<UserManager<ApplicationUser>>();
                ApplicationUser stored = await manager.FindByIdAsync(user.Id)
                    ?? throw new InvalidOperationException("The test account was not persisted.");
                IdentityResult result = await manager.UpdateSecurityStampAsync(stored);
                await Assert.That(result.Succeeded).IsTrue();
            });
        }
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        await InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(membership.ChurchId, membership.Id);
            DefaultHttpContext http = CreateHttpContext(services);
            CookieAuthenticationOptions options = CookieOptions;
            CookieValidatePrincipalContext validation = new(http, Scheme, options, ReadTicket(cookie));
            await ResolveEvents(services, options).ValidatePrincipal(validation);

            await Assert.That(validation.Principal).IsNull();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().ChurchId).IsNull();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().LeaderId).IsNull();
        });
        using HttpResponseMessage response = await GetUserAsync(client, cookie);
        await Assert.That((await ReadUserAsync(response)).IsAuthenticated).IsFalse();
    }

    [Test]
    public async Task ValidatePrincipal_FinalValidationPrincipal_InitializesChurchBeforeHttpContextUserIsPublished()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        await InScopeAsync(async services =>
        {
            DefaultHttpContext http = CreateHttpContext(services);
            CookieAuthenticationOptions options = CookieOptions;
            CookieValidatePrincipalContext validation = new(http, Scheme, options, ReadTicket(cookie));
            await ResolveEvents(services, options).ValidatePrincipal(validation);

            await Assert.That(http.User.Identity?.IsAuthenticated).IsFalse();
            await Assert.That(validation.ShouldRenew).IsTrue();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().ChurchId).IsEqualTo(membership.ChurchId);
            await Assert.That(services.GetRequiredService<ICurrentChurch>().LeaderId).IsEqualTo(membership.Id);
            if (validation.Principal is not { } principal)
            {
                throw new InvalidOperationException("Identity unexpectedly rejected a valid cookie.");
            }
            await AssertSelectionAsync(principal, membership);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidatePrincipal_RefreshingPrincipalDropsChurchOrRejects_ClearsFinalRequestSelection(bool reject)
    {
        ApplicationUser user = await CreateUserAsync();
        _ = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, user);
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        await InScopeAsync(async services =>
        {
            DefaultHttpContext http = CreateHttpContext(services);
            SecurityStampValidatorOptions validatorOptions = services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
            validatorOptions.OnRefreshingPrincipal = refresh =>
            {
                if (refresh.NewPrincipal?.Identity is not ClaimsIdentity identity)
                {
                    throw new InvalidOperationException("Identity did not rebuild the test principal.");
                }

                refresh.NewPrincipal = reject
                    ? null
                    : new ClaimsPrincipal(new ClaimsIdentity(identity.Claims.Where(claim => claim.Type is not
                        (SheafClaimTypes.ChurchId or SheafClaimTypes.ChurchMembershipId or SheafClaimTypes.Role)),
                        identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType));
                return Task.CompletedTask;
            };
            CookieAuthenticationOptions options = CookieOptions;
            CookieValidatePrincipalContext validation = new(http, Scheme, options, ReadTicket(cookie));

            await ResolveEvents(services, options).ValidatePrincipal(validation);

            await Assert.That(validation.Principal is null).IsEqualTo(reject);
            await Assert.That(services.GetRequiredService<ICurrentChurch>().ChurchId).IsNull();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().LeaderId).IsNull();
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResolveRenewalAsync_MembershipBelongsToAnotherUserOrId_ClearsSelection(bool wrongMembershipId)
    {
        ApplicationUser caller = await CreateUserAsync();
        ApplicationUser other = wrongMembershipId ? caller : await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(other);

        await InScopeAsync(async services =>
        {
            ChurchSelection candidate = new(membership.ChurchId,
                wrongMembershipId ? Guid.NewGuid() : membership.Id, ChurchMembershipRole.Admin);
            ChurchSelection? selection = await services.GetRequiredService<IChurchAccessService>()
                .ResolveRenewalAsync(caller, candidate);

            await Assert.That(selection).IsNull();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().ChurchId).IsNull();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().LeaderId).IsNull();
        });
    }

    [Test]
    public async Task GetChurchesForUserAsync_UserScopedDirectory_ProjectsAndAuditsOnlyCallersChurches()
    {
        ApplicationUser caller = await CreateUserAsync();
        ApplicationUser other = await CreateUserAsync();
        ChurchMembership visible = await AddMembershipAsync(caller, ChurchMembershipRole.Coordinator);
        _ = await AddMembershipAsync(other, ChurchMembershipRole.Admin);
        _queries.Commands.Clear();

        await InScopeAsync(async services =>
        {
            IReadOnlyList<ChurchInfo> churches = await services.GetRequiredService<IChurchAccessService>()
                .GetChurchesForUserAsync(caller);

            await Assert.That(churches.Select(church => church.ChurchId)).IsEquivalentTo([visible.ChurchId]);
            await Assert.That(churches.Single().ChurchName).IsEqualTo($"Church {visible.ChurchId}");
            await Assert.That(churches.Single().Role).IsEqualTo(ChurchMembershipRole.Coordinator);
            await Assert.That(services.GetRequiredService<ApplicationDbContext>().ChangeTracker.Entries()).IsEmpty();
            await Assert.That(services.GetRequiredService<ICurrentChurch>().ChurchId).IsNull();
        });

        DirectoryRead audit = _auditReads.Single();
        await Assert.That(audit.UserId).IsEqualTo(caller.Id);
        await Assert.That(audit.ChurchCount).IsEqualTo(1);
        string command = _queries.Commands.Single();
        string projection = command[..command.IndexOf("FROM", StringComparison.Ordinal)];
        await Assert.That(projection).Contains("[ChurchId]");
        await Assert.That(projection).Contains("[Name]");
        await Assert.That(projection).Contains("[Role]");
        await Assert.That(projection.Contains("[UserId]", StringComparison.Ordinal)).IsFalse();
        await Assert.That(projection.Contains("[Id]", StringComparison.Ordinal)).IsFalse();
        await Assert.That(command).Contains("[UserId] = @");
    }

    [Test]
    public async Task GetChurchesForUserAsync_NamedTenancyBypass_KeepsOtherNamedMembershipFilter()
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership visible = await AddMembershipAsync(user);
        _ = await AddMembershipAsync(user, ChurchMembershipRole.Admin);
        await using var filteredFactory = Factory.Inner.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(provider =>
                new MembershipVisibilityDbContext(
                    provider.GetRequiredService<DbContextOptions<ApplicationDbContext>>(),
                    provider.GetRequiredService<ICurrentChurch>())))));

        await using AsyncServiceScope scope = filteredFactory.Services.CreateAsyncScope();
        IReadOnlyList<ChurchInfo> churches = await scope.ServiceProvider.GetRequiredService<IChurchAccessService>()
            .GetChurchesForUserAsync(user);

        await Assert.That(churches.Select(church => church.ChurchId)).IsEquivalentTo([visible.ChurchId]);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICurrentChurch>().ChurchId).IsNull();
    }

    [Test]
    public async Task GetCurrentUser_AnonymousRequestWithSuppliedUserId_DoesNotEnumerateDirectory()
    {
        ApplicationUser user = await CreateUserAsync();
        _ = await AddMembershipAsync(user);
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync($"/api/auth/user?userId={user.Id}");
        UserInfo info = await ReadUserAsync(response);

        await Assert.That(info.IsAuthenticated).IsFalse();
        await Assert.That(info.Churches).IsEmpty();
        await Assert.That(info.CurrentChurch).IsNull();
        await Assert.That(_auditReads).IsEmpty();
    }

    [Test]
    public async Task GetCurrentUser_AuthenticatedRequestWithAnotherUserId_ReturnsOnlySessionsChurches()
    {
        ApplicationUser caller = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(caller);
        ApplicationUser other = await CreateUserAsync();
        _ = await AddMembershipAsync(other);
        using HttpClient client = CreateClient();
        (_, string cookie) = await LoginAsync(client, caller);
        using HttpRequestMessage request = new(HttpMethod.Get, $"/api/auth/user?userId={other.Id}");
        request.Headers.Add(HeaderNames.Cookie, cookie);

        using HttpResponseMessage response = await client.SendAsync(request);
        UserInfo info = await ReadUserAsync(response);

        await Assert.That(info.UserId).IsEqualTo(caller.Id);
        await Assert.That(info.Churches.Select(church => church.ChurchId)).IsEquivalentTo([membership.ChurchId]);
        await Assert.That(_auditReads.All(read => read.UserId == caller.Id)).IsTrue();
    }

    [Test]
    public async Task Logout_AnonymousApiChallenge_ReturnsUnauthorizedWithoutLoginRedirect()
    {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PostAsync("/api/auth/logout", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task RedirectToAccessDenied_ApiRequest_ReturnsForbiddenWithoutLoginRedirect()
    {
        await InScopeAsync(async services =>
        {
            DefaultHttpContext http = CreateHttpContext(services);
            http.Request.Path = "/api/auth/user";
            Endpoint endpoint = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText == "/api/auth/user");
            http.SetEndpoint(endpoint);
            CookieAuthenticationOptions options = CookieOptions;
            RedirectContext<CookieAuthenticationOptions> redirect = new(
                http, Scheme, options, new AuthenticationProperties(), "https://localhost/Account/AccessDenied");

            await ResolveEvents(services, options).RedirectToAccessDenied(redirect);

            await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
            await Assert.That(http.Response.Headers.ContainsKey(HeaderNames.Location)).IsFalse();
        });
    }

    private CookieAuthenticationOptions CookieOptions =>
        Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

    private static AuthenticationScheme Scheme { get; } =
        new(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler));

    private HttpClient CreateClient() => Factory.CreateDefaultClient(new Uri("https://localhost"));

    private async Task<ApplicationUser> CreateUserAsync()
    {
        ApplicationUser user = new()
        {
            UserName = $"{Guid.NewGuid():N}@example.com",
            EmailConfirmed = true
        };
        user.Email = user.UserName;
        await InScopeAsync(async services =>
        {
            IdentityResult result = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Password);
            await Assert.That(result.Succeeded).IsTrue();
        });
        return user;
    }

    private async Task<ChurchMembership> AddMembershipAsync(
        ApplicationUser user, ChurchMembershipRole role = ChurchMembershipRole.Leader)
    {
        Church church = new() { Name = "Pending", Slug = $"church-{Guid.NewGuid():N}" };
        church.Name = $"Church {church.Id}";
        await InScopeAsync(async services =>
        {
            ApplicationDbContext db = services.GetRequiredService<ApplicationDbContext>();
            db.Churches.Add(church);
            await db.SaveChangesAsync();
        });
        ChurchMembership membership = new() { ChurchId = church.Id, UserId = user.Id, Role = role };
        await InChurchAsync(church.Id, async db =>
        {
            db.ChurchMemberships.Add(membership);
            await db.SaveChangesAsync();
        });
        return membership;
    }

    private static async Task<(UserInfo Info, string Cookie)> LoginAsync(
        HttpClient client, ApplicationUser user, string? cookie = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(user.Email ?? throw new InvalidOperationException("User has no email."), Password))
        };
        if (cookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, cookie);
        }
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await ReadUserAsync(response), ReadCookie(response));
    }

    private static async Task<HttpResponseMessage> GetUserAsync(HttpClient client, string cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/user");
        request.Headers.Add(HeaderNames.Cookie, cookie);
        return await client.SendAsync(request);
    }

    private static async Task<UserInfo> ReadUserAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<UserInfo>(JsonOptions)
            ?? throw new InvalidOperationException("The API returned no user info.");
    }

    private static string ReadCookie(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Where(value => value.StartsWith(CookieName, StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

    private AuthenticationTicket ReadTicket(string cookie)
    {
        DefaultHttpContext http = new();
        http.Request.Headers.Cookie = cookie;
        CookieAuthenticationOptions options = CookieOptions;
        string value = options.CookieManager.GetRequestCookie(http, CookieName)
            ?? throw new InvalidOperationException("The response did not contain the Identity cookie.");
        return options.TicketDataFormat.Unprotect(value)
            ?? throw new InvalidOperationException("The Identity cookie could not be unprotected.");
    }

    private string ProtectTicket(AuthenticationTicket ticket) => $"{CookieName}={CookieOptions.TicketDataFormat.Protect(ticket)}";

    private async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    private Task InChurchAsync(Guid churchId, Func<ApplicationDbContext, Task> action) =>
        InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            await action(services.GetRequiredService<ApplicationDbContext>());
        });

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        DefaultHttpContext http = new() { RequestServices = services };
        http.Request.Scheme = "https";
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return http;
    }

    private static CookieAuthenticationEvents ResolveEvents(IServiceProvider services, CookieAuthenticationOptions options) =>
        services.GetRequiredService(options.EventsType
            ?? throw new InvalidOperationException("The application cookie has no events type.")) as CookieAuthenticationEvents
            ?? throw new InvalidOperationException("The application cookie events have an unexpected type.");

    private static async Task AssertSelectionAsync(ClaimsPrincipal principal, ChurchMembership membership)
    {
        await Assert.That(principal.ReadChurchClaims(out ChurchSelection? selection)).IsEqualTo(ChurchClaimState.Selected);
        await Assert.That(selection).IsEqualTo(new ChurchSelection(membership.ChurchId, membership.Id, membership.Role));
        await Assert.That(principal.FindAll(SheafClaimTypes.Role).Select(claim => claim.Value))
            .IsEquivalentTo([membership.Role.ToString()]);
        await Assert.That(principal.IsInRole(membership.Role.ToString())).IsTrue();
    }

    private static async Task AssertUnselectedAsync(ClaimsPrincipal principal, ApplicationUser user)
    {
        await Assert.That(principal.Identity?.IsAuthenticated).IsTrue();
        await Assert.That(principal.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo(user.Id);
        await Assert.That(principal.ReadChurchClaims(out ChurchSelection? selection)).IsEqualTo(ChurchClaimState.Unselected);
        await Assert.That(selection).IsNull();
        await Assert.That(principal.FindAll(ClaimTypes.Role)).IsEmpty();
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private long _utcTicks = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan interval) => Interlocked.Add(ref _utcTicks, interval.Ticks);
    }

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed record DirectoryRead(string UserId, int ChurchCount);

    private sealed class DirectoryAuditProvider(ConcurrentQueue<DirectoryRead> reads) : ILoggerProvider
    {
        public ConcurrentQueue<DirectoryRead> Reads { get; } = reads;
        public ILogger CreateLogger(string categoryName) => new DirectoryAuditLogger(this);
        public void Dispose() { }

        private sealed class DirectoryAuditLogger(DirectoryAuditProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (eventId is { Id: 15600, Name: "ChurchMembershipDirectoryRead" }
                    && state is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    Dictionary<string, object?> values = fields.ToDictionary();
                    if (values["UserId"] is string userId && values["ChurchCount"] is int churchCount)
                    {
                        provider.Reads.Enqueue(new DirectoryRead(userId, churchCount));
                    }
                }
            }
        }
    }

    private sealed class MembershipVisibilityDbContext(
        DbContextOptions<ApplicationDbContext> options, ICurrentChurch currentChurch)
        : ApplicationDbContext(options, currentChurch)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<ChurchMembership>()
                .HasQueryFilter("MembershipVisibility", membership => membership.Role != ChurchMembershipRole.Admin);
        }
    }
}
