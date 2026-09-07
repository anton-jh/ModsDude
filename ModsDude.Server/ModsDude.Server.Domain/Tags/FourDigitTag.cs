using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Server.Domain.Tags;

/// <summary>
/// Four digits derived from an identifier, for telling two things of the same name apart at the
/// point of display.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the identifier and therefore not from the order anything was created in: of two
/// things called the same, neither is the original and neither is the copy, which a counted suffix
/// could never manage. The same identifier always gives the same digits, so a tag survives a
/// rename and is something a person can learn and say out loud.
/// </para>
/// <para>
/// A tag is not an identifier and nothing looks anything up by one. It exists to be read, which is
/// why four digits is enough: it only has to separate the handful of rows in front of a reader, and
/// a collision there costs a moment's confusion rather than a wrong row.
/// </para>
/// </remarks>
public static class FourDigitTag
{
    private const int _modulus = 10000;


    public static string For(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        return (BinaryPrimitives.ReadUInt32BigEndian(hash) % _modulus)
            .ToString("D4", CultureInfo.InvariantCulture);
    }
}
