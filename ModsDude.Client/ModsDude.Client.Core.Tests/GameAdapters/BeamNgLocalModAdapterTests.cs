using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Tests.Sync;
using System.IO.Compression;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class BeamNgLocalModAdapterTests
{
    private static readonly string _hash = "0123456789abcdef" + new string('0', 48);


    [Fact]
    public void Each_folder_named_in_the_settings_is_a_target()
    {
        var targets = Adapter(game: "G", server: "S", cache: "C").ModTargets;

        Assert.Equal([BeamNgTarget.Game, BeamNgTarget.Server, BeamNgTarget.Client], targets.Select(x => x.Key));
        Assert.Equal(["Game", "BeamMP server", "BeamMP client"], targets.Select(x => x.DisplayName));
        Assert.Equal(["G", "S", "C"], targets.Select(x => x.Path));
    }

    [Fact]
    public void A_blank_folder_is_no_target_and_none_at_all_is_an_answer()
    {
        Assert.Equal([BeamNgTarget.Server], Adapter(game: " ", server: "S").ModTargets.Select(x => x.Key));
        Assert.Empty(Adapter().ModTargets);
    }

    [Fact]
    public void Only_the_launcher_cache_is_shared_and_no_target_hardlinks_yet()
    {
        var targets = Adapter(game: "G", server: "S", cache: "C").ModTargets;

        Assert.Equal([false, false, true], targets.Select(x => x.Shared));
        Assert.All(targets, x => Assert.False(x.SupportsHardlinks));
    }

    [Fact]
    public void The_game_and_the_server_get_the_registered_name()
    {
        foreach (var target in Adapter(game: "G", server: "S").ModTargets)
        {
            Assert.Equal("Roamup.zip", Place(target, Desired("roamup", registered: "Roamup.zip")));
        }
    }

    [Fact]
    public void The_launcher_cache_gets_the_registered_name_with_the_start_of_the_hash()
    {
        var cache = Target(BeamNgTarget.Client);

        Assert.Equal("Roamup-01234567.zip", Place(cache, Desired("roamup", registered: "Roamup.zip")));
    }

    [Fact]
    public void Without_a_registered_name_the_cache_keeps_the_installed_one_under_the_current_hash()
    {
        var cache = Target(BeamNgTarget.Client);

        Assert.Equal("Roamup-01234567.zip", Place(cache, Desired("roamup", installed: "Roamup-ffffffff.zip")));
    }

    [Fact]
    public void Without_any_name_a_mod_gets_its_id()
    {
        Assert.Equal("roamup.zip", Place(Target(BeamNgTarget.Game), Desired("roamup")));
    }

    [Fact]
    public void Only_the_game_folder_has_its_mod_list_switched_on()
    {
        var adapter = Adapter(game: "G", server: "S", cache: "C");

        foreach (var target in adapter.ModTargets)
        {
            var edits = adapter.Layout(new ModLayoutContext(target, [Desired("roamup")])).ManagedFiles;

            if (target.Key == BeamNgTarget.Game)
            {
                Assert.Equal(BeamNgModDatabase.FileName, Assert.Single(edits).RelativePath);
            }
            else
            {
                Assert.Empty(edits);
            }
        }
    }

    [Fact]
    public async Task Installed_mods_in_the_launcher_cache_go_by_the_server_name()
    {
        using var folder = new TempDirectory("beamng-cache");
        WriteMod(folder.Combine("Roamup-0123abcd.zip"));

        var adapter = Adapter(cache: folder.Path);
        var mod = Assert.Single(await adapter.GetInstalledMods(adapter.ModTargets.Single(), _ => false, CancellationToken.None));

        Assert.Equal(ModKey.From("roamup"), mod.Id);
        Assert.Equal("Roamup", mod.Name);
    }

    [Fact]
    public async Task Installed_mods_elsewhere_keep_their_whole_name()
    {
        using var folder = new TempDirectory("beamng-server");
        WriteMod(folder.Combine("Roamup-0123abcd.zip"));

        var adapter = Adapter(server: folder.Path);
        var mod = Assert.Single(await adapter.GetInstalledMods(adapter.ModTargets.Single(), _ => false, CancellationToken.None));

        Assert.Equal(ModKey.From("roamup-0123abcd"), mod.Id);
    }


    private static BeamNgLocalModAdapter Adapter(string? game = null, string? server = null, string? cache = null)
        => new(new BeamNgLocalSettings { GameModsFolder = game, ServerModsFolder = server, LauncherCacheFolder = cache });

    private static ModTarget Target(TargetKey key)
        => Adapter(game: "G", server: "S", cache: "C").ModTargets[key]!;

    private static string Place(ModTarget target, ModLayoutMod mod)
        => Assert.Single(Adapter(game: "G", server: "S", cache: "C").Layout(new ModLayoutContext(target, [mod])).Placements).FileName;

    private static ModLayoutMod Desired(string modId, string? registered = null, string? installed = null)
    {
        var id = ModKey.From(modId);

        return new ModLayoutMod(id, ModVersionKey.From("1.0"), _hash, registered is null ? null : ModFileName.For(id, registered), installed, Locked: false);
    }

    private static void WriteMod(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("vehicles/roamer/roamup.jbeam");
    }
}
