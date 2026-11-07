using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Attendance : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid ContextId { get; set; }

    public DateOnly SessionDate { get; set; }

    public Guid PersonId { get; set; }

    public Guid RecordedByLeaderId { get; set; }
}
