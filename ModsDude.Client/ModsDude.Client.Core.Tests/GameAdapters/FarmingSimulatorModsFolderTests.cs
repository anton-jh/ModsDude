using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// Farming Simulator loads mods from the <c>mods</c> folder in its data folder unless
/// <c>gameSettings.xml</c> overrides it, and sync has to put them wherever the game will look.
/// </summary>
public class FarmingSimulatorModsFolderTests : IDisposable
{
    private readonly TempDirectory _gameData = new("fs-mods-folder");


    [Fact]
    public void A_game_that_has_never_saved_its_settings_uses_the_mods_folder_inside_its_data_folder()
    {
        Assert.Equal(DefaultFolder, FindModsFolder());
    }

    [Fact]
    public void An_override_that_is_switched_off_is_ignored()
    {
        WriteSettings("""<modsDirectoryOverride active="false" directory="C:/Temp"/>""");

        Assert.Equal(DefaultFolder, FindModsFolder());
    }

    [Fact]
    public void An_override_that_is_switched_on_is_where_the_mods_are()
    {
        WriteSettings("""<modsDirectoryOverride active="true" directory="D:/Games/FS25 Mods"/>""");

        Assert.Equal(@"D:\Games\FS25 Mods", FindModsFolder());
    }

    [Fact]
    public void An_override_switched_on_with_no_directory_is_as_good_as_off()
    {
        WriteSettings("""<modsDirectoryOverride active="true" directory=""/>""");

        Assert.Equal(DefaultFolder, FindModsFolder());
    }

    [Fact]
    public void Settings_with_no_override_at_all_use_the_default()
    {
        WriteSettings("<defaultMultiplayerPort>10823</defaultMultiplayerPort>");

        Assert.Equal(DefaultFolder, FindModsFolder());
    }

    /// <summary>
    /// Refused rather than read as "no override": a half-written file says nothing about where the
    /// game loads mods from, and guessing the default could be guessing a folder full of other mods.
    /// </summary>
    [Fact]
    public void A_settings_file_that_will_not_parse_is_refused()
    {
        File.WriteAllText(Path.Combine(_gameData.Path, "gameSettings.xml"), "<gameSettings><modsDirectoryOverride active=");

        Assert.Throws<UserFriendlyException>(FindModsFolder);
    }

    [Fact]
    public void The_local_mod_adapter_targets_the_overridden_folder()
    {
        WriteSettings("""<modsDirectoryOverride active="true" directory="D:/Games/FS25 Mods"/>""");

        var adapter = new FarmingSimulatorLocalModAdapter(FarmingSimulatorGameVersion.Fs25, _gameData.Path);

        Assert.Equal(@"D:\Games\FS25 Mods", adapter.ModTargets.Single().Path);
    }


    private string DefaultFolder => Path.Combine(_gameData.Path, "mods");

    private string FindModsFolder() => FarmingSimulatorGameDataFolder.FindModsFolder(_gameData.Path);

    private void WriteSettings(string body)
    {
        File.WriteAllText(
            Path.Combine(_gameData.Path, "gameSettings.xml"),
            $"""
            <?xml version="1.0" encoding="utf-8" standalone="no" ?>
            <gameSettings revision="15">
                {body}
            </gameSettings>
            """);
    }

    public void Dispose() => _gameData.Dispose();
}
