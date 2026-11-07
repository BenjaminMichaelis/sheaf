using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Touchpoint : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid PersonId { get; set; }

    public Guid AuthorLeaderId { get; set; }

    public Guid? ContextId { get; set; }

    public required string Note { get; set; }

    public NoteVisibility NoteVisibility { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public enum NoteVisibility
{
    Private,
    Shared
}
