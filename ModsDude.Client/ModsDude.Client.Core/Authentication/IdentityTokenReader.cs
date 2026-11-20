using System.Text.Json;

namespace ModsDude.Client.Core.Authentication;

/// <summary>
/// Reads the claims of an identity token without checking its signature. Safe only because the token
/// comes straight from the server's token endpoint over TLS, which OpenID Connect allows (Core 3.1.3.7).
/// </summary>
public static class IdentityTokenReader
{
    public static SignedInIdentity Read(string identityToken)
    {
        var parts = identityToken.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("The identity token is not a JWT.");
        }

        using var payload = JsonDocument.Parse(Base64Url.Decode(parts[1]));
        var claims = payload.RootElement;

        var subject = Claim(claims, "sub");
        if (string.IsNullOrEmpty(subject))
        {
            throw new FormatException("The identity token names no subject.");
        }

        return new SignedInIdentity(subject, Claim(claims, "name") ?? "");
    }


    private static string? Claim(JsonElement claims, string name)
        => claims.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}
