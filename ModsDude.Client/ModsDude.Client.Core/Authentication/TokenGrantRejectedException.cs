namespace ModsDude.Client.Core.Authentication;

/// <summary>
/// The server refused the code or refresh token: it expired, was revoked, or the account can no longer
/// sign in. Only signing in again gets new tokens.
/// </summary>
public sealed class TokenGrantRejectedException(string error, string? description)
    : Exception($"The server refused the grant: {error}{(description is null ? "" : $" ({description})")}.")
{
    public string Error { get; } = error;
}
