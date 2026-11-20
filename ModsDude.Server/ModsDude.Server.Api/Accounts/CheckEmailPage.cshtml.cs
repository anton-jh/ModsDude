using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Accounts.Email;
using ModsDude.Server.Persistence.Accounts;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Where sign-up ends: the account exists, and its email has to be confirmed before it can sign in.
/// </summary>
[AllowAnonymous]
public class CheckEmailPageModel(
    UserManager<UserAccount> userManager,
    IAccountEmails emails,
    ILogger<CheckEmailPageModel> logger)
    : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    public bool Resent { get; private set; }

    public bool CouldNotSend { get; private set; }


    public void OnGet()
    {
    }

    /// <remarks>Says the same whatever the address.</remarks>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = await userManager.FindByEmailAsync(Email.Trim());

        if (account is { EmailConfirmed: false, Email: not null })
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(account);
                var link = AbsolutePageUrl("/Accounts/ConfirmEmailPage", new { accountId = account.Id, code = EncodeToken(token), returnUrl = LocalOnly(ReturnUrl) });

                await emails.SendEmailConfirmationAsync(account.Email, link, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Could not send the confirmation email of account {AccountId} again.", account.Id);

                CouldNotSend = true;
                return Page();
            }
        }

        Resent = true;
        return Page();
    }
}
