using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Persistence.Accounts;

namespace ModsDude.Server.Api.Accounts;

[Authorize(Policy = AccountsSetup.SignedInPolicy)]
public class AccountHomePageModel(
    UserManager<UserAccount> userManager,
    SignInManager<UserAccount> signInManager)
    : AccountPageModel
{
    public string Email { get; private set; } = "";

    /// <summary>What the page that sent the user here finished, shown once.</summary>
    [TempData]
    public string? Notice { get; set; }


    public async Task<IActionResult> OnGetAsync()
    {
        if (await userManager.GetUserAsync(User) is not UserAccount account)
        {
            await signInManager.SignOutAsync();
            return Redirect(AccountPagePaths.SignIn(AccountPagePaths.Home));
        }

        Email = account.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostSignOutAsync()
    {
        await signInManager.SignOutAsync();

        return Redirect(AccountPagePaths.SignIn(null));
    }
}
