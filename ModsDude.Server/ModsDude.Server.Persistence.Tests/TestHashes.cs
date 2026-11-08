using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>A content hash in the one format the database accepts, distinct per seed.</summary>
internal static class TestHashes
{
    public static string Of(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
}
