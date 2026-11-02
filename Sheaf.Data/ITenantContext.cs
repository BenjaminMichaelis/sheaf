namespace Sheaf.Data;

public interface ITenantContext
{
    Guid? ChurchId { get; }

    TenantSource Source { get; }

    Guid? LeaderId { get; }

    string Scope { get; }
}

public enum TenantSource
{
    Cookie,
    Jwt,
    CardDav,
    PublicSlug,
    Job
}

public sealed class TenantContext : ITenantContext
{
    private TenantSource? _source;

    public Guid? ChurchId { get; set; }

    public TenantSource Source
    {
        get => _source ?? throw new InvalidOperationException("Tenant source has not been set.");
        set => _source = value;
    }

    public Guid? LeaderId { get; set; }

    public string Scope { get; set; } = string.Empty;
}
