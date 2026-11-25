using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Wpf.Shell;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;

namespace ModsDude.Client.Wpf.Account;

/// <remarks>
/// The browser comes first and the question after, so somebody signing in as themselves again is never
/// asked about the work they are about to keep.
/// </remarks>
public sealed class AccountSwitcher(
    IAuthenticationService authentication,
    ISessionEnd sessionEnd,
    INavigationLockService navigationLock,
    Lazy<IModalService> modalService,
    ILogger<AccountSwitcher> logger) : IAccountSwitcher
{
    public async Task<bool> SignInAsync(SignInPrompt prompt, CancellationToken cancellationToken)
    {
        if (await authentication.PromptAsync(prompt, cancellationToken) is not PendingSignIn signIn)
        {
            return false;
        }

        if (authentication.CurrentAccount is SignedInAccount current && current.Id != signIn.Account.Id
            && await EndSessionAsync() is false)
        {
            return false;
        }

        await authentication.AdoptAsync(signIn);

        return true;
    }


    /// <summary>
    /// Everything built from the current account is about to be thrown away, and every request after
    /// the switch carries the new account's token - so nothing of the old session may still be running.
    /// </summary>
    private async Task<bool> EndSessionAsync()
    {
        if (await sessionEnd.ConfirmAsync("Switch user?", "Stop and switch") is false)
        {
            return false;
        }

        if (await sessionEnd.StopAsync() is false)
        {
            logger.LogWarning("Did not switch user: running work did not stop in time.");

            await modalService.Value.Show(ConfirmationModalViewModel.Refusal(
                "Could not switch user",
                "Running work did not stop. Try again when it has finished."));

            return false;
        }

        navigationLock.Clear();

        return true;
    }
}
