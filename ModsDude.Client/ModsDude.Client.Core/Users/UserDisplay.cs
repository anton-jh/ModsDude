using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Globalization;
using System.Text;

namespace ModsDude.Client.Core.Users;

/// <summary>
/// How a user is drawn: their own name, an avatar colour that is theirs everywhere, and a tag shown
/// only where the list they are in actually needs it.
/// </summary>
/// <remarks>
/// Ambiguity is a property of the list, not of the person, so it is decided here at the moment of
/// rendering rather than baked into anybody's name. A repo where no two members share a name never
/// shows a tag at all; one where two Antons meet shows the tag on <i>both</i> of them, because
/// neither of them is the Anton and the other one the duplicate.
/// </remarks>
public static class UserDisplay
{
    /// <summary>
    /// The ids of the users in <paramref name="users"/> who share their display name with somebody
    /// else in the same set - the only ones whose tag is worth the space.
    /// </summary>
    public static IReadOnlySet<string> FindAmbiguous(IEnumerable<UserDto> users)
    {
        return users
            .GroupBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The avatar colour for a tag, as a hex string WPF can bind straight onto a brush.
    /// </summary>
    /// <remarks>
    /// Hues are walked by the golden angle so that consecutive tags land far apart on the wheel
    /// rather than in the same wedge of it, and saturation and lightness are fixed at a pair that
    /// carries white text on a dark card. The tag is the input rather than the id so that the colour
    /// and the digits under it always agree: two people who look different are different.
    /// </remarks>
    public static string ColorFor(string tag)
    {
        var index = int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        return FromHsl(index * 137.508 % 360, 0.52, 0.58);
    }

    /// <summary>The single character drawn on the avatar. A name that is all punctuation gets none.</summary>
    public static string InitialFor(string displayName)
    {
        foreach (var rune in displayName.Trim().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                return Rune.ToUpper(rune, CultureInfo.CurrentCulture).ToString();
            }
        }

        return string.Empty;
    }


    private static string FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var secondary = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = lightness - chroma / 2;

        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };

        return $"#{ToByte(red + offset):X2}{ToByte(green + offset):X2}{ToByte(blue + offset):X2}";
    }

    private static int ToByte(double value)
    {
        return (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
