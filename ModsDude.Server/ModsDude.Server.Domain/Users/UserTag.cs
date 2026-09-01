using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Server.Domain.Users;

/// <summary>
/// Four digits that separate two users sharing a <see cref="DisplayName"/>.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the subject id, and therefore not from the order anybody signed up in: of two
/// Antons neither is the original and neither is the copy, which a counted suffix could never
/// manage. It is the same four digits everywhere the user appears and it survives a rename at the
/// identity provider, so it is something a person can learn about themselves and say out loud.
/// </para>
/// <para>
/// A tag is not an identifier and nothing looks a user up by one. It exists to be read, which is
/// why four digits is enough: it only has to separate the handful of people in front of a reader,
/// and a collision there costs a moment's confusion rather than a wrong row.
/// </para>
/// </remarks>
public static class UserTag
{
    private const int _modulus = 10000;


    public static string For(UserId userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId.Value));

        return (BinaryPrimitives.ReadUInt32BigEndian(hash) % _modulus)
            .ToString("D4", CultureInfo.InvariantCulture);
    }
}
