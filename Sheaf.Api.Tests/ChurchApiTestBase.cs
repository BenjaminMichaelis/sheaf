using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Sheaf.Core.Tests;
using Sheaf.Data;
using Sheaf.Data.Tenancy;
using Sheaf.Endpoints;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

/// <summary>
/// Shared host, isolated SQL catalog, and account/church helpers for API tests that sign in as a church member.
/// </summary>
public abstract class ChurchApiTestBase : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    protected const string Password = "Church-session-Password1!";
    protected const string CookieName = "__Host-sheaf";
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<ChurchMembershipRole>(), new JsonStringEnumConverter<IntegrationStatus>() }
    };

    private string _connectionString = "";

    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    protected ControlledTimeProvider Time { get; } = new();

    protected override async Task SetupAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(Sql.ConnectionString)
        {
            InitialCatalog = $"Sheaf{GetType().Name}_{Guid.NewGuid():N}"
        }.ConnectionString;

        // Create the catalog before the host starts. The host loads Data Protection keys from the database at startup;
        // if the catalog does not exist yet, every test waits out EF's connection-retry backoff.
        ServiceCollection services = new();
        services.AddSheafDatabase(_connectionString);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureTestConfiguration(IConfigurationBuilder config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _connectionString
        });

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Time);
        services.AddDbContext<ApplicationDbContext>(options => ConfigureDatabase(options.UseAzureSql(_connectionString)));
    }

    /// <summary>Lets a derived fixture add interceptors or options to the test database.</summary>
    protected virtual void ConfigureDatabase(DbContextOptionsBuilder options)
    {
    }

    protected CookieAuthenticationOptions CookieOptions =>
        Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

    protected HttpClient CreateClient() => Factory.CreateDefaultClient(new Uri("https://localhost"));

    protected async Task<ApplicationUser> CreateUserAsync()
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

    protected async Task<ChurchMembership> AddMembershipAsync(
        ApplicationUser user, ChurchMembershipRole role = ChurchMembershipRole.Leader)
    {
        Church church = new() { Name = "Pending", Slug = $"church-{Guid.NewGuid():N}", TimeZoneId = "America/Chicago" };
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

    protected static async Task<(UserInfo Info, string Cookie)> LoginAsync(
        HttpClient client, ApplicationUser user, string? cookie = null, bool rememberMe = false)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(
                user.Email ?? throw new InvalidOperationException("User has no email."), Password, rememberMe))
        };
        if (cookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, cookie);
        }
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await ReadUserAsync(response), ReadCookie(response));
    }

    protected static async Task<UserInfo> ReadUserAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<UserInfo>(JsonOptions)
            ?? throw new InvalidOperationException("The API returned no user info.");
    }

    protected static string ReadCookie(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues(HeaderNames.SetCookie)
            .Where(value => value.StartsWith(CookieName, StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

    protected AuthenticationTicket ReadTicket(string cookie)
    {
        DefaultHttpContext http = new();
        http.Request.Headers.Cookie = cookie;
        CookieAuthenticationOptions options = CookieOptions;
        string value = options.CookieManager.GetRequestCookie(http, CookieName)
            ?? throw new InvalidOperationException("The response did not contain the Identity cookie.");
        return options.TicketDataFormat.Unprotect(value)
            ?? throw new InvalidOperationException("The Identity cookie could not be unprotected.");
    }

    protected async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected Task InChurchAsync(Guid churchId, Func<ApplicationDbContext, Task> action) =>
        InScopeAsync(async services =>
        {
            services.GetRequiredService<ITenantContextInitializer>().Initialize(churchId, null);
            await action(services.GetRequiredService<ApplicationDbContext>());
        });

    /// <summary>Sends a tracker request as a signed-in member of <paramref name="churchId"/>, optionally with If-Match.</summary>
    protected static async Task<HttpResponseMessage> TrackerRequestAsync(HttpClient client, string cookie, Guid churchId, HttpMethod method, string path, object? body = null, Guid? ifMatch = null)
    {
        using HttpRequestMessage request = new(method, $"/api/church{path}");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-Sheaf-Church", churchId.ToString("D"));
        if (ifMatch is { } version)
            request.Headers.TryAddWithoutValidation("If-Match", Sheaf.Services.EntityTag.Format(version));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    /// <summary>Asserts the status code, reporting the body on failure, and reads the JSON response.</summary>
    protected static async Task<T> TrackerReadAsync<T>(HttpResponseMessage response, System.Net.HttpStatusCode expected = System.Net.HttpStatusCode.OK)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("Missing tracker response.");
    }

    /// <summary>A whole-second clock that tests advance explicitly.</summary>
    protected sealed class ControlledTimeProvider : TimeProvider
    {
        private long _utcTicks = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan interval) => Interlocked.Add(ref _utcTicks, interval.Ticks);
    }

    protected static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        DefaultHttpContext http = new() { RequestServices = services };
        http.Request.Scheme = "https";
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return http;
    }
}
