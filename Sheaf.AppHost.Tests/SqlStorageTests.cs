using Aspire.Hosting;

namespace Sheaf.AppHost.Tests;

public class SqlStorageTests
{
    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("worktree-a")]
    [Arguments("worktree-b")]
    public async Task AddSqlServer_ContainerSuffix_ScopesPersistentStorage(string suffix)
    {
        var builder = DistributedApplication.CreateBuilder(
            [$"--{Resources.ContainerSuffixKey}={suffix}"]);

        var sql = Resources.AddSqlServer(builder);
        var mount = sql.Resource.Annotations.OfType<ContainerMountAnnotation>().Single();
        string prefix = string.IsNullOrWhiteSpace(suffix) ? "Sheaf" : $"Sheaf-{suffix}";

        await Assert.That(sql.Resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name)
            .IsEqualTo($"{prefix}-sql");
        await Assert.That(mount.Source).IsEqualTo($"{prefix}-database");
        await Assert.That(mount.Target).IsEqualTo("/var/opt/mssql");
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.Volume);
    }
}
