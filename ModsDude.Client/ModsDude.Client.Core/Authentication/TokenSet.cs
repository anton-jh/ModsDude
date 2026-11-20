namespace ModsDude.Client.Core.Authentication;

/// <summary>What the token endpoint hands back: the access token, the refresh token that replaces it, and who they are for.</summary>
public sealed record TokenSet(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    SignedInIdentity Identity);

/// <summary>Who the tokens are for, as the identity token says.</summary>
/// <param name="Subject">The server's id for the user, and what decides whether a sign-in is somebody else.</param>
public sealed record SignedInIdentity(string Subject, string Name);
