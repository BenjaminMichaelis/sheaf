using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class ContextMember : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid ContextId { get; set; }

    public Guid PersonId { get; set; }

    public DateTimeOffset StartedOn { get; set; }

    public DateTimeOffset? EndedOn { get; set; }
}
