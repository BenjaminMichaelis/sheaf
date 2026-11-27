using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sheaf.Data;
using Sheaf.Data.Security;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public sealed class MembershipSecurityTests : IDisposable
{
    private const string UserId = "member";
    private const string OtherUserId = "other";

    private readonly Church _church = new() { Slug = "church", Name = "Church", TimeZoneId = "America/Chicago" };
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public MembershipSecurityTests()
    {
        _connection.Open();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        using ApplicationDbContext db = CreateContext();
        db.Database.EnsureCreated();
        db.Churches.Add(_church);
        db.Users.AddRange(CreateUser(UserId), CreateUser(OtherUserId));
        db.SaveChanges();
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _connection.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_MembershipDeleted_RotatesOnlyThatUsersStamp(bool useAsync)
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        string[] before = await StampsAsync();

        await using ApplicationDbContext db = CreateContext();
        db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId));
        await SaveAsync(db, useAsync);

        string[] after = await StampsAsync();
        await Assert.That(after[0]).IsNotEqualTo(before[0]);
        await Assert.That(after[1]).IsEqualTo(before[1]);
    }

    [Test]
    [Arguments(ChurchMembershipRole.Admin, ChurchMembershipRole.Staff, false)]
    [Arguments(ChurchMembershipRole.Staff, ChurchMembershipRole.Admin, true)]
    public async Task SaveChanges_RoleChanged_RotatesUsersStamp(
        ChurchMembershipRole original, ChurchMembershipRole updated, bool useAsync)
    {
        Guid membershipId = await SeedMembershipAsync(UserId, original);
        string[] before = await StampsAsync();

        await using ApplicationDbContext db = CreateContext();
        (await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId)).Role = updated;
        await SaveAsync(db, useAsync);

        string[] after = await StampsAsync();
        await Assert.That(after[0]).IsNotEqualTo(before[0]);
        await Assert.That(after[1]).IsEqualTo(before[1]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_DetachedMembershipRoleChanged_RotatesUsersStamp(bool useAsync)
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        string[] before = await StampsAsync();

        await using ApplicationDbContext db = CreateContext();
        ChurchMembership membership = await db.ChurchMemberships.AsNoTracking()
            .SingleAsync(m => m.Id == membershipId);
        membership.Role = ChurchMembershipRole.Admin;
        db.ChurchMemberships.Update(membership);
        await SaveAsync(db, useAsync);

        string[] after = await StampsAsync();
        await Assert.That(after[0]).IsNotEqualTo(before[0]);
        await Assert.That(after[1]).IsEqualTo(before[1]);
        await using ApplicationDbContext verify = CreateContext();
        await Assert.That((await verify.ChurchMemberships.AsNoTracking().SingleAsync()).Role)
            .IsEqualTo(ChurchMembershipRole.Admin);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_DetachedMembershipRoleUnchanged_DoesNotRotateUsersStamp(bool useAsync)
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        string[] before = await StampsAsync();

        await using ApplicationDbContext db = CreateContext();
        ChurchMembership membership = await db.ChurchMemberships.AsNoTracking()
            .SingleAsync(m => m.Id == membershipId);
        db.ChurchMemberships.Update(membership);
        await SaveAsync(db, useAsync);

        await Assert.That(await StampsAsync()).IsEquivalentTo(before);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SaveChanges_StaleDetachedMembershipRole_ThrowsConcurrencyExceptionWithoutOverwritingRole(
        bool useAsync, bool changeDetachedRole)
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        ChurchMembership staleMembership;
        await using (ApplicationDbContext staleContext = CreateContext())
        {
            staleMembership = await staleContext.ChurchMemberships.AsNoTracking()
                .SingleAsync(membership => membership.Id == membershipId);
        }

        string[] stampBeforeConcurrentChange = await StampsAsync();
        string[] stampAfterConcurrentChange = [];
        void ChangeRoleSynchronously()
        {
            using ApplicationDbContext concurrentContext = CreateContext();
            concurrentContext.ChurchMemberships.Single(membership => membership.Id == membershipId).Role =
                ChurchMembershipRole.Admin;
            concurrentContext.SaveChanges();
            stampAfterConcurrentChange = Stamps();
        }

        async Task ChangeRoleAsynchronously()
        {
            await using ApplicationDbContext concurrentContext = CreateContext();
            (await concurrentContext.ChurchMemberships.SingleAsync(membership => membership.Id == membershipId)).Role =
                ChurchMembershipRole.Admin;
            await concurrentContext.SaveChangesAsync();
            stampAfterConcurrentChange = await StampsAsync();
        }

        await using (ApplicationDbContext staleContext = CreateContext(
            new ChangeMembershipRoleBeforeSaveInterceptor(
                ChangeRoleSynchronously,
                ChangeRoleAsynchronously)))
        {
            if (changeDetachedRole)
            {
                staleMembership.Role = ChurchMembershipRole.Admin;
            }

            staleContext.ChurchMemberships.Update(staleMembership);

            await Assert.That(async () => await SaveAsync(staleContext, useAsync))
                .Throws<DbUpdateConcurrencyException>();
        }

        await using ApplicationDbContext verify = CreateContext();
        await Assert.That((await verify.ChurchMemberships.AsNoTracking().SingleAsync()).Role)
            .IsEqualTo(ChurchMembershipRole.Admin);
        await Assert.That(stampAfterConcurrentChange[0]).IsNotEqualTo(stampBeforeConcurrentChange[0]);
        await Assert.That(await StampsAsync()).IsEquivalentTo(stampAfterConcurrentChange);
    }

    [Test]
    public async Task SaveChanges_MembershipAdded_DoesNotRotateStamp()
    {
        string[] before = await StampsAsync();

        await SeedMembershipAsync(UserId, ChurchMembershipRole.Admin);

        await Assert.That(await StampsAsync()).IsEquivalentTo(before);
    }

    [Test]
    public async Task SaveChanges_RoleUnchanged_DoesNotRotateStamp()
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        string[] before = await StampsAsync();

        await using ApplicationDbContext db = CreateContext();
        (await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId)).Role = ChurchMembershipRole.Staff;
        await db.SaveChangesAsync();

        await Assert.That(await StampsAsync()).IsEquivalentTo(before);
    }

    [Test]
    public async Task SaveChanges_CreateRoleChangeAndRemove_LogsStructuredAuditEvents()
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        await using (ApplicationDbContext db = CreateContext())
        {
            (await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId)).Role = ChurchMembershipRole.Admin;
            await db.SaveChangesAsync();
        }

        await using (ApplicationDbContext db = CreateContext())
        {
            db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId));
            await db.SaveChangesAsync();
        }

        await Assert.That(_logs.Events.Select(entry => entry.Id.Id)).IsEquivalentTo([15601, 15602, 15603]);
        CapturedLog created = _logs.Events.Single(entry => entry.Id.Id == 15601);
        await Assert.That(created.State["ChurchId"]).IsEqualTo(_church.Id);
        await Assert.That(created.State["MembershipId"]).IsEqualTo(membershipId);
        await Assert.That(created.State["UserId"]).IsEqualTo(UserId);
        await Assert.That(created.State["Role"]).IsEqualTo(ChurchMembershipRole.Staff);
        CapturedLog changed = _logs.Events.Single(entry => entry.Id.Id == 15602);
        await Assert.That(changed.State["OldRole"]).IsEqualTo(ChurchMembershipRole.Staff);
        await Assert.That(changed.State["NewRole"]).IsEqualTo(ChurchMembershipRole.Admin);
        CapturedLog removed = _logs.Events.Single(entry => entry.Id.Id == 15603);
        await Assert.That(removed.State["UserId"]).IsEqualTo(UserId);
        await Assert.That(removed.State["Role"]).IsEqualTo(ChurchMembershipRole.Admin);
    }

    [Test]
    public async Task SaveChanges_FailedSave_DoesNotLogOrRotate()
    {
        Guid membershipId = await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        string[] before = await StampsAsync();
        _logs.Events.Clear();

        await using (ApplicationDbContext db = CreateContext())
        {
            ChurchMembership membership = await db.ChurchMemberships.SingleAsync(m => m.Id == membershipId);
            membership.Role = ChurchMembershipRole.Admin;
            db.Users.Add(CreateUser(OtherUserId));

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        }

        await Assert.That(_logs.Events).IsEmpty();
        await Assert.That(await StampsAsync()).IsEquivalentTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteDelete_ChurchMemberships_IsRejected(bool useAsync)
    {
        await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        await using ApplicationDbContext db = CreateContext();

        await Assert.That(async () => useAsync
                ? await db.ChurchMemberships.ExecuteDeleteAsync()
                : db.ChurchMemberships.ExecuteDelete())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ExecuteDelete cannot remove church memberships");
        await Assert.That(await db.ChurchMemberships.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MembershipRoleOrUserId_IsRejected(bool updateUserId)
    {
        await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        await using ApplicationDbContext db = CreateContext();

        await Assert.That(async () => updateUserId
                ? await db.ChurchMemberships.ExecuteUpdateAsync(
                    setters => setters.SetProperty(m => m.UserId, OtherUserId))
                : await db.ChurchMemberships.ExecuteUpdateAsync(
                    setters => setters.SetProperty(m => m.Role, ChurchMembershipRole.Admin)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(updateUserId ? "ChurchMembership.UserId" : "ChurchMembership.Role");
        await Assert.That((await db.ChurchMemberships.SingleAsync()).Role).IsEqualTo(ChurchMembershipRole.Staff);
    }

    [Test]
    [Arguments(nameof(ChurchMembership.Role))]
    public async Task ExecuteUpdate_RuntimeEfPropertyRoleSetter_IsRejected(string propertyName)
    {
        await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        await using ApplicationDbContext db = CreateContext();

        await Assert.That(async () => await db.ChurchMemberships.ExecuteUpdateAsync(setters => setters.SetProperty(
                m => EF.Property<ChurchMembershipRole>(m, propertyName), ChurchMembershipRole.Admin)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ChurchMembership.Role");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_ProjectedRole_IsRejected(bool useAsync)
    {
        await SeedMembershipAsync(UserId, ChurchMembershipRole.Staff);
        await using ApplicationDbContext db = CreateContext();
        var query = db.ChurchMemberships.Select(membership => new { membership.Role });

        await Assert.That(async () => useAsync
                ? await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(projection => projection.Role, ChurchMembershipRole.Admin))
                : query.ExecuteUpdate(
                    setters => setters.SetProperty(projection => projection.Role, ChurchMembershipRole.Admin)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ChurchMembership.Role");
        await Assert.That((await db.ChurchMemberships.SingleAsync()).Role).IsEqualTo(ChurchMembershipRole.Staff);
    }

    [Test]
    public async Task ExecuteUpdate_UnrelatedBulkWrites_AreNotBlocked()
    {
        await using ApplicationDbContext db = CreateContext();

        int updated = await db.Users.ExecuteUpdateAsync(setters => setters.SetProperty(u => u.UserName, u => u.UserName));

        await Assert.That(updated).IsEqualTo(2);
    }

    [Test]
    public async Task AddDatabase_ProductionRegistration_IncludesMembershipAndTenancyInterceptors()
    {
        using IHost host = ProductionDatabaseHost.Create();
        using IServiceScope scope = host.Services.CreateScope();
        IDbContextOptions options = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .GetService<IDbContextOptions>();
        IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor> interceptors =
            options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        await Assert.That(interceptors.OfType<MembershipSecurityStampInterceptor>()).Count().IsEqualTo(1);
        await Assert.That(interceptors.OfType<TenancySaveChangesInterceptor>()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AddDatabase_ProductionRegistration_RejectsMembershipBulkWrites()
    {
        using IHost host = ProductionDatabaseHost.Create();
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(async () => await db.ChurchMemberships.ExecuteDeleteAsync())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ExecuteDelete cannot remove church memberships");
        await Assert.That(async () => await db.ChurchMemberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ChurchMembership.Role");

        var projectedMemberships = db.ChurchMemberships.Select(membership => new { membership.Role });
        await Assert.That(async () => await projectedMemberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(projection => projection.Role, ChurchMembershipRole.Admin)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("ChurchMembership.Role");
    }

    private ApplicationDbContext CreateContext(params IInterceptor[] additionalInterceptors)
    {
        DbContextOptionsBuilder<ApplicationDbContext> builder = new();
        builder.UseSqlite(_connection).UseLoggerFactory(_loggerFactory);
        DbContextOptionsBuilder optionsBuilder = builder;
        optionsBuilder.UseMultiTenancy().UseMembershipSecurity();
        if (additionalInterceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(additionalInterceptors);
        }

        return new ApplicationDbContext(builder.Options, new FixedChurch(_church.Id));
    }

    private async Task<Guid> SeedMembershipAsync(string userId, ChurchMembershipRole role)
    {
        await using ApplicationDbContext db = CreateContext();
        ChurchMembership membership = new() { ChurchId = _church.Id, UserId = userId, Role = role };
        db.ChurchMemberships.Add(membership);
        await db.SaveChangesAsync();
        return membership.Id;
    }

    private async Task<string[]> StampsAsync()
    {
        await using ApplicationDbContext db = CreateContext();
        return await db.Users.AsNoTracking().OrderByDescending(u => u.Id == UserId)
            .Select(u => u.SecurityStamp ?? string.Empty).ToArrayAsync();
    }

    private string[] Stamps()
    {
        using ApplicationDbContext db = CreateContext();
        return db.Users.AsNoTracking().OrderByDescending(u => u.Id == UserId)
            .Select(u => u.SecurityStamp ?? string.Empty).ToArray();
    }

    private static async Task SaveAsync(ApplicationDbContext db, bool useAsync)
    {
        if (useAsync)
        {
            await db.SaveChangesAsync();
        }
        else
        {
            db.SaveChanges();
        }
    }

    private static ApplicationUser CreateUser(string id) =>
        new() { Id = id, UserName = id, SecurityStamp = Guid.NewGuid().ToString() };

    private sealed class FixedChurch(Guid churchId) : ICurrentChurch
    {
        public Guid? ChurchId { get; } = churchId;

        public Guid? LeaderId => null;
    }

    private sealed class ChangeMembershipRoleBeforeSaveInterceptor(
        Action changeRole,
        Func<Task> changeRoleAsync) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            changeRole();
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await changeRoleAsync();
            return result;
        }
    }

    private sealed record CapturedLog(EventId Id, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<CapturedLog> Events { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(MembershipSecurityStampInterceptor).FullName ? new Logger(Events) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Logger(List<CapturedLog> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    events.Add(new(eventId, values.ToDictionary(pair => pair.Key, pair => pair.Value)));
                }
            }
        }
    }
}
