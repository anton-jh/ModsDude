using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Wpf.Account;

public interface IAuthenticationService : IAccessTokenAccessor
{
    /// <summary>
    /// Raised when the signed-in account becomes a different one - the first sign-in, or a switch.
    /// Always on the UI thread: everything listening to this rebuilds bound state.
    /// </summary>
    event EventHandler<SignedInAccount>? AccountChanged;

    /// <summary>Null until the first sign-in completes, and never null again.</summary>
    SignedInAccount? CurrentAccount { get; }

    /// <summary>
    /// Raised when <see cref="NeedsSignIn"/> changes. Always on the UI thread.
    /// </summary>
    event EventHandler? NeedsSignInChanged;

    /// <summary>
    /// Whether a token was asked for and could only be had by signing in again. Cleared by the next
    /// sign-in that completes.
    /// </summary>
    bool NeedsSignIn { get; }

    /// <summary>
    /// Signs in as the account kept from the last run, only if that takes no one at the keyboard.
    /// </summary>
    /// <remarks>
    /// What it does not swallow is anything other than "needs the user" - a network that is not up
    /// yet arrives as an exception, and it is the caller's to decide what that means.
    /// </remarks>
    /// <returns>False where the account has to be asked for interactively, or there is none.</returns>
    Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the sign-in or sign-up page, replacing any such page still open. Nothing changes until the
    /// result is adopted.
    /// </summary>
    /// <returns>Null where the user closed the page, asked for another before finishing it, or the caller cancelled.</returns>
    Task<PendingSignIn?> PromptAsync(SignInPrompt prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a pending sign-in the account. Raises <see cref="AccountChanged"/> where it is a different user.
    /// </summary>
    Task AdoptAsync(PendingSignIn signIn);
}
