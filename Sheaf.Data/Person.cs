using System.ComponentModel.DataAnnotations.Schema;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public class Person : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public required string Name { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public ConsentSource ConsentSource { get; set; }

    public DateTimeOffset? StoppedAt { get; set; }

    public bool IsMinor { get; set; }

    public Guid? GuardianPersonId { get; set; }

    public PersonVisibility Visibility { get; set; }
}

public enum ConsentSource
{
    LeaderEntered,
    QrForm,
    Imported
}

public enum PersonVisibility
{
    Standard,
    Restricted
}
