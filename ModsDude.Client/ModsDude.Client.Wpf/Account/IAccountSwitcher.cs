using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Every sign-in the user asks for: the first one, creating an account, signing in again and switching
/// user. A sign-in as somebody else stops everything the current session runs first.
/// </summary>
public interface IAccountSwitcher
{
    /// <returns>
    /// True where somebody is signed in from it - the same account again, or a new one. False where
    /// the user gave up on the page, would rather keep working, or the running work did not stop.
    /// </returns>
    Task<bool> SignInAsync(SignInPrompt prompt, CancellationToken cancellationToken);
}
