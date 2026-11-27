using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

using Sheaf.Data;
using Sheaf.Integrations;
using Sheaf.Integrations.PlanningCenter;

namespace Sheaf.Api.Tests.Integrations;

public class PlanningCenterTests : ChurchApiTestBase
{
    private const string BasePath = "/api/church/integrations/planning-center";
    private const string ReturnUrl = "/settings/integrations";
    private static readonly Uri RedirectUri = new("https://localhost" + PlanningCenterAuthenticationDefaults.CallbackPath);

    private FakePlanningCenter Pco { get; } = new();

    /// <summary>Runs before a context saves a modified <see cref="ChurchIntegration"/>, outside any save it starts itself.</summary>
    private IntegrationSaveHook SaveHook { get; } = new();

    protected override void ConfigureTestConfiguration(IConfigurationBuilder config)
    {
        base.ConfigureTestConfiguration(config);
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlanningCenter:ClientId"] = "test-client",
            ["PlanningCenter:ClientSecret"] = "test-secret",
            ["PlanningCenter:RedirectUri"] = RedirectUri.AbsoluteUri,
            ["PlanningCenter:ReturnUrl"] = ReturnUrl,
            ["PlanningCenter:UserAgent"] = "Sheaf Tests (tests@example.org)"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddHttpClient(PlanningCenterOAuthClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Pco);
    }

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) =>
        options.AddInterceptors(new BeforeIntegrationSaveInterceptor(SaveHook));

    // ---- GET /connect ----

    [Test]
    public async Task ConnectPlanningCenter_Admin_RedirectsToPkceAuthorizationUrl()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            var query = QueryHelpers.ParseQuery(start.Url.Query);
            await Assert.That(start.Url.GetLeftPart(UriPartial.Path)).IsEqualTo("https://api.planningcenteronline.com/oauth/authorize");
            await Assert.That(query["client_id"].ToString()).IsEqualTo("test-client");
            await Assert.That(query["redirect_uri"].ToString()).IsEqualTo(RedirectUri.AbsoluteUri);
            await Assert.That(query["response_type"].ToString()).IsEqualTo("code");
            // Scope order carries no meaning (RFC 6749 section 3.3).
            await Assert.That(query["scope"].ToString().Split(' ')).IsEquivalentTo(["openid", "people"]);
            await Assert.That(query["code_challenge_method"].ToString()).IsEqualTo("S256");
            await Assert.That(query["prompt"].ToString()).IsEqualTo("select_account");
            await Assert.That(query["code_challenge"].ToString()).IsNotEmpty();
            await Assert.That(start.State).IsNotEmpty();
            await Assert.That(query["nonce"].ToString()).IsNotEmpty();
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_Staff_ReturnsForbidden()
    {
        var (client, staff) = await SignInAsync(ChurchMembershipRole.Staff);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, staff, HttpMethod.Get, ConnectPath(staff));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_ChurchIdNotSelected_ReturnsConflict()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Get, $"/connect?churchId={Guid.NewGuid():D}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_ChurchIdMissing_ReturnsBadRequest()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Get, "/connect");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_CrossSiteNavigation_ReturnsForbidden()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage response = await NavigateToConnectAsync(client, admin, "cross-site");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(Pco.Requests).IsEmpty();
        }
    }

    /// <summary>The Fetch Metadata resource isolation policy (web.dev/articles/fetch-metadata) allows these.</summary>
    [Test]
    [Arguments("same-origin")]
    [Arguments("same-site")]
    [Arguments("none")]
    [Arguments(null)]
    public async Task ConnectPlanningCenter_NavigationNotCrossSite_RedirectsToPlanningCenter(string? fetchSite)
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage response = await NavigateToConnectAsync(client, admin, fetchSite);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location?.Host).IsEqualTo("api.planningcenteronline.com");
        }
    }

    // ---- Callback ----

    [Test]
    public async Task PlanningCenterCallback_ValidStateAndCode_ConnectsChurchWithEncryptedTokens()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => Pco.CodeExchange("access-connected", "refresh-connected"));

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, admin.Cookie, start, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            // The exchange is bound to the registered redirect URI and proves possession of the PKCE verifier.
            FakePlanningCenter.Request exchange = Pco.To("/oauth/token").Single();
            await Assert.That(exchange.Form["redirect_uri"]).IsEqualTo(RedirectUri.AbsoluteUri);
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(exchange.Form["code_verifier"])));
            await Assert.That(challenge).IsEqualTo(QueryHelpers.ParseQuery(start.Url.Query)["code_challenge"].ToString());

            PlanningCenterStatusResponse status = await GetStatusAsync(client, admin);
            await Assert.That(status.Status).IsEqualTo(IntegrationStatus.Active);
            await Assert.That(status.OrganizationName).IsEqualTo("Grace Church");
            await Assert.That(status.ConnectedPersonName).IsEqualTo("Pat Admin");
            await InChurchAsync(admin.ChurchId, async db =>
            {
                ChurchIntegration stored = await db.ChurchIntegrations.SingleAsync();
                await Assert.That(stored.ExternalTenantId).IsEqualTo(Pco.OrganizationId);
                await Assert.That(stored.ProtectedCredentials).IsNotNull();
                await Assert.That(stored.ProtectedCredentials).DoesNotContain("refresh-connected");
                await Assert.That(stored.ProtectedCredentials).DoesNotContain("access-connected");
            });
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-connected");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StateMismatch_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, start.HandlerCookies, "code=good-code&state=forged");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_CorrelationCookieMissing_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            // The link is opened in a browser that did not start the connection, so it has no correlation cookie.
            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, admin.Cookie, start with { HandlerCookies = "" }, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StartedByAdminOfAnotherChurch_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (starterClient, starter) = await SignInAsync(ChurchMembershipRole.Admin);
        var (otherClient, other) = await SignInAsync(ChurchMembershipRole.Admin);
        using (starterClient)
        using (otherClient)
        {
            ConnectStart start = await BeginConnectAsync(starterClient, starter);

            // The other admin's browser presents the starter's correlation and nonce cookies and a valid state for them.
            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(otherClient, other.Cookie, start, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StartedByAnotherAdminOfSameChurch_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (starterClient, starter) = await SignInAsync(ChurchMembershipRole.Admin);
        var (otherClient, other) = await SignInToChurchAsync(starter.ChurchId, ChurchMembershipRole.Admin);
        using (starterClient)
        using (otherClient)
        {
            ConnectStart start = await BeginConnectAsync(starterClient, starter);

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(otherClient, other.Cookie, start, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SheafSessionMissing_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, sheafCookie: null, start, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_UserDeclined_RedirectsDenied()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, admin.Cookie, start, "error=access_denied");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Denied);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_PlanningCenterReportsError_RedirectsFailed()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, admin.Cookie, start, "error=server_error");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_IdTokenNonceMismatch_RedirectsFailedWithoutConnecting()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            ConnectStart start = await BeginConnectAsync(client, admin);
            Pco.Nonce = "replayed-nonce";

            using HttpResponseMessage callback = await ReturnFromPlanningCenterAsync(client, admin.Cookie, start, "code=good-code");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_IdTokenWithoutOrganization_RedirectsFailedAndRevokesGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            Pco.IncludeOrganization = false;

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "unusable");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-unusable"]);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_PersonIdLongerThanStored_RedirectsFailedAndRevokesGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            Pco.PersonId = new string('7', ChurchIntegration.ExternalIdMaxLength + 1);

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "unstorable");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-unstorable"]);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_TokenEndpointUnavailable_DoesNotRetryCodeExchange()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            // An authorization code is single-use, so a retry could never succeed and must not be sent.
            using HttpResponseMessage callback = await ConnectAsync(client, admin,
                () => FakePlanningCenter.Json(HttpStatusCode.ServiceUnavailable, "{}"));

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(Pco.To("/oauth/token").Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_OrganizationConnectedToAnotherChurch_RedirectsOrganizationInUseWithoutRevoking()
    {
        var (firstClient, first) = await SignInAsync(ChurchMembershipRole.Admin);
        var (secondClient, second) = await SignInAsync(ChurchMembershipRole.Admin);
        using (firstClient)
        using (secondClient)
        {
            await ConnectAsync(firstClient, first, "first");

            // Planning Center may hand the same person the grant the first church already holds; revoking would cut it off.
            using HttpResponseMessage callback = await ConnectAsync(secondClient, second,
                () => Pco.CodeExchange("access-first", "refresh-first"));

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.OrganizationInUse);
            await Assert.That(RevokedTokens()).IsEmpty();
            await Assert.That((await GetStatusAsync(secondClient, second)).Status).IsEqualTo(IntegrationStatus.Disconnected);
            await Assert.That(await StoredAccessTokenAsync(first.ChurchId)).IsEqualTo("access-first");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_OrganizationReleasedByDisconnect_ConnectsAnotherChurch()
    {
        var (firstClient, first) = await SignInAsync(ChurchMembershipRole.Admin);
        var (secondClient, second) = await SignInAsync(ChurchMembershipRole.Admin);
        using (firstClient)
        using (secondClient)
        {
            await ConnectAsync(firstClient, first, "first");
            using HttpResponseMessage disconnect = await SendAsync(firstClient, first, HttpMethod.Delete, "");

            using HttpResponseMessage callback = await ConnectAsync(secondClient, second, "second");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ReconnectToDifferentOrganization_ReleasesPreviousOrganization()
    {
        var (firstClient, first) = await SignInAsync(ChurchMembershipRole.Admin);
        var (secondClient, second) = await SignInAsync(ChurchMembershipRole.Admin);
        using (firstClient)
        using (secondClient)
        {
            string originalOrganization = Pco.OrganizationId;
            await ConnectAsync(firstClient, first, "first");
            Pco.OrganizationId = "5151";
            using HttpResponseMessage reconnect = await ConnectAsync(firstClient, first, "moved");
            Pco.OrganizationId = originalOrganization;

            using HttpResponseMessage callback = await ConnectAsync(secondClient, second, "second");

            await AssertOutcomeAsync(reconnect, PlanningCenterConnectOutcome.Connected);
            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ReconnectSameOrganization_RevokesReplacedTokenOnly()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "old");

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "new");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-old"]);
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-new");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ReconnectReturnsSameGrant_DoesNotRevokeIt()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "same");

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "same");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            await Assert.That(RevokedTokens()).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SaveConflictWithDifferentGrant_RevokesNewGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "stored");
            ConflictOnNextSave(admin.ChurchId);

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "lost");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-lost"]);
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-stored");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SaveConflictWithStoredGrant_DoesNotRevokeIt()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "stored");
            // A second authorization returns the grant already stored, and another connect saves first.
            ConflictOnNextSave(admin.ChurchId);

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "stored");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(RevokedTokens()).IsEmpty();
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-stored");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_DatabaseFailsWhileSaving_RedirectsFailedAndRevokesNewGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "stored");
            SaveHook.Before = () =>
            {
                SaveHook.Before = null;
                throw new InvalidOperationException("The database is unavailable.");
            };

            using HttpResponseMessage callback = await ConnectAsync(client, admin, "unsaved");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-unsaved"]);
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-stored");
        }
    }

    // ---- GET status ----

    [Test]
    public async Task GetPlanningCenterStatus_Staff_ReturnsForbidden()
    {
        var (client, staff) = await SignInAsync(ChurchMembershipRole.Staff);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, staff, HttpMethod.Get, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    // ---- DELETE ----

    [Test]
    public async Task DisconnectPlanningCenter_Connected_RevokesRefreshTokenAndForgetsConnection()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "connected");

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(RevokedTokens()).IsEquivalentTo(["refresh-connected"]);
            PlanningCenterStatusResponse status = await GetStatusAsync(client, admin);
            await Assert.That(status.Status).IsEqualTo(IntegrationStatus.Disconnected);
            await Assert.That(status.OrganizationName).IsNull();
            await Assert.That(status.ConnectedPersonName).IsNull();
            await Assert.That(status.ConnectedAt).IsNull();
            await InChurchAsync(admin.ChurchId, async db =>
            {
                ChurchIntegration stored = await db.ChurchIntegrations.SingleAsync();
                await Assert.That(stored.ExternalTenantId).IsNull();
                await Assert.That(stored.ConnectedAccountId).IsNull();
                await Assert.That(stored.ProtectedCredentials).IsNull();
            });
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_NeverConnected_ReturnsNoContent()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_Staff_ReturnsForbiddenAndKeepsConnection()
    {
        var (adminClient, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        var (staffClient, staff) = await SignInToChurchAsync(admin.ChurchId, ChurchMembershipRole.Staff);
        using (adminClient)
        using (staffClient)
        {
            await ConnectAsync(adminClient, admin, "connected");

            using HttpResponseMessage response = await SendAsync(staffClient, staff, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That((await GetStatusAsync(adminClient, admin)).Status).IsEqualTo(IntegrationStatus.Active);
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_RevokeRefused_StillDisconnects()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "connected");
            Pco.Enqueue("/oauth/revoke", () => FakePlanningCenter.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""));

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_ChangedByAnotherAdminMeanwhile_ReturnsConflictWithoutRevoking()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin, "connected");
            // Another admin reconnects meanwhile; Planning Center may have handed them the same grant.
            ConflictOnNextSave(admin.ChurchId);

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(RevokedTokens()).IsEmpty();
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Active);
            await Assert.That(await StoredAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-connected");
        }
    }

    // ---- OAuth client ----

    [Test]
    public async Task TryRevokeAsync_SuccessWithEmptyBody_ReturnsTrue()
    {
        Pco.Enqueue("/oauth/revoke", () => new HttpResponseMessage(HttpStatusCode.OK));

        bool revoked = await Factory.Services.GetRequiredService<PlanningCenterOAuthClient>()
            .TryRevokeAsync("refresh-any", CancellationToken.None);

        await Assert.That(revoked).IsTrue();
    }

    // ---- Helpers ----

    private sealed record Session(string Cookie, Guid ChurchId);

    /// <summary>A started connection: where the handler sent the browser, and the cookies it set for the return.</summary>
    private sealed record ConnectStart(Uri Url, string HandlerCookies)
    {
        public string State => QueryHelpers.ParseQuery(Url.Query)["state"].ToString();
    }

    private sealed class IntegrationSaveHook
    {
        public Func<Task>? Before { get; set; }
    }

    private static readonly AsyncLocal<bool> InsideBeforeSave = new();

    private sealed class BeforeIntegrationSaveInterceptor(IntegrationSaveHook hook) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!InsideBeforeSave.Value
                && hook.Before is { } action
                && eventData.Context?.ChangeTracker.Entries<ChurchIntegration>().Any(entry => entry.State == EntityState.Modified) == true)
            {
                InsideBeforeSave.Value = true;
                try
                {
                    await action();
                }
                finally
                {
                    InsideBeforeSave.Value = false;
                }
            }
            return result;
        }
    }

    private static string ConnectPath(Session session) => $"/connect?churchId={session.ChurchId:D}";

    private IEnumerable<string> RevokedTokens() => Pco.To("/oauth/revoke").Select(request => request.Form["token"]);

    /// <summary>Another writer saves first on the next save of the connection, so that save conflicts.</summary>
    private void ConflictOnNextSave(Guid churchId) => SaveHook.Before = async () =>
    {
        SaveHook.Before = null;
        await InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            await services.GetRequiredService<ApplicationDbContext>().ChurchIntegrations
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, Guid.NewGuid()));
        });
    };

    /// <summary>The access token the church has stored, decrypted, or null when there is none.</summary>
    private async Task<string?> StoredAccessTokenAsync(Guid churchId)
    {
        string? token = null;
        await InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            PlanningCenterConnectionStore store = services.GetRequiredService<PlanningCenterConnectionStore>();
            token = await store.Connection.SingleOrDefaultAsync() is { } connection ? store.Read(connection)?.AccessToken : null;
        });
        return token;
    }

    private async Task<(HttpClient Client, Session Session)> SignInToChurchAsync(Guid churchId, ChurchMembershipRole role)
    {
        ApplicationUser user = await CreateUserAsync();
        await InChurchAsync(churchId, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = churchId, UserId = user.Id, Role = role });
            await db.SaveChangesAsync();
        });
        HttpClient client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return (client, new Session(cookie, churchId));
    }

    private async Task<(HttpClient Client, Session Session)> SignInAsync(ChurchMembershipRole role)
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user, role);
        HttpClient client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return (client, new Session(cookie, membership.ChurchId));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Session session, HttpMethod method, string path)
    {
        using HttpRequestMessage request = new(method, BasePath + path);
        request.Headers.Add(HeaderNames.Cookie, session.Cookie);
        request.Headers.Add("X-Sheaf-Church", session.ChurchId.ToString("D"));
        return await client.SendAsync(request);
    }

    /// <summary>A browser navigation to the connect endpoint, with the fetch metadata a browser would send.</summary>
    private static async Task<HttpResponseMessage> NavigateToConnectAsync(HttpClient client, Session session, string? fetchSite)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, BasePath + ConnectPath(session));
        request.Headers.Add(HeaderNames.Cookie, session.Cookie);
        if (fetchSite is not null)
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Navigates to the connect endpoint and returns where it redirects, with the correlation and nonce cookies the
    /// handler set. The fake then puts the request's nonce in the id token, as Planning Center would.
    /// </summary>
    private async Task<ConnectStart> BeginConnectAsync(HttpClient client, Session session)
    {
        using HttpResponseMessage response = await SendAsync(client, session, HttpMethod.Get, ConnectPath(session));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect).Because(await response.Content.ReadAsStringAsync());
        Uri url = response.Headers.Location ?? throw new InvalidOperationException("The connect response did not redirect.");
        Pco.Nonce = QueryHelpers.ParseQuery(url.Query)["nonce"].ToString();
        string handlerCookies = string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Select(value => value.Split(';', 2)[0]));
        return new ConnectStart(url, handlerCookies);
    }

    /// <summary>The browser returns from Planning Center with <paramref name="query"/> and the start's state.</summary>
    private static Task<HttpResponseMessage> ReturnFromPlanningCenterAsync(
        HttpClient client, string? sheafCookie, ConnectStart start, string query) =>
        CallbackAsync(client, sheafCookie, start.HandlerCookies, $"{query}&state={Uri.EscapeDataString(start.State)}");

    private static async Task<HttpResponseMessage> CallbackAsync(HttpClient client, string? sheafCookie, string handlerCookies, string query)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{PlanningCenterAuthenticationDefaults.CallbackPath}?{query}");
        request.Headers.Add(HeaderNames.Cookie, sheafCookie is null ? handlerCookies : $"{sheafCookie}; {handlerCookies}");
        return await client.SendAsync(request);
    }

    /// <summary>The whole round trip, in which Planning Center issues <c>access-{name}</c> and <c>refresh-{name}</c>.</summary>
    private Task<HttpResponseMessage> ConnectAsync(HttpClient client, Session session, string name) =>
        ConnectAsync(client, session, () => Pco.CodeExchange($"access-{name}", $"refresh-{name}"));

    /// <summary>The whole round trip, with <paramref name="tokenResponse"/> as Planning Center's answer to the code exchange.</summary>
    private async Task<HttpResponseMessage> ConnectAsync(HttpClient client, Session session, Func<HttpResponseMessage> tokenResponse)
    {
        ConnectStart start = await BeginConnectAsync(client, session);
        Pco.Enqueue("/oauth/token", tokenResponse);
        return await ReturnFromPlanningCenterAsync(client, session.Cookie, start, "code=good-code");
    }

    private static async Task<PlanningCenterStatusResponse> GetStatusAsync(HttpClient client, Session session)
    {
        using HttpResponseMessage response = await SendAsync(client, session, HttpMethod.Get, "");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<PlanningCenterStatusResponse>(JsonOptions)
            ?? throw new InvalidOperationException("The status response was empty.");
    }

    private static async Task AssertOutcomeAsync(HttpResponseMessage callback, PlanningCenterConnectOutcome expected)
    {
        await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(callback.Headers.Location?.OriginalString)
            .IsEqualTo($"{ReturnUrl}?{PlanningCenterAuthenticationDefaults.OutcomeQueryName}={expected}");
    }
}
