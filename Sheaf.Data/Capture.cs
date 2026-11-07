using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Capture : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public string? RawText { get; set; }

    public string? Transcript { get; set; }

    public string? ExtractionJson { get; set; }

    public required string Status { get; set; }
}
