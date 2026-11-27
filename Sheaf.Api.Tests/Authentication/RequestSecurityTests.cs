using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests.Authentication;

public class RequestSecurityTests : WebApplicationTest<RequestSecurityWebApplicationFactory, Program>
{
    private static readonly string[] _multipleOrigins = ["https://localhost", "https://attacker.example"];
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<IStartupFilter>(new ConnectionEndpoints());

    [Test]
    [Arguments("POST")]
    [Arguments("PUT")]
    [Arguments("PATCH")]
    [Arguments("DELETE")]
    public async Task Post_CrossSiteOrigin_Returns403(string method)
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        using HttpRequestMessage request = new(new HttpMethod(method), "/test/security");
        request.Headers.Add("Origin", "https://attacker.example");
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    public async Task Post_NoOriginNoFetchMetadataNoReferer_Returns403()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    public async Task Post_SameOriginFetchMetadataWithoutOrigin_Succeeds()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("write reached");
    }

    [Test]
    [Arguments("/api/auth/login")]
    [Arguments("/api/auth/register")]
    [Arguments("/api/auth/logout")]
    [Arguments("/api/auth/church/00000000-0000-0000-0000-000000000001")]
    public async Task Login_CrossSiteOrigin_Returns403(string path)
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        using HttpResponseMessage response = await client.PostAsync(path, null);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    [Arguments("https://attacker.example")]
    [Arguments("null")]
    [Arguments("not-an-origin")]
    [Arguments("https://localhost/path")]
    public async Task Post_DisallowedOriginWithTrustedFallbackSignals_Returns403(string origin)
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        using HttpRequestMessage request = new(HttpMethod.Post, "/test/security");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("Referer", "https://localhost/page");
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    public async Task Post_EmptyOriginWithSameOriginFetchMetadata_Returns403()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("X-Test-Empty-Origin", "true");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    public async Task Post_MultipleOriginsWithSameOriginFetchMetadata_Returns403()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", _multipleOrigins);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    [Arguments("https://client.example", HttpStatusCode.OK)]
    [Arguments("http://api.example", HttpStatusCode.OK)]
    [Arguments("https://client.example:443", HttpStatusCode.OK)]
    [Arguments("https://client.example:444", HttpStatusCode.Forbidden)]
    [Arguments("http://client.example", HttpStatusCode.Forbidden)]
    [Arguments("https://client.example.attacker.example", HttpStatusCode.Forbidden)]
    public async Task Post_OriginSchemeHostAndPort_UsesExactAllowedOrigin(string origin, HttpStatusCode expected)
    {
        using HttpClient client = Factory.CreateDefaultClient(new Uri("http://api.example"));
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", origin);
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    [Arguments("https://client.example/page?query=1", HttpStatusCode.OK)]
    [Arguments("http://api.example/page", HttpStatusCode.OK)]
    [Arguments("https://attacker.example/page", HttpStatusCode.Forbidden)]
    [Arguments("https://client.example@attacker.example/page", HttpStatusCode.Forbidden)]
    public async Task Post_RefererWithoutOriginOrFetchMetadata_VerifiesRefererOrigin(string referer, HttpStatusCode expected)
    {
        using HttpClient client = Factory.CreateDefaultClient(new Uri("http://api.example"));
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Referer", referer);
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    [Test]
    public async Task Post_CrossSiteFetchMetadataWithAllowedReferer_Returns403()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        client.DefaultRequestHeaders.Add("Referer", "https://localhost/page");
        using HttpResponseMessage response = await client.PostAsync("/test/security", null);
        await AssertCsrfDeniedAsync(response);
    }

    [Test]
    public async Task Get_CrossSiteOrigin_NotBlockedByCsrfCheck()
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        using HttpResponseMessage response = await client.GetAsync("/");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("API is running.");
    }

    [Test]
    [Arguments("/api/auth/login")]
    [Arguments("/api/auth/register")]
    [Arguments("/api/auth/church/00000000-0000-0000-0000-000000000001")]
    public async Task Login_ExceedingRateLimit_Returns429(string path)
    {
        using HttpClient client = Factory.CreateClient();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage response = await SendInvalidBodyAsync(client, path);
            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }
        using HttpResponseMessage rejected = await SendInvalidBodyAsync(client, path);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        using HttpResponseMessage read = await client.GetAsync("/api/auth/user");
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("203.0.113.10", "203.0.113.10", "http")]
    [Arguments("192.0.2.10", "203.0.113.50", "https")]
    [Arguments("198.51.100.10", "203.0.113.50", "https")]
    public async Task ForwardedFor_FromUntrustedProxy_IsIgnored(string peer, string expectedIp, string expectedScheme)
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Peer", peer);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using HttpResponseMessage response = await client.GetAsync("/test/connection");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("ip").GetString()).IsEqualTo(expectedIp);
        await Assert.That(body.RootElement.GetProperty("scheme").GetString()).IsEqualTo(expectedScheme);
    }

    [Test]
    [Arguments("192.0.2.10", HttpStatusCode.BadRequest)]
    [Arguments("203.0.113.10", HttpStatusCode.TooManyRequests)]
    public async Task Login_ForwardedClientChanges_OnlyTrustedProxyGetsSeparateIpPartitions(string peer, HttpStatusCode expected)
    {
        using HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Peer", peer);
        for (int attempt = 0; attempt < 11; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{attempt + 1}");
            using HttpResponseMessage response = await SendInvalidBodyAsync(client, "/api/auth/login");
            await Assert.That(response.StatusCode).IsEqualTo(attempt < 10 ? HttpStatusCode.BadRequest : expected);
        }
    }

    [Test]
    public async Task Login_AlternatingProtectedEndpoints_SharesIpRateLimitBudget()
    {
        string[] paths = ["/api/auth/login", "/api/auth/register", "/api/auth/church/00000000-0000-0000-0000-000000000001"];
        using HttpClient client = Factory.CreateClient();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage response = await SendInvalidBodyAsync(client, paths[attempt % paths.Length]);
            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }
        using HttpResponseMessage rejected = await SendInvalidBodyAsync(client, "/api/auth/login");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task ForwardedFor_WithoutConfiguredTrust_IsIgnored()
    {
        await using NoTrustedProxyFactory factory = new();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Peer", "127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using HttpResponseMessage response = await client.GetAsync("/test/connection");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("ip").GetString()).IsEqualTo("127.0.0.1");
        await Assert.That(body.RootElement.GetProperty("scheme").GetString()).IsEqualTo("http");
    }

    private static Task<HttpResponseMessage> SendInvalidBodyAsync(HttpClient client, string path) =>
        client.PostAsync(path, new StringContent("{", Encoding.UTF8, "application/json"));

    private static async Task AssertCsrfDeniedAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo("tag:sheaf,2026:problems/csrf-origin-denied");
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    private sealed class NoTrustedProxyFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            ConfigurationBuilder configuration = new();
            ConfigureStartupConfiguration(configuration);
            foreach (KeyValuePair<string, string?> setting in configuration.Build().AsEnumerable())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ConnectionEndpoints()));
        }
    }

    private sealed class ConnectionEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Peer"].FirstOrDefault() ?? "203.0.113.10");
                if (context.Request.Headers.ContainsKey("X-Test-Empty-Origin"))
                {
                    // HttpClient omits empty headers, so preserve the wire-level empty Origin here.
                    context.Request.Headers.Origin = "";
                }
                return following(context);
            });
            next(app);
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMethods("/test/security", ["POST", "PUT", "PATCH", "DELETE"], () => "write reached").AllowAnonymous();
                endpoints.MapGet("/test/connection", (HttpContext context) => new
                {
                    ip = context.Connection.RemoteIpAddress?.ToString(),
                    scheme = context.Request.Scheme
                }).AllowAnonymous();
            });
        };
    }
}

