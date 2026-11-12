using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

[Index(nameof(ChurchId), nameof(System), nameof(ExternalId), IsUnique = true)]
public class PersonExternalRef : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid PersonId { get; set; }

    public PersonExternalSystem System { get; set; }

    public required string ExternalId { get; set; }

    public DateTimeOffset? SyncedAt { get; set; }
}

public enum PersonExternalSystem
{
    Pco,
    Csv
}
