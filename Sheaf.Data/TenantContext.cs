namespace Sheaf.Data;

public sealed class TenantContext : ICurrentChurch
{
    public Guid? ChurchId { get; set; }

    public Guid? LeaderId { get; set; }
}
