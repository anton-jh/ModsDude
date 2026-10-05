using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class BeamNgNamingTests
{
    [Fact]
    public void The_cache_name_is_the_server_name_with_the_start_of_the_hash()
    {
        Assert.Equal("Truck-1a2b3c4d.zip", BeamMpCacheName.For("Truck.zip", "1a2b3c4d" + new string('0', 56)));
    }

    [Theory]
    [InlineData("Truck-1a2b3c4d.zip", "Truck.zip")]
    [InlineData("Truck.zip", "Truck.zip")]
    [InlineData("Truck-1a2b3c4d-1a2b3c4d.zip", "Truck-1a2b3c4d.zip")]
    [InlineData("Truck-1A2B3C4D.zip", "Truck-1A2B3C4D.zip")]
    [InlineData("Truck-v2.zip", "Truck-v2.zip")]
    public void A_cached_name_loses_one_hash_suffix(string cached, string server)
    {
        Assert.Equal(server, BeamMpCacheName.ServerFileName(cached));
    }

    [Fact]
    public void Repository_bbcode_reads_as_plain_text()
    {
        var text = BeamNgBbCode.ToPlainText(
            "[CENTER][SIZE=5][B]Title[/B][/SIZE][/CENTER]\r\n\r\n\r\n\r\nSee [URL='https://x']here[/URL].\n[ATTACH]799790[/ATTACH][MEDIA=youtube]abc[/MEDIA]");

        Assert.Equal("Title\n\nSee here.", text);
    }

    [Fact]
    public void The_launcher_cache_is_where_its_settings_say()
    {
        using var launcher = new TempDirectory("beammp-launcher");
        launcher.WriteFile("Launcher.cfg", """{ "Port": 4444, "CachingDirectory": "./Cache" }""");
        var cache = launcher.CreateSubdirectory("Cache");

        Assert.Equal(cache, BeamNgFolders.FindLauncherCacheFolder(launcher.Path, NullLogger.Instance));
    }

    [Fact]
    public void Without_a_setting_the_launcher_cache_is_beside_the_launcher()
    {
        using var launcher = new TempDirectory("beammp-launcher");
        launcher.WriteFile("Launcher.cfg", """{ "Port": 4444 }""");
        var cache = launcher.CreateSubdirectory("Resources");

        Assert.Equal(cache, BeamNgFolders.FindLauncherCacheFolder(launcher.Path, NullLogger.Instance));
    }

    [Fact]
    public void Unreadable_launcher_settings_fall_back_to_the_default_cache()
    {
        using var launcher = new TempDirectory("beammp-launcher");
        launcher.WriteFile("Launcher.cfg", "{ half");
        var cache = launcher.CreateSubdirectory("Resources");

        Assert.Equal(cache, BeamNgFolders.FindLauncherCacheFolder(launcher.Path, NullLogger.Instance));
    }

    [Fact]
    public void Folders_that_do_not_exist_are_not_suggested()
    {
        using var root = new TempDirectory("beamng-missing");

        Assert.Null(BeamNgFolders.FindLauncherCacheFolder(root.Combine("BeamMP-Launcher"), NullLogger.Instance));
        Assert.Null(BeamNgFolders.FindGameModsFolder(root.Path));
    }

    [Fact]
    public void The_game_mods_folder_is_in_the_current_user_folder()
    {
        using var root = new TempDirectory("beamng-local");
        var mods = root.CreateSubdirectory(Path.Combine("BeamNG", "BeamNG.drive", "current", "mods"));

        Assert.Equal(mods, BeamNgFolders.FindGameModsFolder(root.Path));
    }
}
