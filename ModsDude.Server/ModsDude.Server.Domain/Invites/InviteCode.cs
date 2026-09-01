using System.Security.Cryptography;

namespace ModsDude.Server.Domain.Invites;

/// <summary>
/// The canonical form of an invite code: twelve characters of the alphabet below, upper case and
/// undashed. <see cref="InviteCodes.Format"/> is what a person is shown; this is what is stored and
/// compared.
/// </summary>
public readonly record struct InviteCode(string Value);

/// <summary>
/// Invite codes are read off one screen and typed into another, or said out loud over voice chat, so
/// the alphabet is Crockford's base32: no letter that can be misheard or mistyped as a digit
/// survives in it, and the four that can - I, L, O, U - are either folded into the digit they look
/// like or refused outright.
/// </summary>
/// <remarks>
/// Twelve characters is sixty bits. That is not a password, but nothing about an invite depends on
/// it being one: it grants a level somebody chose to hand out, it can be capped and revoked, and it
/// is not guessable in any number of attempts a server will answer.
/// </remarks>
public static class InviteCodes
{
    public const int Length = 12;

    /// <summary>Codes are shown in threes of four, because that is how they are read out loud.</summary>
    public const int GroupSize = 4;

    private const string _alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";


    public static InviteCode Generate()
    {
        return new(new string(RandomNumberGenerator.GetItems<char>(_alphabet, Length)));
    }

    /// <summary>
    /// Accepts what somebody actually typed - any casing, dashes or spaces wherever they fell, and
    /// the four confusable letters - and hands back the one form the code is stored in.
    /// </summary>
    public static bool TryParse(string? input, out InviteCode code)
    {
        code = default;

        if (input is null)
        {
            return false;
        }

        Span<char> canonical = stackalloc char[Length];
        var length = 0;

        foreach (var character in input)
        {
            if (!char.IsLetterOrDigit(character))
            {
                // Whatever the reader used to break the code up. Dashes are what we print, but a
                // code pasted back from a chat window has been through other hands.
                continue;
            }

            if (length == Length)
            {
                return false;
            }

            var folded = char.ToUpperInvariant(character) switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                var other => other
            };

            if (!_alphabet.Contains(folded))
            {
                return false;
            }

            canonical[length++] = folded;
        }

        if (length != Length)
        {
            return false;
        }

        code = new InviteCode(new string(canonical));
        return true;
    }

    public static string Format(InviteCode code)
    {
        return string.Join('-', Enumerable
            .Range(0, code.Value.Length / GroupSize)
            .Select(x => code.Value.Substring(x * GroupSize, GroupSize)));
    }
}
