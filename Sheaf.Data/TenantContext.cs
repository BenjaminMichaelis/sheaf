namespace Sheaf.Data;

public sealed class TenantContext : ITenantContext
{
    public Guid? ChurchId { get; set; }

    public Guid? LeaderId { get; set; }

    public string Scope { get; set; } = string.Empty;
}
