using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class DataProtectionPersistenceTests
{
    [Test]
    public async Task PersistKeysToDbContext_NoCurrentChurch_StoresKey()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddScoped<ICurrentChurch, TenantContext>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(nameof(PersistKeysToDbContext_NoCurrentChurch_StoresKey))
                .UseMultiTenancy());
        services.AddDataProtection()
            .SetApplicationName("Sheaf")
            .PersistKeysToDbContext<ApplicationDbContext>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            await Assert.That(() => db.ChurchIdOrThrow).Throws<InvalidOperationException>();
        }

        IDataProtector protector = provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(PersistKeysToDbContext_NoCurrentChurch_StoresKey));
        _ = protector.Protect("persisted data");

        await using AsyncServiceScope verificationScope = provider.CreateAsyncScope();
        ApplicationDbContext verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verificationDb.DataProtectionKeys.AsNoTracking().ToListAsync()).IsNotEmpty();
    }
}
