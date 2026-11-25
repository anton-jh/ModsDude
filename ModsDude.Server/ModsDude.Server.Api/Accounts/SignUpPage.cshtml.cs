using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Accounts.Email;
using ModsDude.Server.Api.Accounts.SignUp;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Accounts;
using System.ComponentModel.DataAnnotations;

namespace ModsDude.Server.Api.Accounts;

[AllowAnonymous]
public class SignUpPageModel(
    IAccountSignUp signUp,
    UserManager<UserAccount> userManager,
    IAccountEmails emails,
    ILogger<SignUpPageModel> logger)
    : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public Guid RequestId { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "That is not an email address.")]
    [StringLength(AccountsSetup.MaximumEmailLength, ErrorMessage = "An email cannot be longer than 256 characters.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter a name.")]
    public string DisplayName { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Choose a password.")]
    public string Password { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Enter the password again.")]
    [Compare(nameof(Password), ErrorMessage = "The passwords are not the same.")]
    public string ConfirmPassword { get; set; } = "";

    [BindProperty]
    public string? TrustCode { get; set; }

    [BindProperty]
    public List<string?> InviteCodes { get; set; } = [];

    public int MinimumPasswordLength => AccountsSetup.MinimumPasswordLength;

    public int MaximumDisplayNameLength => Domain.Users.DisplayName.MaximumLength;


    public void OnGet()
    {
        RequestId = Guid.NewGuid();
        InviteCodes = [null];
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (InviteCodes.Count == 0)
        {
            InviteCodes = [null];
        }

        // A form without one would share the empty id with every other such form.
        if (RequestId == Guid.Empty)
        {
            ModelState.AddModelError("", "This form is out of date. Reload the page and try again.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var outcome = await signUp.SignUpAsync(
            new SignUpForm(RequestId, Email, DisplayName, Password, TrustCode, InviteCodes),
            cancellationToken);

        if (outcome is SignUpOutcome.Refused refused)
        {
            foreach (var error in refused.Errors)
            {
                ModelState.AddModelError(KeyOf(error), error.Message);
            }

            return Page();
        }

        var succeeded = (SignUpOutcome.Succeeded)outcome;

        // A repeat has already been sent its link, and can ask for another on the next page.
        if (!succeeded.IsRepeat)
        {
            await SendConfirmationAsync(succeeded.AccountId, cancellationToken);
        }

        return RedirectToPage("/Accounts/CheckEmailPage", new { email = succeeded.Email, returnUrl = LocalOnly(ReturnUrl) });
    }


    private async Task SendConfirmationAsync(string accountId, CancellationToken cancellationToken)
    {
        if (await userManager.FindByIdAsync(accountId) is not UserAccount account || account.Email is null)
        {
            throw new InvalidOperationException($"Account '{accountId}' was signed up but cannot be found.");
        }

        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(account);
            var link = AbsolutePageUrl("/Accounts/ConfirmEmailPage", new { accountId = account.Id, code = EncodeToken(token), returnUrl = LocalOnly(ReturnUrl) });

            await emails.SendEmailConfirmationAsync(account.Email, link, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // The account is there either way, and the next page offers to send the link again.
            logger.LogError(exception, "Could not send the confirmation email of account {AccountId}.", account.Id);
        }
    }

    private static string KeyOf(SignUpError error) => error.Field switch
    {
        SignUpField.Email => nameof(Email),
        SignUpField.DisplayName => nameof(DisplayName),
        SignUpField.Password => nameof(Password),
        SignUpField.TrustCode => nameof(TrustCode),
        SignUpField.InviteCode => $"{nameof(InviteCodes)}[{error.InviteIndex}]",
        _ => ""
    };
}
