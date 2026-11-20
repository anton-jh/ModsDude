using Microsoft.AspNetCore.Identity;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Accounts.Passwords;

/// <summary>
/// The rules beyond Identity's minimum length: a maximum length, and none of the user's own email or
/// name inside the password.
/// </summary>
public class PasswordRulesValidator(ApplicationDbContext dbContext) : IPasswordValidator<UserAccount>
{
    public const int MaximumLength = 256;

    /// <summary>Shorter parts of an address or name are too likely to turn up by chance.</summary>
    private const int _shortestPersonalPart = 3;


    public async Task<IdentityResult> ValidateAsync(UserManager<UserAccount> manager, UserAccount user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        if (password.Length > MaximumLength)
        {
            return Failed("PasswordTooLong", $"A password cannot be longer than {MaximumLength} characters.");
        }

        var emailName = user.Email?.Split('@')[0] ?? "";
        if (Contains(password, emailName))
        {
            return Failed("PasswordContainsEmail", "A password cannot contain your email address.");
        }

        // Found in the change tracker during sign-up, where the user is saved in the same transaction
        // just before the account.
        var domainUser = await dbContext.Users.FindAsync(new UserId(user.Id));
        if (domainUser is not null && Contains(password, domainUser.DisplayName.Value))
        {
            return Failed("PasswordContainsName", "A password cannot contain your name.");
        }

        return IdentityResult.Success;
    }


    private static bool Contains(string password, string personal)
        => personal.Trim().Length >= _shortestPersonalPart
        && password.Contains(personal.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IdentityResult Failed(string code, string description)
        => IdentityResult.Failed(new IdentityError { Code = code, Description = description });
}
