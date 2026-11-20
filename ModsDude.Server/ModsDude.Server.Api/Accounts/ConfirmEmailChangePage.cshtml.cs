using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Opened from the link sent to the new address, possibly in another browser than the one that asked
/// for it. Changes on a button press, so a mail scanner that follows every link changes nothing.
/// </summary>
[AllowAnonymous]
public class ConfirmEmailChangePageModel(UserManager<UserAccount> userManager) : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string AccountId { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Code { get; set; } = "";

    public bool Changed { get; private set; }

    public string? Error { get; private set; }


    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var account = await userManager.FindByIdAsync(AccountId);
        var token = DecodeToken(Code);

        if (account is null || token is null)
        {
            Error = "This link does not work. Ask for a new one.";
            return Page();
        }

        IdentityResult result;

        try
        {
            result = await userManager.ChangeEmailAsync(account, Email, token);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(UserAccountConstraints.EmailIndex))
        {
            result = IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(Email));
        }

        if (!result.Succeeded)
        {
            Error = result.Errors.Any(x => x.Code == nameof(IdentityErrorDescriber.DuplicateEmail))
                ? "Another account already uses this email."
                : "This link does not work. Ask for a new one.";

            return Page();
        }

        Changed = true;
        return Page();
    }
}
