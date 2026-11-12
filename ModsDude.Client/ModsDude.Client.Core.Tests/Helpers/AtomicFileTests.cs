using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.Helpers;

public class AtomicFileTests
{
    [Fact]
    public void Leftovers_of_interrupted_writes_to_the_file_are_deleted_and_nothing_else()
    {
        using var folder = new TempDirectory("atomic-leftovers");

        var path = folder.WriteFile("state.json", "{}");
        var leftover = folder.WriteFile($"state.json.{Guid.NewGuid():N}.tmp", "{ half");
        var otherFilesLeftover = folder.WriteFile($"other.json.{Guid.NewGuid():N}.tmp", "{ half");
        var notOurs = folder.WriteFile("state.json.backup.tmp", "the user's");

        AtomicFile.DeleteLeftovers(path, NullLogger.Instance);

        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(otherFilesLeftover));
        Assert.True(File.Exists(notOurs));
    }
}
