using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

[AllowAnonymous]
public class SignInPageModel(
    UserManager<UserAccount> userManager,
    SignInManager<UserAccount> signInManager)
    : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    [Required(ErrorMessage = "Enter your email.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = "";

    /// <summary>Set when the password was right but the address has not been confirmed yet.</summary>
    public bool NeedsConfirmation { get; private set; }


    public void OnGet()
    {
        ModelState.Clear();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var account = await userManager.FindByEmailAsync(Email.Trim());
        if (account is null)
        {
            ModelState.AddModelError("", "Wrong email or password.");
            return Page();
        }

        // Identity would say so before checking the password, telling anyone which addresses have an
        // account waiting for confirmation. Wrong guesses count towards the lockout all the same.
        if (!await signInManager.CanSignInAsync(account))
        {
            if (await userManager.IsLockedOutAsync(account))
            {
                ModelState.AddModelError("", "Too many wrong passwords. Try again in 15 minutes.");
            }
            else if (await userManager.CheckPasswordAsync(account, Password))
            {
                NeedsConfirmation = true;
            }
            else
            {
                await userManager.AccessFailedAsync(account);
                ModelState.AddModelError("", "Wrong email or password.");
            }

            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(account, Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectToReturnUrl(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Too many wrong passwords. Try again in 15 minutes.");
        }
        else
        {
            ModelState.AddModelError("", "Wrong email or password.");
        }

        return Page();
    }
}
