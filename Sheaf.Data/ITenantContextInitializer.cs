namespace Sheaf.Data;

/// <summary>Authentication-only initialization of the request's church and membership selection.</summary>
public interface ITenantContextInitializer
{
    void Initialize(Guid? churchId, Guid? leaderId);
}
