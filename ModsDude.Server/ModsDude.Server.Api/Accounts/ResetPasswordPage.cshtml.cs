using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

[AllowAnonymous]
public class ResetPasswordPageModel(
    UserManager<UserAccount> userManager,
    IAccountSessions sessions)
    : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string AccountId { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Code { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Choose a password.")]
    public string Password { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter the password again.")]
    [Compare(nameof(Password), ErrorMessage = "The passwords are not the same.")]
    public string ConfirmPassword { get; set; } = "";

    public int MinimumPasswordLength => AccountsSetup.MinimumPasswordLength;

    public bool Changed { get; private set; }


    public void OnGet()
    {
        ModelState.Clear();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var account = await userManager.FindByIdAsync(AccountId);
        var token = DecodeToken(Code);

        if (account is null || token is null)
        {
            ModelState.AddModelError("", "This link does not work. Ask for a new one.");
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(account, token, Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    error.Code.StartsWith("Password", StringComparison.Ordinal) ? nameof(Password) : "",
                    error.Code == nameof(IdentityErrorDescriber.InvalidToken) ? "This link does not work. Ask for a new one." : error.Description);
            }

            return Page();
        }

        // Whoever knew the old password is signed out everywhere, and the wrong guesses that came
        // before no longer count against the account.
        await sessions.RevokeAllAsync(account.Id, cancellationToken);
        await userManager.ResetAccessFailedCountAsync(account);
        await userManager.SetLockoutEndDateAsync(account, null);

        Changed = true;
        return Page();
    }
}
