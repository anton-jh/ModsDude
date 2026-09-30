using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.GameAdapters;

public class FarmingSimulatorModLayoutTests
{
    [Fact]
    public void A_mod_is_placed_under_the_name_the_repo_registered()
    {
        var placement = Place(Desired("fs25_a", registered: "FS25_A.zip", installed: "fs25_a.zip"));

        Assert.Equal("FS25_A.zip", placement.FileName);
    }

    [Fact]
    public void Without_a_registered_name_an_installed_file_keeps_its_name()
    {
        var placement = Place(Desired("fs25_a", registered: null, installed: "FS25_A_old.zip"));

        Assert.Equal("FS25_A_old.zip", placement.FileName);
    }

    [Fact]
    public void Without_a_registered_or_installed_name_the_mod_id_is_used()
    {
        var placement = Place(Desired("fs25_a", registered: null, installed: null));

        Assert.Equal("fs25_a.zip", placement.FileName);
    }

    [Fact]
    public void Farming_simulator_needs_no_other_files_changed()
    {
        var layout = Adapter().Layout(new ModLayoutContext(Target(), [Desired("fs25_a", null, null)]));

        Assert.Empty(layout.ManagedFiles);
    }


    private static ModPlacement Place(ModLayoutMod mod)
        => Assert.Single(Adapter().Layout(new ModLayoutContext(Target(), [mod])).Placements);

    private static ModLayoutMod Desired(string modId, string? registered, string? installed)
    {
        var id = Keys.Mod(modId);

        return new ModLayoutMod(id, Keys.V("1.0.0"), registered is null ? null : ModFileName.For(id, registered), installed, Locked: false);
    }

    private static FarmingSimulatorLocalModAdapter Adapter()
        => new(FarmingSimulatorGameVersion.Fs25, Path.GetTempPath());

    private static ModTarget Target() => new(new TargetKey("mods"), "Mod folder", Path.GetTempPath());
}
