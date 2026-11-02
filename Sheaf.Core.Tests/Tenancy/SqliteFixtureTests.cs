using Microsoft.EntityFrameworkCore;

using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

public class SqliteFixtureTests
{
    [Test]
    public async Task CreateContext_WriterDisposedBeforeReader_ReaderSeesPersistedRowsInFreshContext()
    {
        using SqliteFixture sqlite = new();
        Guid leaderId = Guid.Parse("5b0f3a52-7c1e-4d0a-9f55-0d3c8a6e1b10");
        Guid personId = Guid.Parse("a7d4c2e9-3b86-4f1d-8e02-6c9b5d7f4a31");
        Church church = new() { Slug = "smoke-church", Name = "Smoke Church" };
        Leader writtenLeader = new() { Id = leaderId, ChurchId = church.Id, Name = "Smoke Leader" };
        Person writtenPerson = new()
        {
            Id = personId,
            ChurchId = church.Id,
            Name = "Smoke Person",
            Email = "smoke@example.com",
            LeaderId = leaderId,
        };

        TenancyTestDbContext writer = sqlite.CreateContext(church.Id);
        TenancyTestDbContext secondContext = sqlite.CreateContext(church.Id);
        await Assert.That(ReferenceEquals(writer, secondContext)).IsFalse();
        await secondContext.DisposeAsync();

        await using (writer)
        {
            writer.Churches.Add(church);
            writer.Leaders.Add(writtenLeader);
            writer.People.Add(writtenPerson);
            await writer.SaveChangesAsync();
        }

        await using TenancyTestDbContext reader = sqlite.CreateContext(church.Id);
        await Assert.That(ReferenceEquals(writer, reader)).IsFalse();

        Person readPerson = await reader.People.Include(p => p.Leader).SingleAsync();

        await Assert.That(readPerson.Id).IsEqualTo(personId);
        await Assert.That(readPerson.Name).IsEqualTo("Smoke Person");
        await Assert.That(readPerson.Email).IsEqualTo("smoke@example.com");
        await Assert.That(readPerson.Leader?.Id).IsEqualTo(leaderId);
        await Assert.That(readPerson.Leader?.Name).IsEqualTo("Smoke Leader");
        await Assert.That(ReferenceEquals(readPerson, writtenPerson)).IsFalse();
        await Assert.That(await reader.Churches.SingleAsync(c => c.Slug == "smoke-church")).IsNotEqualTo(church);
    }
}