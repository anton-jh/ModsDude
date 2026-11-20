using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Accounts.Email;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

[AllowAnonymous]
public class ForgotPasswordPageModel(
    UserManager<UserAccount> userManager,
    IAccountEmails emails,
    ILogger<ForgotPasswordPageModel> logger)
    : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    [Required(ErrorMessage = "Enter your email.")]
    public string Email { get; set; } = "";

    public bool Sent { get; private set; }

    public bool CouldNotSend { get; private set; }


    public void OnGet()
    {
        ModelState.Clear();
    }

    /// <remarks>
    /// Says the same whatever the address. Only a confirmed address is sent a link: an unconfirmed one
    /// was never proved to belong to whoever signed up with it.
    /// </remarks>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var account = await userManager.FindByEmailAsync(Email.Trim());

        if (account is { EmailConfirmed: true, Email: not null })
        {
            try
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(account);
                var link = AbsolutePageUrl("/Accounts/ResetPasswordPage", new { accountId = account.Id, code = EncodeToken(token) });

                await emails.SendPasswordResetAsync(account.Email, link, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Could not send the password reset email of account {AccountId}.", account.Id);

                CouldNotSend = true;
                return Page();
            }
        }

        Sent = true;
        return Page();
    }
}
