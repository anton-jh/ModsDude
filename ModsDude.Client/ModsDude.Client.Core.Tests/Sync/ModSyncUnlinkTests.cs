using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// A target whose hardlinks were switched off after files were linked into it: the next apply turns
/// each of them into a copy of its own.
/// </summary>
public class ModSyncUnlinkTests
{
    [Fact]
    public async Task A_linked_file_on_a_target_that_no_longer_allows_links_is_unlinked()
    {
        using var fixture = await LinkedFixtureAsync();
        fixture.Adapter.Target = fixture.Adapter.Target with { SupportsHardlinks = false };

        var plan = await fixture.PlanAsync();

        Assert.Equal(ModSyncAction.Unlink, Assert.Single(plan.Items).Action);
        Assert.True(plan.HasWork);

        var result = await fixture.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Equal(1, FileLinks.TryGetLinkCount(fixture.Folder.Combine("fs25_a.zip")));
        Assert.Equal(Mod("1.0.0", "a"), fixture.ReadInstalled("fs25_a.zip"));
    }

    [Fact]
    public async Task An_unlinked_folder_is_in_sync_and_needs_nothing_more()
    {
        using var fixture = await LinkedFixtureAsync();
        fixture.Adapter.Target = fixture.Adapter.Target with { SupportsHardlinks = false };
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(DriftStatus.InSync, fixture.CheckDrift().Status);
        Assert.False((await fixture.PlanAsync()).HasWork);
    }

    [Fact]
    public async Task Writing_to_the_unlinked_file_leaves_the_store_alone()
    {
        using var fixture = await LinkedFixtureAsync();
        fixture.Adapter.Target = fixture.Adapter.Target with { SupportsHardlinks = false };
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        File.WriteAllText(fixture.Folder.Combine("fs25_a.zip"), "rewritten in place");

        var hash = SyncTestContent.HashOf(Mod("1.0.0", "a"));
        Assert.Equal(Mod("1.0.0", "a"), File.ReadAllText(fixture.ServingStore.GetBlobPath(hash)));
    }

    [Fact]
    public async Task A_linked_file_on_a_target_that_allows_links_is_kept()
    {
        using var fixture = await LinkedFixtureAsync();

        var plan = await fixture.PlanAsync();

        Assert.Equal(ModSyncAction.Keep, Assert.Single(plan.Items).Action);
    }

    [Fact]
    public async Task A_copied_file_is_kept()
    {
        using var fixture = new SyncFixture();
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        var plan = await fixture.PlanAsync();

        Assert.Equal(ModSyncAction.Keep, Assert.Single(plan.Items).Action);
    }


    private static async Task<SyncFixture> LinkedFixtureAsync()
    {
        var fixture = new SyncFixture(supportsHardlinks: true);
        fixture.Server.Pin("fs25_a", "1.0.0", Mod("1.0.0", "a"));
        await fixture.ExecuteAsync(await fixture.PlanAsync());

        Assert.Equal(2, FileLinks.TryGetLinkCount(fixture.Folder.Combine("fs25_a.zip")));

        return fixture;
    }

    private static string Mod(string version, string build) => SyncTestContent.File(version, build);
}
