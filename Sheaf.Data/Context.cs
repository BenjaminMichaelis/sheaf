using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Context : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public required string Name { get; set; }

    public ContextKind Kind { get; set; }

    public CustomFieldSchema CustomFieldSchema { get; set; } = new();
}

public enum ContextKind
{
    Campaign,
    Group,
    Assignment
}

public class CustomFieldSchema
{
    public List<CustomFieldDefinition> Fields { get; set; } = [];
}

public class CustomFieldDefinition
{
    public required string Name { get; set; }

    public required string Type { get; set; }
}
