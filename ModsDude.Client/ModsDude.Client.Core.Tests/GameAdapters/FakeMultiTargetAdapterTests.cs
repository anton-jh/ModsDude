using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The multi-target matrix, which Farming Simulator cannot reach and no real adapter exists for yet.
/// </summary>
/// <remarks>
/// These are tests of the fake, which sounds like testing the test until you notice what they are
/// actually holding: that a target is derived from settings rather than stored, that emptying a field
/// removes one, and that the two halves of a target are independently optional. Everything after
/// slice 1 is built on those three facts.
/// </remarks>
public class FakeMultiTargetAdapterTests
{
    /// <summary>
    /// A game somebody connected and then pointed at nothing. Not an error - there is simply nothing
    /// to sync - which is why <c>ModTargets</c> is a list rather than a folder.
    /// </summary>
    [Fact]
    public void A_game_whose_settings_point_at_nothing_reaches_no_target()
    {
        Assert.Empty(FakeMultiTargetGameAdapter.ModAdapterFor(new FakeMultiTargetSettings()).ModTargets);
    }

    [Fact]
    public void One_filled_field_is_one_target_and_narrow_callers_are_happy()
    {
        var targets = FakeMultiTargetGameAdapter.ModAdapterFor(new FakeMultiTargetSettings
        {
            SoloModFolder = @"C:\solo\mods"
        }).ModTargets;

        Assert.Equal(@"C:\solo\mods", targets.RequireSingleTarget().Path);
    }

    /// <summary>The shape the whole phase is for, and the one nothing could reach before this fake.</summary>
    [Fact]
    public void Three_filled_fields_are_three_targets_and_narrow_callers_are_not()
    {
        var targets = FakeMultiTargetGameAdapter.ModAdapterFor(Everything()).ModTargets;

        Assert.Equal(3, targets.Count);
        Assert.Throws<InvalidOperationException>(() => targets.RequireSingleTarget());
    }

    /// <summary>
    /// The transition the orphans live in. A settings edit is all it takes for a target with a
    /// manifest behind it - and possibly a savegame this machine is still holding - to stop existing,
    /// and nothing anywhere is asked first.
    /// </summary>
    [Fact]
    public void Emptying_a_field_takes_its_target_away()
    {
        var settings = Everything();

        settings.ServerModFolder = null;
        settings.ServerSavegameFolder = null;

        var targets = FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets;

        Assert.Equal(2, targets.Count);
        Assert.Null(targets[FakeMultiTargetSettings.Server]);
    }

    /// <summary>
    /// And it takes away only its own. A target is addressed by a key an adapter author chose, not by
    /// a position in a list, so removing one does not renumber the others onto each other's
    /// manifests.
    /// </summary>
    [Fact]
    public void Removing_a_target_leaves_the_others_where_they_were()
    {
        var settings = Everything();
        var before = FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets;

        settings.ServerModFolder = null;
        settings.ServerSavegameFolder = null;

        var after = FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets;

        Assert.Equal(
            before[FakeMultiTargetSettings.Client]?.Path,
            after[FakeMultiTargetSettings.Client]?.Path);
    }

    /// <summary>A folder somebody cleared and one they never filled in are the same thing.</summary>
    [Fact]
    public void A_field_holding_only_whitespace_is_an_empty_field()
    {
        var targets = FakeMultiTargetGameAdapter.ModAdapterFor(new FakeMultiTargetSettings
        {
            SoloModFolder = "   "
        }).ModTargets;

        Assert.Empty(targets);
    }

    /// <summary>
    /// The half of the pairing sync never sees. A target with savegames and no mods is ordinary - the
    /// MP client whose mods the server's copy serves - and it is a target, so a save in its folder
    /// still belongs to it.
    /// </summary>
    [Fact]
    public void A_target_holding_savegames_and_no_mods_is_not_a_mod_target()
    {
        var settings = new FakeMultiTargetSettings { ClientSavegameFolder = @"C:\client\saves" };
        var adapter = LocalAdapterFor(settings);

        Assert.Empty(FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets);
        Assert.Equal(FakeMultiTargetSettings.Client, Assert.Single(adapter.Targets).Key);
    }

    [Fact]
    public void A_target_holding_mods_and_no_savegames_is_ordinary()
    {
        var settings = new FakeMultiTargetSettings { ServerModFolder = @"C:\server\mods" };

        Assert.Null(Assert.Single(LocalAdapterFor(settings).Targets).SavegameFolder);
        Assert.Single(FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets);
    }

    /// <summary>
    /// The transition worth telling apart from a removal. Clearing only the mod folder leaves a
    /// target that still holds savegames, so the manifest behind it is stale and droppable while a
    /// binding behind it is a savegame this machine is still holding.
    /// </summary>
    [Fact]
    public void Clearing_only_the_mod_folder_leaves_the_target_holding_its_savegames()
    {
        var settings = Everything();

        settings.ClientModFolder = null;

        Assert.Null(FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets[FakeMultiTargetSettings.Client]);
        Assert.Equal(
            @"C:\client\saves",
            LocalAdapterFor(settings).Targets.Single(x => x.Key == FakeMultiTargetSettings.Client).SavegameFolder);
    }

    /// <summary>
    /// Deliberately a test of constants. An adapter author changing 'client' to 'multiplayer' orphans
    /// every manifest and every savegame binding keyed on it, on every member's machine, and nothing
    /// downstream can tell that from the target having been removed - so the keys are held here where
    /// changing one has to be a decision rather than a rename.
    /// </summary>
    [Fact]
    public void The_keys_are_a_contract_and_do_not_move()
    {
        Assert.Equal(
            ["server", "client", "solo"],
            FakeMultiTargetGameAdapter.ModAdapterFor(Everything()).ModTargets.Select(x => x.Key.Value));
    }

    /// <summary>
    /// Targets survive the trip the app actually makes them take: settings are persisted as a string
    /// and the adapter is rehydrated from it, so a target that only existed in a live object would
    /// work in a test and nowhere else.
    /// </summary>
    [Fact]
    public void Targets_survive_being_serialized_and_read_back()
    {
        var settings = Everything();
        var rehydrated = FakeMultiTargetSettings.Deserialize(settings.Serialize());

        Assert.Equal(
            FakeMultiTargetGameAdapter.ModAdapterFor(settings).ModTargets.Select(x => (x.Key, x.Path)),
            FakeMultiTargetGameAdapter.ModAdapterFor(rehydrated).ModTargets.Select(x => (x.Key, x.Path)));
    }

    /// <summary>
    /// Named, because with three of them the interesting half of any notice is which one it is about.
    /// </summary>
    [Fact]
    public void A_game_with_several_targets_names_them()
    {
        Assert.All(
            FakeMultiTargetGameAdapter.ModAdapterFor(Everything()).ModTargets,
            x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayName)));
    }


    private static FakeMultiTargetLocalGameAdapter LocalAdapterFor(FakeMultiTargetSettings settings)
    {
        return (FakeMultiTargetLocalGameAdapter)new FakeMultiTargetGameAdapter()
            .WithBaseSettings(new EmptyAdapterSettings())
            .WithLocalSettings(settings.Serialize());
    }

    private static FakeMultiTargetSettings Everything() => new()
    {
        ServerModFolder = @"C:\server\mods",
        ServerSavegameFolder = @"C:\server\saves",
        ClientModFolder = @"C:\client\mods",
        ClientSavegameFolder = @"C:\client\saves",
        SoloModFolder = @"C:\solo\mods",
        SoloSavegameFolder = @"C:\solo\saves"
    };
}
