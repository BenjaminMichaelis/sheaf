using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;

namespace Sheaf.Data;

/// <summary>
/// A sign-in account. Its email is the login and is changed only by the account holder after proving the new
/// address; it is never copied from or to <see cref="Person.Email"/>, which church staff and leaders can edit.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public const int NameMaxLength = 100;

    /// <summary>The longest email the account endpoints accept; Identity stores up to this many characters.</summary>
    public const int EmailMaxLength = 256;

    [MaxLength(NameMaxLength)]
    public string FirstName { get; set; } = "";

    [MaxLength(NameMaxLength)]
    public string LastName { get; set; } = "";
}
