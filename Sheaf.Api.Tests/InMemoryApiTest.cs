using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheaf.Data;
using Sheaf.Data.Security;
using Sheaf.Data.Tenancy;

using TUnit.AspNetCore;

namespace Sheaf.Api.Tests;

/// <summary>
/// API tests against a fresh in-memory database and a clock the test moves. For behavior that depends on SQL Server
/// (constraints, column sizes, query translation), derive from <see cref="ChurchApiTestBase"/> instead.
/// </summary>
public abstract class InMemoryApiTest : WebApplicationTest<ApiWebApplicationFactory, Program>
{
    protected AdjustableTimeProvider Time { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        string database = Guid.NewGuid().ToString("N");
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database)
            .UseMultiTenancy().UseMembershipSecurity());
        services.AddSingleton<TimeProvider>(Time);
    }

    protected HttpClient CreateClient() => Factory.CreateDefaultClient(new Uri("https://localhost"));

    protected async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>A clock that starts at the real time and moves only when a test advances it.</summary>
    protected sealed class AdjustableTimeProvider : TimeProvider
    {
        private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _utcTicks, elapsed.Ticks);
    }
}
