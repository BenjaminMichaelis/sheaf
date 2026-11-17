namespace Sheaf.Data;

/// <summary>Optional church and leader selection for the current request, including requests before selection.</summary>
public interface ICurrentChurch
{
    Guid? ChurchId { get; }

    /// <summary>The selected leader's church membership ID, not the global Identity user ID.</summary>
    Guid? LeaderId { get; }
}
