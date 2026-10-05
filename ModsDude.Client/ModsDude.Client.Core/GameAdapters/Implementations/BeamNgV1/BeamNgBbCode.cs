using System.Text.RegularExpressions;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// The BBCode the game's repository writes mod descriptions in, reduced to the text a reader sees.
/// </summary>
/// <remarks>
/// Embeds - a video, an attached screenshot, an inline image - go entirely, since what is between
/// their tags is an id or an address rather than words. Every other tag goes and keeps its text.
/// </remarks>
internal static partial class BeamNgBbCode
{
    public static string? ToPlainText(string? bbCode)
    {
        if (bbCode is null)
        {
            return null;
        }

        var text = Embed().Replace(bbCode, "");
        text = Tag().Replace(text, "");
        text = text.Replace("\r\n", "\n");
        text = BlankLines().Replace(text, "\n\n");

        return text.Trim();
    }


    [GeneratedRegex(@"\[(MEDIA|ATTACH|IMG)(=[^\]]*)?\].*?\[/\1\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Embed();

    [GeneratedRegex(@"\[/?[a-z*]+(=[^\]]*)?\]", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*){2,}")]
    private static partial Regex BlankLines();
}
