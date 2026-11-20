using Microsoft.AspNetCore.Identity;

namespace ModsDude.Server.Persistence.Accounts;

/// <summary>
/// How a user signs in: their email and password. Its <see cref="IdentityUser{TKey}.Id"/> is the
/// <see cref="Domain.Users.UserId"/> of the user it belongs to, and the database holds the two
/// together - see the migration that adds this table.
/// </summary>
public class UserAccount : IdentityUser
{
    /// <summary>
    /// Chosen by the sign-up page and repeated with a resubmit, so a repeat is recognised rather than
    /// refused as an address already in use. Identifies the request, never the account.
    /// </summary>
    public Guid SignUpRequestId { get; set; }
}
