using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public sealed class Person : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(100)]
    public required string FirstName { get; set; }
    [MaxLength(100)]
    public string? LastName { get; set; }
    [NotMapped]
    public string Name => FirstName + (LastName == null ? string.Empty : " " + LastName);
    [MaxLength(320)]
    public string? Email { get; set; }
    [MaxLength(50)]
    public string? Phone { get; set; }
    public Guid? HouseholdId { get; set; }
    [MaxLength(450)]
    public string? UserId { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>When the record was created; duplicate review uses it to tell the original from a later copy.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>
    /// The account that created the record, or null for imports and seeds. Not a foreign key, so the audit trail
    /// survives the account being deleted.
    /// </summary>
    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }
    /// <summary>
    /// The person this record was merged into as a duplicate, or null while it is a record of its own. A merged record is
    /// kept, inactive, with the values it had, so history survives; everything that pointed at it now points at
    /// <see cref="MergedIntoId"/>. A merge cannot be undone.
    /// </summary>
    public Guid? MergedIntoId { get; set; }
    /// <summary>When the record was merged into <see cref="MergedIntoId"/>.</summary>
    public DateTimeOffset? MergedAt { get; set; }
    /// <summary>The account that merged the record. Not a foreign key, like <see cref="CreatedByUserId"/>.</summary>
    [MaxLength(450)]
    public string? MergedByUserId { get; set; }
    /// <summary>Changes on every write to this row, including household moves; clients send it back as If-Match.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
