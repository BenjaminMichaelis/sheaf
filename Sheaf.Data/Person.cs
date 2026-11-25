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
    /// <summary>Changes on every write to this row, including household moves; clients send it back as If-Match.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
