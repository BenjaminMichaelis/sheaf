namespace Sheaf.Data;

public class Church
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public required string Slug { get; set; }

    public required string Name { get; set; }
}
