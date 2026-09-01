namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Who the app is signed in as, as far as the token can say.
/// </summary>
/// <param name="Id">
/// MSAL's home account identifier - stable across sign-ins, and what "a different user" is decided
/// on. Not the server's user id: the client never needs one.
/// </param>
/// <param name="DisplayName">
/// The token's <c>name</c> claim. Not the username this system stores - that is allocated
/// server-side from this same claim and has to be asked for; see
/// <see cref="Core.Services.CurrentUserService"/>.
/// </param>
public sealed record SignedInAccount(string Id, string DisplayName);
