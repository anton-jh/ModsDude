using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Authentication;

/// <summary>Proof Key for Code Exchange (RFC 7636), with the S256 method.</summary>
public static class Pkce
{
    public const string Method = "S256";


    public static string CreateVerifier() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeFor(string verifier) => Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
