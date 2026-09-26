namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Who the app is signed in as, as far as the token can say.
/// </summary>
/// <param name="Id">
/// MSAL's home account identifier - stable across sign-ins, and what "a different user" is decided
/// on. Not the server's user id: the client never needs one.
/// </param>
/// <param name="DisplayName">
/// The token's <c>name</c> claim - what the identity provider called them, which is only what this
/// system called them until they first renamed themselves. The stored name has to be asked for; see
/// <see cref="Core.Services.CurrentUserService"/>.
/// </param>
/// <param name="Email">
/// The address they sign in with, which is the provider's username for this tenant. Shown on the
/// account page, and handed back to the sign-in page so a password reset does not start by asking
/// for it again.
/// </param>
public sealed record SignedInAccount(string Id, string DisplayName, string Email);
