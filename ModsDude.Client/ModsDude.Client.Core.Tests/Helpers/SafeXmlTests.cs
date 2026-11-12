using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Tests.Helpers;

public class SafeXmlTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("safe-xml %41 tests").FullName;


    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(_folder, true);
    }


    [Fact]
    public void A_path_is_read_as_a_path_and_not_as_a_uri()
    {
        var path = Path.Combine(_folder, "gameSettings%20.xml");
        File.WriteAllText(path, "<settings><value>1</value></settings>");

        Assert.Equal("1", SafeXml.Load(path).Root?.Element("value")?.Value);
    }
}
