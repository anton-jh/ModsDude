using System.Xml;
using System.Xml.Linq;

namespace ModsDude.Client.Core.Helpers;

/// <summary>Reads XML that came off somebody else's disk: no DTDs, and nothing resolved from outside it.</summary>
internal static class SafeXml
{
    public static XDocument Load(Stream stream)
    {
        using var reader = XmlReader.Create(stream, CreateSettings());

        return XDocument.Load(reader);
    }

    public static XDocument Load(string path)
    {
        using var reader = XmlReader.Create(path, CreateSettings());

        return XDocument.Load(reader);
    }


    private static XmlReaderSettings CreateSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };
}
