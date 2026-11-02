namespace Sheaf.Data;

public interface ITenantContext
{
    Guid? ChurchId { get; }

    Guid? LeaderId { get; }
}
