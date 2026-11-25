using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

[Authorize(Policy = AccountsSetup.SignedInPolicy)]
public class ChangePasswordPageModel(
    UserManager<UserAccount> userManager,
    SignInManager<UserAccount> signInManager,
    IAccountSessions sessions)
    : AccountPageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Enter your current password.")]
    public string CurrentPassword { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Choose a password.")]
    public string NewPassword { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter the password again.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords are not the same.")]
    public string ConfirmPassword { get; set; } = "";

    public int MinimumPasswordLength => AccountsSetup.MinimumPasswordLength;

    [TempData]
    public string? Notice { get; set; }


    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (await userManager.GetUserAsync(User) is not UserAccount account)
        {
            await signInManager.SignOutAsync();
            return Redirect(AccountPagePaths.SignIn(AccountPagePaths.Home));
        }

        var check = await signInManager.CheckPasswordSignInAsync(account, CurrentPassword, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(
                nameof(CurrentPassword),
                check.IsLockedOut ? "Too many wrong passwords. Try again in 15 minutes." : "Wrong password.");

            return Page();
        }

        var result = await userManager.ChangePasswordAsync(account, CurrentPassword, NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? nameof(CurrentPassword) : nameof(NewPassword),
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "Wrong password." : error.Description);
            }

            return Page();
        }

        await sessions.RevokeAllAsync(account.Id, cancellationToken);
        await signInManager.RefreshSignInAsync(account);

        Notice = "Password changed. Sign in to ModsDude again on every device.";
        return RedirectToPage("/Accounts/AccountHomePage");
    }
}
