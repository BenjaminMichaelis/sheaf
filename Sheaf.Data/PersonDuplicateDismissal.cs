using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// Staff decided two people who look alike (same name, email, or phone) are different people, so the pair no longer
/// appears among suggested duplicates. The pair is unordered; <see cref="PersonId"/> is the lower identifier.
/// </summary>
public sealed class PersonDuplicateDismissal : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid PersonId { get; set; }
    public Guid OtherPersonId { get; set; }
    public DateTimeOffset DismissedAt { get; set; }
    /// <summary>The account that dismissed the pair. Not a foreign key, like <see cref="Person.CreatedByUserId"/>.</summary>
    [MaxLength(450)]
    public string? DismissedByUserId { get; set; }
}
