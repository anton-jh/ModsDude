using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// A sign-in the user finished in the browser that has not replaced the current account yet. Adopted
/// or dropped by whoever prompted for it.
/// </summary>
public sealed record PendingSignIn(TokenSet Tokens, SignedInAccount Account);
