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
}
