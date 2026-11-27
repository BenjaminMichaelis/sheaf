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
using Sheaf.Endpoints;
using Sheaf.Integrations;
using Sheaf.Integrations.PlanningCenter;

namespace Sheaf.Api.Tests.Integrations;

public class PlanningCenterTests : ChurchApiTestBase
{
    private const string BasePath = "/api/church/integrations/planning-center";
    private const string ReturnUrl = "/settings/integrations";
    private static readonly Uri RedirectUri = new("https://localhost" + PlanningCenterEndpoints.CallbackPath);

    private FakePlanningCenter Pco { get; } = new();

    protected override void ConfigureTestConfiguration(IConfigurationBuilder config)
    {
        base.ConfigureTestConfiguration(config);
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlanningCenter:ClientId"] = "test-client",
            ["PlanningCenter:ClientSecret"] = "test-secret",
            ["PlanningCenter:RedirectUri"] = RedirectUri.AbsoluteUri,
            ["PlanningCenter:ReturnUrl"] = ReturnUrl
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddHttpClient(PlanningCenterOAuthClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Pco);
    }

    [Test]
    public async Task ConnectPlanningCenter_Staff_ReturnsForbidden()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Staff);
        using (client)
        {
            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Get, $"/connect?churchId={admin.ChurchId:D}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_Admin_RedirectsToPkceAuthorizationUrl()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (url, _) = await BeginConnectAsync(client, admin);

            var query = QueryHelpers.ParseQuery(url.Query);
            await Assert.That(url.GetLeftPart(UriPartial.Path)).IsEqualTo("https://api.planningcenteronline.com/oauth/authorize");
            await Assert.That(query["client_id"].ToString()).IsEqualTo("test-client");
            await Assert.That(query["redirect_uri"].ToString()).IsEqualTo(RedirectUri.AbsoluteUri);
            await Assert.That(query["response_type"].ToString()).IsEqualTo("code");
            await Assert.That(query["scope"].ToString()).IsEqualTo("openid people");
            await Assert.That(query["code_challenge_method"].ToString()).IsEqualTo("S256");
            await Assert.That(query["prompt"].ToString()).IsEqualTo("select_account");
            await Assert.That(query["code_challenge"].ToString()).IsNotEmpty();
            await Assert.That(query["state"].ToString()).IsNotEmpty();
            await Assert.That(query["nonce"].ToString()).IsNotEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ValidStateAndCode_ConnectsChurchWithEncryptedTokens()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (url, handlerCookies) = await BeginConnectAsync(client, admin);
            var query = QueryHelpers.ParseQuery(url.Query);

            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(query["state"].ToString())}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            FakePlanningCenter.Request exchange = Pco.To("/oauth/token").Single();
            await Assert.That(exchange.Form["grant_type"]).IsEqualTo("authorization_code");
            await Assert.That(exchange.Form["code"]).IsEqualTo("good-code");
            await Assert.That(exchange.Form["redirect_uri"]).IsEqualTo(RedirectUri.AbsoluteUri);
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(exchange.Form["code_verifier"])));
            await Assert.That(challenge).IsEqualTo(query["code_challenge"].ToString());

            PlanningCenterStatusResponse status = await GetStatusAsync(client, admin);
            await Assert.That(status.Status).IsEqualTo(IntegrationStatus.Active);
            await Assert.That(status.OrganizationName).IsEqualTo("Grace Church");
            await Assert.That(status.ConnectedPersonName).IsEqualTo("Pat Admin");

            await InChurchAsync(admin.ChurchId, async db =>
            {
                ChurchIntegration stored = await db.ChurchIntegrations.SingleAsync();
                await Assert.That(stored.Provider).IsEqualTo(IntegrationProvider.PlanningCenter);
                await Assert.That(stored.ExternalTenantId).IsEqualTo("4242");
                await Assert.That(stored.ProtectedCredentials).IsNotNull();
                await Assert.That(stored.ProtectedCredentials).DoesNotContain("refresh-1");
                await Assert.That(stored.ProtectedCredentials).DoesNotContain("access-1");
            });
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StateMismatch_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (_, handlerCookies) = await BeginConnectAsync(client, admin);

            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, handlerCookies, "code=good-code&state=forged");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StartedByAnotherAdmin_RedirectsInvalidRequest()
    {
        var (starterClient, starter) = await SignInAsync(ChurchMembershipRole.Admin);
        var (otherClient, other) = await SignInAsync(ChurchMembershipRole.Admin);
        using (starterClient)
        using (otherClient)
        {
            var (url, handlerCookies) = await BeginConnectAsync(starterClient, starter);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();

            // The other admin's browser presents the starter's correlation and nonce cookies and a valid state for them.
            using HttpResponseMessage callback = await CallbackAsync(otherClient, other.Cookie, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(state)}");

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
            var (url, handlerCookies) = await BeginConnectAsync(client, admin);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();

            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, handlerCookies, $"error=access_denied&state={Uri.EscapeDataString(state)}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Denied);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SheafSessionMissing_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (url, handlerCookies) = await BeginConnectAsync(client, admin);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();

            using HttpResponseMessage callback = await CallbackAsync(client, sheafCookie: null, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(state)}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_IdTokenNonceMismatch_RedirectsFailedWithoutConnecting()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (url, handlerCookies) = await BeginConnectAsync(client, admin);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();
            Pco.Nonce = "replayed-nonce";

            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(state)}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
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
            await ConnectAsync(firstClient, first);
            // Planning Center may hand the same person the grant the first church already holds; revoking would cut it off.
            Pco.Enqueue("/oauth/token", () => Pco.CodeExchange("access-1", "refresh-1"));

            using HttpResponseMessage callback = await ConnectAsync(secondClient, second);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.OrganizationInUse);
            await Assert.That(Pco.To("/oauth/revoke")).IsEmpty();
            await Assert.That((await GetStatusAsync(secondClient, second)).Status).IsEqualTo(IntegrationStatus.Disconnected);
            await Assert.That(await GetAccessTokenAsync(first.ChurchId)).IsEqualTo("access-1");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_TokenEndpointUnavailable_DoesNotRetryCodeExchange()
    {
        Pco.Enqueue("/oauth/token", () => FakePlanningCenter.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(Pco.To("/oauth/token").Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_Connected_RevokesRefreshTokenAndForgetsTokens()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-1"]);
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
    public async Task DisconnectPlanningCenter_RevokeRefused_StillDisconnects()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/revoke", () => FakePlanningCenter.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""));

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task DisconnectPlanningCenter_RefreshedDuringDisconnect_RevokesBothGrants()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            SaveHook.Before = async () =>
            {
                SaveHook.Before = null;
                await UpdateConnectionAsync(admin.ChurchId, (row, protector) => row.ProtectedCredentials = protector.Protect(
                    IntegrationProvider.PlanningCenter,
                    new PlanningCenterCredentials("access-new", Time.GetUtcNow().AddHours(2), "refresh-new", Time.GetUtcNow())));
            };

            using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-1", "refresh-new"]);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task TryRevokeAsync_SuccessWithEmptyBody_ReturnsTrue()
    {
        Pco.Enqueue("/oauth/revoke", () => new HttpResponseMessage(HttpStatusCode.OK));

        bool revoked = await Factory.Services.GetRequiredService<PlanningCenterOAuthClient>()
            .TryRevokeAsync("refresh-1", CancellationToken.None);

        await Assert.That(revoked).IsTrue();
    }

    [Test]
    public async Task DisconnectPlanningCenter_Disconnected_FreesOrganizationForAnotherChurch()
    {
        var (firstClient, first) = await SignInAsync(ChurchMembershipRole.Admin);
        var (secondClient, second) = await SignInAsync(ChurchMembershipRole.Admin);
        using (firstClient)
        using (secondClient)
        {
            await ConnectAsync(firstClient, first);
            using HttpResponseMessage disconnect = await SendAsync(firstClient, first, HttpMethod.Delete, "");

            using HttpResponseMessage callback = await ConnectAsync(secondClient, second);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_FreshToken_ReturnsStoredTokenWithoutRefreshing()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);

            string token = await GetAccessTokenAsync(admin.ChurchId);

            await Assert.That(token).IsEqualTo("access-1");
            await Assert.That(Pco.To("/oauth/token").Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_NearExpiry_RefreshesWithRotatedRefreshToken()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);

            Time.Advance(TimeSpan.FromHours(2) - PlanningCenterTokenProvider.RefreshMargin);
            string second = await GetAccessTokenAsync(admin.ChurchId);
            Time.Advance(TimeSpan.FromHours(2));
            string third = await GetAccessTokenAsync(admin.ChurchId);

            await Assert.That(second).IsEqualTo("access-2");
            await Assert.That(third).IsEqualTo("access-3");
            var refreshes = Pco.To("/oauth/token").Where(r => r.Form["grant_type"] == "refresh_token").Select(r => r.Form["refresh_token"]);
            await Assert.That(refreshes).IsEquivalentTo(["refresh-1", "refresh-2"]);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_RefreshTokenRejected_MarksNeedsReauthorization()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => FakePlanningCenter.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}"""));
            Time.Advance(TimeSpan.FromHours(3));

            await AssertNotConnectedAsync(admin.ChurchId);
            PlanningCenterStatusResponse status = await GetStatusAsync(client, admin);
            await Assert.That(status.Status).IsEqualTo(IntegrationStatus.NeedsReauthorization);
        }
    }

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

    [Test]
    public async Task DisconnectPlanningCenter_Staff_ReturnsForbiddenAndKeepsConnection()
    {
        var (adminClient, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        var (staffClient, staff) = await SignInToChurchAsync(admin.ChurchId, ChurchMembershipRole.Staff);
        using (adminClient)
        using (staffClient)
        {
            await ConnectAsync(adminClient, admin);

            using HttpResponseMessage response = await SendAsync(staffClient, staff, HttpMethod.Delete, "");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That((await GetStatusAsync(adminClient, admin)).Status).IsEqualTo(IntegrationStatus.Active);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_StartedByAnotherAdminOfSameChurch_RedirectsInvalidRequest()
    {
        var (starterClient, starter) = await SignInAsync(ChurchMembershipRole.Admin);
        var (otherClient, other) = await SignInToChurchAsync(starter.ChurchId, ChurchMembershipRole.Admin);
        using (starterClient)
        using (otherClient)
        {
            var (url, handlerCookies) = await BeginConnectAsync(starterClient, starter);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();

            using HttpResponseMessage callback = await CallbackAsync(otherClient, other.Cookie, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(state)}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ReconnectSameOrganization_RevokesReplacedTokenOnly()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);

            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-1"]);
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-2");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_ReconnectReturnsSameGrant_DoesNotRevokeIt()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => Pco.CodeExchange("access-1", "refresh-1"));

            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
            await Assert.That(Pco.To("/oauth/revoke")).IsEmpty();
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_RefreshTokenNoLongerValid_MarksNeedsReauthorization()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => FakePlanningCenter.Json(HttpStatusCode.Unauthorized,
                """{"error":"invalid_request","error_description":"The refresh token is no longer valid"}"""));
            Time.Advance(TimeSpan.FromHours(3));

            await AssertNotConnectedAsync(admin.ChurchId);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.NeedsReauthorization);
        }
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, "Bad Request")]
    [Arguments(HttpStatusCode.Forbidden, "<html>Request blocked. CloudFront Request ID: abc</html>")]
    public async Task GetAccessTokenAsync_RefreshFailsWithoutOAuthError_KeepsConnectionActive(HttpStatusCode status, string body)
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => new HttpResponseMessage(status) { Content = new StringContent(body) });
            Time.Advance(TimeSpan.FromHours(3));

            await Assert.That(async () => { await GetAccessTokenAsync(admin.ChurchId); })
                .Throws<PlanningCenterOAuthException>();
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Active);
            // The stored refresh token is still the live one, so the next attempt uses it again.
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-2");
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_UnreadableCredentials_MarksNeedsReauthorizationAndKeepsCiphertext()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            await UpdateConnectionAsync(admin.ChurchId, (row, _) => row.ProtectedCredentials = "not-decryptable");

            await AssertNotConnectedAsync(admin.ChurchId);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.NeedsReauthorization);
            await Assert.That(Pco.To("/oauth/token").Count).IsEqualTo(1);
            // Kept so that a recovered Data Protection key ring could read it again.
            await InChurchAsync(admin.ChurchId, async db =>
                await Assert.That((await db.ChurchIntegrations.SingleAsync()).ProtectedCredentials).IsEqualTo("not-decryptable"));
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_DisconnectedDuringRefresh_RevokesNewTokenAndThrows()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", async () =>
            {
                await UpdateConnectionAsync(admin.ChurchId, (row, _) =>
                {
                    row.Status = IntegrationStatus.Disconnected;
                    row.ExternalTenantId = null;
                    row.ProtectedCredentials = null;
                });
                return Tokens("access-new", "refresh-new");
            });
            Time.Advance(TimeSpan.FromHours(3));

            await AssertNotConnectedAsync(admin.ChurchId);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-new"]);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_ConcurrentRefreshGaveUpDuringRefresh_KeepsNewTokenAndReactivates()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            // Another process refreshed with the same old token, was rejected, and marked the connection dead.
            Pco.Enqueue("/oauth/token", async () =>
            {
                await UpdateConnectionAsync(admin.ChurchId, (row, _) =>
                {
                    row.Status = IntegrationStatus.NeedsReauthorization;
                    row.ProtectedCredentials = null;
                });
                return Tokens("access-new", "refresh-new");
            });
            Time.Advance(TimeSpan.FromHours(3));

            string token = await GetAccessTokenAsync(admin.ChurchId);

            await Assert.That(token).IsEqualTo("access-new");
            await Assert.That(Pco.To("/oauth/revoke")).IsEmpty();
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Active);
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-new");
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_RefreshedElsewhereDuringRefresh_UsesWinnerAndRevokesOwnToken()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Time.Advance(TimeSpan.FromHours(3));
            Pco.Enqueue("/oauth/token", async () =>
            {
                await UpdateConnectionAsync(admin.ChurchId, (row, protector) => row.ProtectedCredentials = protector.Protect(
                    IntegrationProvider.PlanningCenter,
                    new PlanningCenterCredentials("access-winner", Time.GetUtcNow().AddHours(2), "refresh-winner", Time.GetUtcNow())));
                return Tokens("access-loser", "refresh-loser");
            });

            string token = await GetAccessTokenAsync(admin.ChurchId);

            await Assert.That(token).IsEqualTo("access-winner");
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-loser"]);
        }
    }

    [Test]
    public async Task ConnectPlanningCenter_CrossSiteNavigation_ReturnsForbidden()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{BasePath}/connect?churchId={admin.ChurchId:D}");
            request.Headers.Add(HeaderNames.Cookie, admin.Cookie);
            request.Headers.Add("Sec-Fetch-Site", "cross-site");

            using HttpResponseMessage response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(Pco.Requests).IsEmpty();
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
    public async Task PlanningCenterCallback_CorrelationCookieMissing_RedirectsInvalidRequestWithoutExchangingCode()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            var (url, _) = await BeginConnectAsync(client, admin);
            string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();

            // The link is opened in a browser that did not start the connection, so it has no correlation cookie.
            using HttpResponseMessage callback = await CallbackAsync(client, admin.Cookie, handlerCookies: "", $"code=good-code&state={Uri.EscapeDataString(state)}");

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.InvalidRequest);
            await Assert.That(Pco.To("/oauth/token")).IsEmpty();
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
            await ConnectAsync(firstClient, first);
            Pco.OrganizationId = "5151";
            using HttpResponseMessage reconnect = await ConnectAsync(firstClient, first);
            Pco.OrganizationId = "4242";

            using HttpResponseMessage callback = await ConnectAsync(secondClient, second);

            await AssertOutcomeAsync(reconnect, PlanningCenterConnectOutcome.Connected);
            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Connected);
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SaveConflictWithDifferentGrant_RevokesNewGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            SaveHook.Before = async () =>
            {
                SaveHook.Before = null;
                await UpdateConnectionAsync(admin.ChurchId, (_, _) => { });
            };

            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-2"]);
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-1");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_DatabaseFailsWhileSaving_RedirectsFailedAndRevokesNewGrant()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            SaveHook.Before = () =>
            {
                SaveHook.Before = null;
                throw new InvalidOperationException("The database is unavailable.");
            };

            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-2"]);
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-1");
        }
    }

    [Test]
    public async Task PlanningCenterCallback_SaveConflictWithStoredGrant_DoesNotRevokeIt()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            // A second authorization returns the grant already stored, and another connect saves first.
            Pco.Enqueue("/oauth/token", () => Pco.CodeExchange("access-1", "refresh-1"));
            SaveHook.Before = async () =>
            {
                SaveHook.Before = null;
                await UpdateConnectionAsync(admin.ChurchId, (_, _) => { });
            };

            using HttpResponseMessage callback = await ConnectAsync(client, admin);

            await AssertOutcomeAsync(callback, PlanningCenterConnectOutcome.Failed);
            await Assert.That(Pco.To("/oauth/revoke")).IsEmpty();
            await Assert.That(await GetAccessTokenAsync(admin.ChurchId)).IsEqualTo("access-1");
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_RefreshRejectedAfterDisconnect_StaysDisconnected()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Time.Advance(TimeSpan.FromHours(3));
            Pco.Enqueue("/oauth/token", async () =>
            {
                await UpdateConnectionAsync(admin.ChurchId, (row, _) =>
                {
                    row.Status = IntegrationStatus.Disconnected;
                    row.ExternalTenantId = null;
                    row.ProtectedCredentials = null;
                });
                return FakePlanningCenter.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""");
            });

            await AssertNotConnectedAsync(admin.ChurchId);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Disconnected);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_RefreshRejectedButRefreshedElsewhere_ReturnsWinnersToken()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Time.Advance(TimeSpan.FromHours(3));
            Pco.Enqueue("/oauth/token", async () =>
            {
                await UpdateConnectionAsync(admin.ChurchId, (row, protector) => row.ProtectedCredentials = protector.Protect(
                    IntegrationProvider.PlanningCenter,
                    new PlanningCenterCredentials("access-winner", Time.GetUtcNow().AddHours(2), "refresh-winner", Time.GetUtcNow())));
                return FakePlanningCenter.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""");
            });

            string token = await GetAccessTokenAsync(admin.ChurchId);

            await Assert.That(token).IsEqualTo("access-winner");
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.Active);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_InvalidGrantNotLabeledJson_MarksNeedsReauthorization()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Pco.Enqueue("/oauth/token", () => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "text/plain")
            });
            Time.Advance(TimeSpan.FromHours(3));

            await AssertNotConnectedAsync(admin.ChurchId);
            await Assert.That((await GetStatusAsync(client, admin)).Status).IsEqualTo(IntegrationStatus.NeedsReauthorization);
        }
    }

    [Test]
    public async Task GetAccessTokenAsync_SaveConflictsOnEveryAttempt_RevokesNewTokenAndThrows()
    {
        var (client, admin) = await SignInAsync(ChurchMembershipRole.Admin);
        using (client)
        {
            await ConnectAsync(client, admin);
            Time.Advance(TimeSpan.FromHours(3));
            // Before every save of the refreshed row, another writer gives up on the connection, so each save conflicts.
            SaveHook.Before = () => UpdateConnectionAsync(admin.ChurchId, (row, _) =>
            {
                row.Status = IntegrationStatus.NeedsReauthorization;
                row.ProtectedCredentials = null;
            });

            await AssertNotConnectedAsync(admin.ChurchId);
            SaveHook.Before = null;
            await Assert.That(Pco.To("/oauth/revoke").Select(r => r.Form["token"])).IsEquivalentTo(["refresh-2"]);
        }
    }

    /// <summary>Runs before a context saves a modified <see cref="ChurchIntegration"/>, outside any save it starts itself.</summary>
    private IntegrationSaveHook SaveHook { get; } = new();

    private sealed class IntegrationSaveHook
    {
        public Func<Task>? Before { get; set; }
    }

    private static readonly AsyncLocal<bool> InsideBeforeSave = new();

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) =>
        options.AddInterceptors(new BeforeIntegrationSaveInterceptor(SaveHook));

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

    private sealed record Admin(string Cookie, Guid ChurchId);

    private static HttpResponseMessage Tokens(string accessToken, string refreshToken) => FakePlanningCenter.Json(HttpStatusCode.OK,
        $$"""{"access_token":"{{accessToken}}","token_type":"bearer","expires_in":7200,"refresh_token":"{{refreshToken}}","scope":"openid people"}""");

    /// <summary>Changes the stored connection the way another process would, bumping its concurrency version.</summary>
    private Task UpdateConnectionAsync(Guid churchId, Action<ChurchIntegration, IntegrationCredentialProtector> change) =>
        InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            ApplicationDbContext db = services.GetRequiredService<ApplicationDbContext>();
            ChurchIntegration row = await db.ChurchIntegrations.SingleAsync();
            change(row, services.GetRequiredService<IntegrationCredentialProtector>());
            row.Version = Guid.NewGuid();
            await db.SaveChangesAsync();
        });

    private async Task<(HttpClient Client, Admin Admin)> SignInToChurchAsync(Guid churchId, ChurchMembershipRole role)
    {
        ApplicationUser user = await CreateUserAsync();
        await InChurchAsync(churchId, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = churchId, UserId = user.Id, Role = role });
            await db.SaveChangesAsync();
        });
        HttpClient client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return (client, new Admin(cookie, churchId));
    }

    private async Task<(HttpClient Client, Admin Admin)> SignInAsync(ChurchMembershipRole role)
    {
        ApplicationUser user = await CreateUserAsync();
        ChurchMembership membership = await AddMembershipAsync(user, role);
        HttpClient client = CreateClient();
        var (_, cookie) = await LoginAsync(client, user);
        return (client, new Admin(cookie, membership.ChurchId));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Admin admin, HttpMethod method, string path)
    {
        using HttpRequestMessage request = new(method, BasePath + path);
        request.Headers.Add(HeaderNames.Cookie, admin.Cookie);
        request.Headers.Add("X-Sheaf-Church", admin.ChurchId.ToString("D"));
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Navigates to the connect endpoint and returns where it redirects, with the correlation and nonce cookies the
    /// handler set. The fake then puts the request's nonce in the id token, as Planning Center would.
    /// </summary>
    private async Task<(Uri Url, string HandlerCookies)> BeginConnectAsync(HttpClient client, Admin admin)
    {
        using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Get, $"/connect?churchId={admin.ChurchId:D}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect).Because(await response.Content.ReadAsStringAsync());
        Uri url = response.Headers.Location ?? throw new InvalidOperationException("The connect response did not redirect.");
        Pco.Nonce = QueryHelpers.ParseQuery(url.Query)["nonce"].ToString();
        string handlerCookies = string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Select(value => value.Split(';', 2)[0]));
        return (url, handlerCookies);
    }

    private static async Task<HttpResponseMessage> CallbackAsync(HttpClient client, string? sheafCookie, string handlerCookies, string query)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{PlanningCenterEndpoints.CallbackPath}?{query}");
        request.Headers.Add(HeaderNames.Cookie, sheafCookie is null ? handlerCookies : $"{sheafCookie}; {handlerCookies}");
        return await client.SendAsync(request);
    }

    /// <summary>Runs the whole browser round trip: start, then return from Planning Center with a code.</summary>
    private async Task<HttpResponseMessage> ConnectAsync(HttpClient client, Admin admin)
    {
        var (url, handlerCookies) = await BeginConnectAsync(client, admin);
        string state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();
        return await CallbackAsync(client, admin.Cookie, handlerCookies, $"code=good-code&state={Uri.EscapeDataString(state)}");
    }

    private static async Task<PlanningCenterStatusResponse> GetStatusAsync(HttpClient client, Admin admin)
    {
        using HttpResponseMessage response = await SendAsync(client, admin, HttpMethod.Get, "");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<PlanningCenterStatusResponse>(StatusJson)
            ?? throw new InvalidOperationException("The status response was empty.");
    }

    private static readonly System.Text.Json.JsonSerializerOptions StatusJson = new(JsonOptions)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<IntegrationStatus>() }
    };

    private static async Task AssertOutcomeAsync(HttpResponseMessage callback, PlanningCenterConnectOutcome expected)
    {
        await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(callback.Headers.Location?.OriginalString)
            .IsEqualTo($"{ReturnUrl}?{PlanningCenterEndpoints.OutcomeQueryName}={expected}");
    }

    private async Task AssertNotConnectedAsync(Guid churchId)
    {
        PlanningCenterException? exception = await Assert.That(async () => { await GetAccessTokenAsync(churchId); })
            .Throws<PlanningCenterException>();
        await Assert.That(exception?.Failure).IsEqualTo(PlanningCenterFailure.NotConnected);
    }

    private async Task<string> GetAccessTokenAsync(Guid churchId)
    {
        string? token = null;
        await InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            token = await services.GetRequiredService<PlanningCenterTokenProvider>().GetAccessTokenAsync(CancellationToken.None);
        });
        return token ?? throw new InvalidOperationException("No token was returned.");
    }
}
