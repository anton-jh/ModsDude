using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// A game whose folder was last applied from one repo, now applied from another: the outgoing repo's
/// mods can be fetched again from there, so they are kept in the store rather than recycled.
/// </summary>
public class ModSyncRepoSwitchTests
{
    private readonly Guid _previousRepo = Guid.NewGuid();


    [Fact]
    public async Task Mods_from_the_repo_the_folder_was_applied_from_are_uninstalled_into_the_store()
    {
        using var fixture = new SyncFixture();
        fixture.Server.RegisterIn(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.InstallAppliedFrom(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));

        var plan = await fixture.PlanAsync();

        Assert.Contains(plan.Items, x => x.ModId.Value == "fs25_old" && x.Action is ModSyncAction.UninstallRecoverable);
        Assert.Empty(plan.Unrecognised);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Empty(fixture.RecycleBin.Recycled);
        Assert.Equal(["fs25_a.zip"], fixture.FolderContents());
        Assert.True(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "old"))));
    }

    [Fact]
    public async Task Switching_back_installs_the_outgoing_mods_from_the_store_without_downloading()
    {
        using var fixture = new SyncFixture();
        fixture.Server.RegisterIn(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.InstallAppliedFrom(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));

        await fixture.ExecuteAsync(await fixture.PlanAsync());

        fixture.Server.Pin("fs25_old", "1.0.0", Mod("1.0.0", "old"));
        var downloadsBefore = fixture.Downloader.Downloads;

        var plan = await fixture.PlanAsync();

        Assert.Empty(plan.HashesToFetch);
        Assert.True((await fixture.ExecuteAsync(plan)).Completed);
        Assert.Equal(downloadsBefore, fixture.Downloader.Downloads);
        Assert.Equal(Mod("1.0.0", "old"), fixture.ReadInstalled("fs25_old.zip"));
    }

    [Fact]
    public async Task A_file_changed_since_the_other_repos_apply_still_goes_to_the_recycle_bin()
    {
        using var fixture = new SyncFixture();
        fixture.Server.RegisterIn(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.InstallAppliedFrom(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Install("fs25_old.zip", Mod("1.0.0", "edited by hand"));

        var plan = await fixture.PlanAsync();

        Assert.Contains(plan.Items, x => x.ModId.Value == "fs25_old" && x.Action is ModSyncAction.Quarantine);

        await fixture.ExecuteAsync(plan);

        Assert.Single(fixture.RecycleBin.Recycled);
    }

    [Fact]
    public async Task Mods_from_a_repo_whose_list_is_refused_go_to_the_recycle_bin()
    {
        using var fixture = new SyncFixture();
        fixture.Server.RegisterIn(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));
        fixture.Server.Refuse(_previousRepo);
        fixture.InstallAppliedFrom(_previousRepo, "fs25_old", "1.0.0", Mod("1.0.0", "old"));

        var plan = await fixture.PlanAsync();

        Assert.Contains(plan.Items, x => x.ModId.Value == "fs25_old" && x.Action is ModSyncAction.Quarantine);

        await fixture.ExecuteAsync(plan);

        Assert.Single(fixture.RecycleBin.Recycled);
        Assert.False(fixture.ServingStore.Contains(SyncTestContent.HashOf(Mod("1.0.0", "old"))));
    }


    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