public sealed class RequestSecurityWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
    {
        base.ConfigureStartupConfiguration(configurationBuilder);
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedOrigins:0"] = "https://client.example",
            ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10",
            ["ForwardedHeaders:KnownNetworks:0"] = "198.51.100.0/24"
        });
    }
}

public class ProductionSecurityStartupTests
{
    [Test]
    public async Task Startup_ProductionWithAllowedOrigins_AllowsConfiguredCorsOrigin()
    {
        await using ProductionSecurityFactory factory = new(configureOrigins: true);
        using HttpClient client = factory.CreateDefaultClient(new Uri("https://api.example"));
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://client.example");
        using HttpResponseMessage response = await client.GetAsync("/");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://client.example");
    }

    [Test]
    public async Task Startup_ProductionWithoutAllowedOrigins_Fails()
    {
        await using ProductionSecurityFactory factory = new(configureOrigins: false);
        await Assert.That(() => factory.CreateClient()).Throws<InvalidOperationException>()
            .WithMessage("AllowedOrigins must contain at least one explicit HTTP(S) origin outside Development.");
    }

    private sealed class ProductionSecurityFactory(bool configureOrigins) : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            ConfigurationBuilder configuration = new();
            ConfigureStartupConfiguration(configuration);
            foreach (KeyValuePair<string, string?> setting in configuration.Build().AsEnumerable())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.UseEnvironment("Production");
        }

        protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
        {
            base.ConfigureStartupConfiguration(configurationBuilder);
            Dictionary<string, string?> settings = new()
            {
                ["DataProtection:KeyVaultKeyIdentifier"] = "https://vault.example/keys/test",
                ["Email:Smtp:Host"] = "smtp.example",
                ["Email:From"] = "Sheaf <no-reply@example.com>"
            };
            if (configureOrigins)
            {
                settings["AllowedOrigins:0"] = "https://client.example";
            }

            configurationBuilder.AddInMemoryCollection(settings);
        }
    }
}
