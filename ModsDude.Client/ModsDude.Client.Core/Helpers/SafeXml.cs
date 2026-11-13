using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.Helpers;

/// <summary>Reads XML that came off somebody else's disk: no DTDs, and nothing resolved from outside it.</summary>
internal static class SafeXml
{
    public static XDocument Load(Stream stream, LoadOptions options = LoadOptions.None)
    {
        using var reader = XmlReader.Create(stream, CreateSettings());

        return XDocument.Load(reader, options);
    }

    /// <param name="share">How others may use the file meanwhile; a game may hold its own files open.</param>
    public static XDocument Load(string path, FileShare share = FileShare.Read)
    {
        // Opened as a file rather than handed to XmlReader as a string, which would read it as a URI.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);

        return Load(stream);
    }


    private static XmlReaderSettings CreateSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };
}
