using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public sealed class Household : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    [MaxLength(200)]
    public string? Label { get; set; }
    /// <summary>Changes whenever the label or membership changes; clients send it back as If-Match.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
