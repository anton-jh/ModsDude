using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Persistence.Accounts;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Opened from the confirmation email. Confirms on a button press rather than on opening, so a mail
/// scanner that follows every link does not confirm on the user's behalf.
/// </summary>
[AllowAnonymous]
public class ConfirmEmailPageModel(UserManager<UserAccount> userManager) : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string AccountId { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Confirmed { get; private set; }

    public bool Failed { get; private set; }


    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var account = await userManager.FindByIdAsync(AccountId);
        var token = DecodeToken(Code);

        if (account is null || token is null)
        {
            Failed = true;
            return Page();
        }

        if (account.EmailConfirmed)
        {
            Confirmed = true;
            return Page();
        }

        var result = await userManager.ConfirmEmailAsync(account, token);

        Confirmed = result.Succeeded;
        Failed = !result.Succeeded;

        return Page();
    }
}
