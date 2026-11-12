using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Relationship : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public Guid PersonId { get; set; }

    public Guid LeaderId { get; set; }

    public required string Kind { get; set; }

    public RelationshipStatus Status { get; set; }

    public RelationshipCloseReason? CloseReason { get; set; }

    public DateTimeOffset StartedOn { get; set; }

    public DateTimeOffset? ClosedOn { get; set; }
}

public enum RelationshipStatus
{
    Open,
    Closed
}

public enum RelationshipCloseReason
{
    Placed,
    Released,
    StaffClosed,
    CampaignEnded
}
