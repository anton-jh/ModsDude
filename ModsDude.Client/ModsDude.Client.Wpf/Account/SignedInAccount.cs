namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Who the app is signed in as, as far as the identity token says.
/// </summary>
/// <param name="Id">The server's user id, and what "a different user" is decided on.</param>
/// <param name="DisplayName">
/// The name in the token, shown until the server's answer arrives; see
/// <see cref="Core.Users.ICurrentUserStore"/>.
/// </param>
public sealed record SignedInAccount(string Id, string DisplayName);
