using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace Sheaf.Data;

[Index(nameof(Slug), IsUnique = true)]
public class Church
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The IANA time zone the church meets in, such as "America/Chicago". Meeting dates are local calendar dates in
    /// this zone; <see cref="ChurchCalendar"/> turns them into UTC instants.
    /// </summary>
    [MaxLength(64)]
    public required string TimeZoneId { get; set; }
}
