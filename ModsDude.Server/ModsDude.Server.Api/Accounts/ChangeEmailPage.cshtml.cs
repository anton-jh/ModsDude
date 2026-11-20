using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Accounts.Email;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Sends a link to the new address. The account keeps the old one until that link is opened.
/// </summary>
[Authorize(Policy = AccountsSetup.SignedInPolicy)]
public class ChangeEmailPageModel(
    UserManager<UserAccount> userManager,
    SignInManager<UserAccount> signInManager,
    IAccountEmails emails,
    ILogger<ChangeEmailPageModel> logger)
    : AccountPageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Enter the new email.")]
    [EmailAddress(ErrorMessage = "That is not an email address.")]
    [StringLength(AccountsSetup.MaximumEmailLength, ErrorMessage = "An email cannot be longer than 256 characters.")]
    public string NewEmail { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = "";

    public string CurrentEmail { get; private set; } = "";

    public string? SentTo { get; private set; }


    public async Task<IActionResult> OnGetAsync()
    {
        if (await userManager.GetUserAsync(User) is not UserAccount account)
        {
            return await SignOutAsync();
        }

        CurrentEmail = account.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not UserAccount account)
        {
            return await SignOutAsync();
        }

        CurrentEmail = account.Email ?? "";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var check = await signInManager.CheckPasswordSignInAsync(account, Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            ModelState.AddModelError(nameof(Password), "Too many wrong passwords. Try again in 15 minutes.");
            return Page();
        }

        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(Password), "Wrong password.");
            return Page();
        }

        var newEmail = NewEmail.Trim();

        if (userManager.NormalizeEmail(newEmail) == account.NormalizedEmail)
        {
            ModelState.AddModelError(nameof(NewEmail), "That is already your email.");
            return Page();
        }

        if (await userManager.FindByEmailAsync(newEmail) is not null)
        {
            ModelState.AddModelError(nameof(NewEmail), "Another account already uses this email.");
            return Page();
        }

        try
        {
            var token = await userManager.GenerateChangeEmailTokenAsync(account, newEmail);
            var link = AbsolutePageUrl("/Accounts/ConfirmEmailChangePage", new { accountId = account.Id, email = newEmail, code = EncodeToken(token) });

            await emails.SendEmailChangeAsync(newEmail, link, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not send the email change link of account {AccountId}.", account.Id);

            ModelState.AddModelError("", "Could not send the email. Try again in a moment.");
            return Page();
        }

        SentTo = newEmail;
        return Page();
    }


    private async Task<IActionResult> SignOutAsync()
    {
        await signInManager.SignOutAsync();
        return Redirect(AccountPagePaths.SignIn(AccountPagePaths.Home));
    }
}
