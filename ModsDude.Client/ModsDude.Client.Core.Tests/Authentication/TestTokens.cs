using ModsDude.Client.Core.Authentication;
using System.Text;
using System.Text.Json;

namespace ModsDude.Client.Core.Tests.Authentication;

internal static class TestTokens
{
    /// <summary>An unsigned JWT carrying the claims, which is all the client reads of one.</summary>
    public static string IdentityToken(object claims)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Encode("""{"alg":"none"}""")}.{Encode(JsonSerializer.Serialize(claims))}.signature";
    }

    public static TokenSet Set(string accessToken, DateTimeOffset expiresAt, string refreshToken, string subject = "user-1")
        => new(accessToken, expiresAt, refreshToken, new SignedInIdentity(subject, "Name"));
}
