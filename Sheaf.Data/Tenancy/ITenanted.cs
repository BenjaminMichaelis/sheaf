namespace Sheaf.Data.Tenancy;

/// <summary>Marks an entity that belongs to exactly one church.</summary>
public interface ITenanted
{
    Guid ChurchId { get; set; }
}

/// <summary>Implemented by a DbContext to expose the church whose data it may access.</summary>
public interface ITenantContext
{
    Guid ChurchId { get; }
}
