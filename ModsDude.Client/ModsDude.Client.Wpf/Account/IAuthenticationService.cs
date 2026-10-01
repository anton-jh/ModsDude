using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Wpf.Account;

public interface IAuthenticationService : IAccessTokenAccessor
{
    /// <summary>
    /// Raised when the signed-in account becomes a different one - the first sign-in, or a switch.
    /// Always on the UI thread: MSAL finishes wherever it likes, and everything listening to this
    /// rebuilds bound state.
    /// </summary>
    event EventHandler<SignedInAccount>? AccountChanged;

    /// <summary>Null until the first sign-in completes, and never null again.</summary>
    SignedInAccount? CurrentAccount { get; }

    /// <summary>
    /// Signs in only if that takes no one at the keyboard.
    /// </summary>
    /// <remarks>
    /// For an app that started itself at sign-in and has no window up: <see cref="IAccessTokenAccessor.Get"/> falls back to
    /// a browser, and a browser tab appearing unprompted at logon is the one thing a background start
    /// must not do. What it does not swallow is anything other than "needs the user" - a network that
    /// is not up yet arrives as an exception, and it is the caller's to decide what that means.
    /// </remarks>
    /// <returns>False where the account has to be asked for interactively, or there is none.</returns>
    Task<bool> TrySignInSilentlyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Prompts for an account and signs in as whoever is picked.
    /// </summary>
    /// <returns>
    /// False where the user cancelled the prompt, asked for another before finishing it, or picked the
    /// account they were already on. Either way nothing changed and no event was raised.
    /// </returns>
    Task<bool> SwitchUser(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the sign-in page on the current account, where "Forgot password?" is the way to a new
    /// password: the identity provider has no page for changing one while signed in, and a reset by
    /// emailed code is the change it offers.
    /// </summary>
    /// <remarks>
    /// Finishing the page signs in again, which for the same account changes nothing here. Somebody
    /// who signs in as a different account on it has switched user, and is treated exactly as
    /// <see cref="SwitchUser"/> would treat them.
    /// </remarks>
    /// <returns>False where the user closed the page, or asked for another, without finishing it.</returns>
    Task<bool> ResetPassword(CancellationToken cancellationToken);
}
