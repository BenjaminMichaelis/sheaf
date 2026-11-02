namespace Sheaf.Data;

public interface ITenantContext
{
    Guid? ChurchId { get; }

    Guid? LeaderId { get; }

    string Scope { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? ChurchId { get; set; }

    public Guid? LeaderId { get; set; }

    public string Scope { get; set; } = string.Empty;
}
