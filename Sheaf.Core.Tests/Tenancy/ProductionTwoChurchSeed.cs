using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

using DomainCapture = Sheaf.Data.Capture;
using DomainContext = Sheaf.Data.Context;
using DomainPerson = Sheaf.Data.Person;
using DomainTouchpoint = Sheaf.Data.Touchpoint;

namespace Sheaf.Core.Tests;

internal sealed record ProductionTwoChurchSeed(ProductionChurchSeed ChurchA, ProductionChurchSeed ChurchB)
{
    public static DateTimeOffset OccurredAt { get; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public static async Task<ProductionTwoChurchSeed> SeedAsync(ProductionSqlServerFixture sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ProductionTwoChurchSeed seed = new(new ProductionChurchSeed("A"), new ProductionChurchSeed("B"));

        await using (AsyncServiceScope scope = sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            db.Churches.AddRange(seed.ChurchA.Church, seed.ChurchB.Church);
            db.Users.AddRange(seed.ChurchA.User, seed.ChurchB.User);
            await db.SaveChangesAsync();
        }

        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = sql.CreateScope(church.Church.Id);
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(church.Entities);
            await db.SaveChangesAsync();
        }

        return seed;
    }
}

internal sealed class ProductionChurchSeed
{
    public ProductionChurchSeed(string label)
    {
        Church = new Church { Slug = $"synthetic-church-{label.ToLowerInvariant()}", Name = $"Synthetic church {label}" };
        User = new ApplicationUser { UserName = $"synthetic-member-{label}", NormalizedUserName = $"SYNTHETIC-MEMBER-{label}" };
        Membership = new ChurchMembership
        {
            ChurchId = Church.Id,
            UserId = User.Id,
            Role = ChurchMembershipRole.Leader
        };
        Context = new DomainContext
        {
            ChurchId = Church.Id,
            Name = $"Synthetic group {label}",
            Kind = ContextKind.Group,
            CustomFieldSchema = new CustomFieldSchema
            {
                Fields =
                [
                    new CustomFieldDefinition { Name = $"fixture-text-{label}", Type = "text" },
                    new CustomFieldDefinition { Name = $"fixture-count-{label}", Type = "number" }
                ]
            }
        };
        Person = new DomainPerson
        {
            ChurchId = Church.Id,
            Name = $"Synthetic person {label}",
            ConsentSource = ConsentSource.LeaderEntered,
            Visibility = PersonVisibility.Standard
        };
        Capture = new DomainCapture
        {
            ChurchId = Church.Id,
            RawText = $"Synthetic capture {label}",
            Transcript = $"Synthetic transcript {label}",
            ExtractionJson = "{\"fixture\":true}",
            Status = $"seeded-{label}"
        };
        Touchpoint = new DomainTouchpoint
        {
            ChurchId = Church.Id,
            PersonId = Person.Id,
            AuthorLeaderId = Membership.Id,
            ContextId = Context.Id,
            Note = $"Synthetic note {label}",
            NoteVisibility = NoteVisibility.Private,
            OccurredAt = ProductionTwoChurchSeed.OccurredAt
        };
        Proposal = new Proposal
        {
            ChurchId = Church.Id,
            Type = "fixture-change",
            PayloadJson = "{\"fixture\":true}"
        };
        AuditEntry = new AuditEntry
        {
            ChurchId = Church.Id,
            Action = "fixture-seeded",
            OccurredAt = ProductionTwoChurchSeed.OccurredAt,
            PayloadJson = "{\"fixture\":true}"
        };
        LeaderToken = new LeaderToken
        {
            ChurchId = Church.Id,
            LeaderId = Membership.Id,
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-fixture-{label}"))
        };
        Relationship = new Relationship
        {
            ChurchId = Church.Id,
            PersonId = Person.Id,
            LeaderId = Membership.Id,
            Kind = "fixture-follow-up",
            Status = RelationshipStatus.Open,
            StartedOn = ProductionTwoChurchSeed.OccurredAt
        };
        ContextLeader = new ContextLeader
        {
            ChurchId = Church.Id,
            ContextId = Context.Id,
            LeaderId = Membership.Id,
            StartedOn = ProductionTwoChurchSeed.OccurredAt
        };
        ContextMember = new ContextMember
        {
            ChurchId = Church.Id,
            ContextId = Context.Id,
            PersonId = Person.Id,
            StartedOn = ProductionTwoChurchSeed.OccurredAt
        };
        Attendance = new Attendance
        {
            ChurchId = Church.Id,
            ContextId = Context.Id,
            PersonId = Person.Id,
            RecordedByLeaderId = Membership.Id,
            SessionDate = DateOnly.FromDateTime(ProductionTwoChurchSeed.OccurredAt.UtcDateTime)
        };
        ExternalRef = new PersonExternalRef
        {
            ChurchId = Church.Id,
            PersonId = Person.Id,
            System = PersonExternalSystem.Csv,
            ExternalId = "synthetic-existing-external-id",
            SyncedAt = ProductionTwoChurchSeed.OccurredAt
        };
        Entities =
        [
            Membership, Context, Person, Capture, Touchpoint, Proposal, AuditEntry, LeaderToken,
            Relationship, ContextLeader, ContextMember, Attendance, ExternalRef
        ];
        ExpectedIds = new Dictionary<Type, Guid[]>
        {
            [typeof(ChurchMembership)] = [Membership.Id],
            [typeof(DomainContext)] = [Context.Id],
            [typeof(DomainPerson)] = [Person.Id],
            [typeof(DomainCapture)] = [Capture.Id],
            [typeof(DomainTouchpoint)] = [Touchpoint.Id],
            [typeof(Proposal)] = [Proposal.Id],
            [typeof(AuditEntry)] = [AuditEntry.Id],
            [typeof(LeaderToken)] = [LeaderToken.Id],
            [typeof(Relationship)] = [Relationship.Id],
            [typeof(ContextLeader)] = [ContextLeader.Id],
            [typeof(ContextMember)] = [ContextMember.Id],
            [typeof(Attendance)] = [Attendance.Id],
            [typeof(PersonExternalRef)] = [ExternalRef.Id]
        };
    }

    public Church Church { get; }
    public ApplicationUser User { get; }
    public ChurchMembership Membership { get; }
    public DomainContext Context { get; }
    public DomainPerson Person { get; }
    public DomainCapture Capture { get; }
    public DomainTouchpoint Touchpoint { get; }
    public Proposal Proposal { get; }
    public AuditEntry AuditEntry { get; }
    public LeaderToken LeaderToken { get; }
    public Relationship Relationship { get; }
    public ContextLeader ContextLeader { get; }
    public ContextMember ContextMember { get; }
    public Attendance Attendance { get; }
    public PersonExternalRef ExternalRef { get; }
    public IReadOnlyList<ITenanted> Entities { get; }
    public IReadOnlyDictionary<Type, Guid[]> ExpectedIds { get; }
}
