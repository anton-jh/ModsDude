using Microsoft.Identity.Client;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// A sign-in the user finished in the browser that has not replaced the current account yet. Adopted
/// or discarded by whoever prompted for it.
/// </summary>
public sealed record PendingSignIn(AuthenticationResult Result, SignedInAccount Account);
